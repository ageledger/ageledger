using System.Text.Json;
using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Ageledger;

/// <remarks>An implementation used with <see cref="AgeSignalsService"/> must not call <see cref="AgeSignalsService.EvaluateAsync"/> on that same service (not supported: it would recurse without end).</remarks>
public interface IDecisionLog
{
    /// <summary>Adds one entry.</summary>
    /// <param name="entry">The entry to store.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="ArgumentNullException"><paramref name="entry"/> is null.</exception>
    Task AppendAsync(DecisionLogEntry entry, CancellationToken cancellationToken = default);
    /// <summary>Returns the stored entries in the order they were appended.</summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    Task<IReadOnlyList<DecisionLogEntry>> ReadAllAsync(CancellationToken cancellationToken = default);
    /// <summary>Returns the log as one JSON array.</summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    Task<string> ExportJsonAsync(CancellationToken cancellationToken = default);
    /// <summary>Deletes every stored entry.</summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    Task ClearAsync(CancellationToken cancellationToken = default);
}

// "+" is written literally ("18+"), so writers in other languages produce identical bytes. Relaxed escaping is safe here because every
// written field is validated to ASCII [A-Za-z0-9._:+-] or is one of the fixed decision names (the timestamp is digits, "-", ":", "." and "T"/"Z"), so
// nothing that needs escaping can reach the encoder. Reading accepts both the literal and the escaped form.
[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(DecisionLogEntry))]
[JsonSerializable(typeof(List<DecisionLogEntry>))]
internal sealed partial class DecisionLogJson : JsonSerializerContext
{
    public static DecisionLogJson Relaxed { get; } = new(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
}

/// <summary>Keeps entries in memory for the life of the process. Unlike <see cref="JsonFileDecisionLog"/> it has no age or count limits.</summary>
public sealed class InMemoryDecisionLog : IDecisionLog
{
    private readonly List<DecisionLogEntry> _entries = [];
    private readonly object _gate = new();

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException"><paramref name="entry"/> is null (nothing is stored).</exception>
    public Task AppendAsync(DecisionLogEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_gate) _entries.Add(entry);
        return Task.CompletedTask;
    }
    /// <inheritdoc/>
    public Task<IReadOnlyList<DecisionLogEntry>> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate) return Task.FromResult<IReadOnlyList<DecisionLogEntry>>(_entries.ToArray());
    }
    /// <inheritdoc/>
    public Task<string> ExportJsonAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate) return Task.FromResult(JsonSerializer.Serialize(_entries, DecisionLogJson.Relaxed.ListDecisionLogEntry));
    }
    /// <inheritdoc/>
    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate) _entries.Clear();
        return Task.CompletedTask;
    }
}

/// <summary>
/// Local JSON Lines file, one <see cref="DecisionLogEntry"/> per line. Capped by age and count; nothing is sent anywhere.
/// Rows past the limits are hidden on read and physically removed on the next append or <see cref="ClearAsync"/>, which also removes leftover temporary files.
/// </summary>
public sealed class JsonFileDecisionLog : IDecisionLog
{
    private readonly string _path;
    private readonly int _maxEntries;
    private readonly TimeSpan _retention;
    private readonly TimeProvider _time;
    // One lock per full path, shared by every instance in the process (separate instances on one file would otherwise lose writes).
    // It does not coordinate separate processes. One lock per distinct path is kept for the process lifetime (fine for one or two log files).
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates =
        new(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate;

    /// <summary>Largest accepted <c>maxEntries</c>.</summary>
    public const int MaxEntriesLimit = 100_000;
    /// <summary>Longest accepted retention, in days (about ten years).</summary>
    public const int MaxRetentionDays = 3650;
    /// <summary>A line longer than this many bytes (not counting the final <c>\n</c>; a <c>\r</c> before it counts) is dropped on read. A valid entry is well under 400.</summary>
    internal const int MaxLineBytes = 512;
    /// <summary>An entry whose timestamp is more than this far ahead of the clock is dropped on read and refused on append.</summary>
    internal static readonly TimeSpan MaxFutureSkew = TimeSpan.FromDays(1);

    /// <param name="path">The log file. The full path is used; the folder is created on the first append.</param>
    /// <param name="maxEntries">Most entries kept, 1 to 100000 (default 1000).</param>
    /// <param name="retention">Longest age kept, 1 to 3650 days (default 365 days).</param>
    /// <param name="time">The clock for retention and the future-timestamp check. It is the log's own: a service whose clock is more than a day ahead of this one gets <see cref="ArgumentException"/> from <see cref="AppendAsync"/> (the service reports <c>Logged = false</c>). Use one <see cref="TimeProvider"/> for both.</param>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null, empty or white space.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxEntries"/> is not 1 to 100000, or <paramref name="retention"/> is not 1 to 3650 days.</exception>
    public JsonFileDecisionLog(string path, int maxEntries = 1000, TimeSpan? retention = null, TimeProvider? time = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxEntries, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxEntries, MaxEntriesLimit);
        if (retention is { } r && (r < TimeSpan.FromDays(1) || r > TimeSpan.FromDays(MaxRetentionDays)))
            throw new ArgumentOutOfRangeException(nameof(retention), "Retention must be 1 to 3650 days.");
        _path = Path.GetFullPath(path);
        _gate = Gates.GetOrAdd(_path, _ => new SemaphoreSlim(1, 1));
        _maxEntries = maxEntries;
        _retention = retention ?? TimeSpan.FromDays(365);
        _time = time ?? TimeProvider.System;
    }

    /// <exception cref="ArgumentNullException"><paramref name="entry"/> is null.</exception>
    /// <exception cref="ArgumentException">The entry's timestamp is more than a day ahead of the clock (such rows would be dropped on read).</exception>
    /// <exception cref="IOException">The file cannot be read or written (storage errors). The service catches this and reports <c>Logged = false</c>; a direct caller sees it.</exception>
    /// <exception cref="UnauthorizedAccessException">The app may not access the file or folder.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled while waiting for the file lock or writing.</exception>
    public async Task AppendAsync(DecisionLogEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.TimestampUtc > _time.GetUtcNow() + MaxFutureSkew) throw new ArgumentException("Entry timestamp is in the future.", nameof(entry));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Rows more than MaxFutureSkew ahead are hidden on read but kept here: a wrong clock must not physically delete them.
            var entries = ReadCore(includeFuture: true);
            entries.Add(entry);
            var cutoff = _time.GetUtcNow() - _retention;
            var kept = entries.Where(e => e.TimestampUtc >= cutoff).TakeLast(_maxEntries).ToList();
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = $"{_path}.{Guid.NewGuid():N}.tmp";
            try
            {
                // UTF-8 without a byte-order mark, "\n" line ends on every OS (the format is fixed, so it does not follow Environment.NewLine).
                var text = string.Concat(kept.Select(e => JsonSerializer.Serialize(e, DecisionLogJson.Relaxed.DecisionLogEntry) + "\n"));
                var bytes = new System.Text.UTF8Encoding(false).GetBytes(text);
                await using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
                {
                    await fs.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                    fs.Flush(flushToDisk: true);   // on disk before the rename, so a crash leaves the old file or the new one, not a truncated one
                }
                File.Move(temp, _path, overwrite: true);
            }
            finally { try { File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }
        finally { _gate.Release(); }
    }

    /// <inheritdoc/>
    /// <exception cref="IOException">The file cannot be read or written (storage errors).</exception>
    /// <exception cref="UnauthorizedAccessException">The app may not access the file or folder.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled while waiting for the file lock or writing.</exception>
    public async Task<IReadOnlyList<DecisionLogEntry>> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return ReadCore(); }
        finally { _gate.Release(); }
    }

    /// <inheritdoc/>
    /// <exception cref="IOException">The file cannot be read or written (storage errors).</exception>
    /// <exception cref="UnauthorizedAccessException">The app may not access the file or folder.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled while waiting for the file lock or writing.</exception>
    public async Task<string> ExportJsonAsync(CancellationToken cancellationToken = default) =>
        JsonSerializer.Serialize(new List<DecisionLogEntry>(await ReadAllAsync(cancellationToken).ConfigureAwait(false)), DecisionLogJson.Relaxed.ListDecisionLogEntry);

    /// <inheritdoc/>
    /// <exception cref="IOException">The file cannot be read or written (storage errors).</exception>
    /// <exception cref="UnauthorizedAccessException">The app may not access the file or folder.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled while waiting for the file lock or writing.</exception>
    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(_path)) File.Delete(_path);
            // Orphan temporary files from an interrupted append hold rows too.
            var dir = Path.GetDirectoryName(_path)!;
            var prefix = Path.GetFileName(_path) + ".";
            // Only the exact shape the writer produces: <filename>.<32 lowercase hex>.tmp (Guid "N" format).
            var orphan = new Regex("^" + Regex.Escape(prefix) + "[0-9a-f]{32}\\.tmp$", RegexOptions.CultureInvariant);
            if (Directory.Exists(dir))
                foreach (var tmp in Directory.EnumerateFiles(dir, "*.tmp").Where(f => orphan.IsMatch(Path.GetFileName(f))).ToList())
                    File.Delete(tmp);
        }
        finally { _gate.Release(); }
    }

    // Splits the file on "\n" without ever holding more than MaxLineBytes + 1 bytes of a line; a longer line is skipped (never yielded). A leading byte-order mark is skipped.
    private static IEnumerable<string> ReadBoundedLines(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096);
        var buffer = new byte[4096];
        var line = new byte[MaxLineBytes + 1];
        var length = 0;
        var tooLong = false;
        var first = true;
        int read;
        while ((read = fs.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (var i = 0; i < read; i++)
            {
                var b = buffer[i];
                if (b == (byte)'\n')
                {
                    if (!tooLong) yield return Decode(line, length, first);
                    length = 0; tooLong = false; first = false;
                }
                else if (!tooLong)
                {
                    if (length == MaxLineBytes) tooLong = true; else line[length++] = b;
                }
            }
        }
        if (length > 0 && !tooLong) yield return Decode(line, length, first);

        static string Decode(byte[] bytes, int count, bool isFirst)
        {
            var skip = isFirst && count >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
            return System.Text.Encoding.UTF8.GetString(bytes, skip, count - skip);
        }
    }

    // Lines that are not valid JSON, repeat a key, or whose five values fail validation are dropped. Extra keys on an otherwise valid line are ignored
    // (not kept), so a hand-edited file cannot add other fields. Age and count limits apply on read as well as on append. Lines longer than MaxLineBytes and entries more than MaxFutureSkew in the future are dropped (future rows stay in the file until their time comes or the retention applies).
    private List<DecisionLogEntry> ReadCore(bool includeFuture = false)
    {
        var list = new List<DecisionLogEntry>();
        if (!File.Exists(_path)) return list;
        var now = _time.GetUtcNow();
        var cutoff = now - _retention;
        var latest = now + MaxFutureSkew;
        foreach (var line in ReadBoundedLines(_path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var e = JsonSerializer.Deserialize(line, DecisionLogJson.Relaxed.DecisionLogEntry);
                if (e is not null && e.TimestampUtc >= cutoff && (includeFuture || e.TimestampUtc <= latest)) list.Add(e);
            }
            catch (Exception) { }   // any per-line failure (bad JSON, failed validation, regex time-out) drops that line only
        }
        return list.Count > _maxEntries ? list.GetRange(list.Count - _maxEntries, _maxEntries) : list;
    }
}

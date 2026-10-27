using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Ageledger;

/// <summary>
/// One logged decision. These five fields are the whole record: there is deliberately no place for a birthdate,
/// install ID, account ID, age-source tier or device identifier. Values are constrained to fixed shapes, which reduces
/// but does not prevent misuse: a caller can still put something unintended into a value that fits the shape.
/// </summary>
[JsonConverter(typeof(DecisionLogEntryConverter))]
public sealed record DecisionLogEntry
{
    /// <summary>The five JSON keys of a record, in the order they are written. There are no others.</summary>
    public static readonly IReadOnlyList<string> FieldNames = ["timestampUtc", "ageBucket", "decision", "appVersion", "ruleId"];

    private static readonly Regex BucketPattern = new(@"^(unknown|withheld|(?<lo>[0-9]{1,2})-(?<hi>[0-9]{1,2})|[0-9]{1,2}\+)\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    // Up to four numeric parts and an optional well-known pre-release tag. No free-text suffix and no "+build" metadata.
    private static readonly Regex VersionPattern = new(@"^[0-9]{1,5}(\.[0-9]{1,5}){0,3}(-(alpha|beta|rc|preview|dev)(\.?[0-9]{1,2})?)?\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex RulePattern = new(@"^[A-Za-z][A-Za-z0-9._:-]{0,47}\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    // 8+ digits, or digit groups separated by . : _ - / (date shapes such as 2010-05-01 or 2010.05.01), anywhere in the value.
    private static readonly Regex LooksLikeDate = new(@"[0-9]{8,}|[0-9]{2,4}[.:_/-][0-9]{1,2}[.:_/-][0-9]{1,4}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    // A run of 8+ hex characters (GUID or hash fragments, which could be identifiers).
    private static readonly Regex HexRun = new(@"[0-9A-Fa-f]{8,}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>When the decision was made, in UTC (whole seconds when written).</summary>
    public DateTimeOffset TimestampUtc { get; }
    /// <summary>The recorded range: "unknown", "withheld", "N+" or "N-M".</summary>
    public string AgeBucket { get; }
    /// <summary>What the app decided.</summary>
    public AgeDecision Decision { get; }
    /// <summary>The app's version, such as "1.4.2" or "1.4.2-beta1".</summary>
    public string AppVersion { get; }
    /// <summary>A short rule label such as "gate-v1".</summary>
    public string RuleId { get; }

    /// <summary>Creates an entry after checking every field against its fixed shape.</summary>
    /// <param name="timestampUtc">The time, which must be UTC (offset zero) and not the default value.</param>
    /// <param name="ageBucket">"unknown", "withheld", "N+" or "N-M" with N lower than M (one or two digits); see docs/decision-log-format.md.</param>
    /// <param name="decision">A defined <see cref="AgeDecision"/>.</param>
    /// <param name="appVersion">Looks like "1.4.2" or "1.4.2-beta1" (up to four numeric parts, optional alpha/beta/rc/preview/dev tag); see docs/decision-log-format.md.</param>
    /// <param name="ruleId">A short identifier such as "gate-v1" that starts with a letter and has no date shape, long number or hex run; see docs/decision-log-format.md.</param>
    /// <exception cref="ArgumentException">Any value is missing, not UTC, undefined or not in its allowed shape.</exception>
    public DecisionLogEntry(DateTimeOffset timestampUtc, string ageBucket, AgeDecision decision, string appVersion, string ruleId)
    {
        if (timestampUtc == default) throw new ArgumentException("Timestamp is missing.", nameof(timestampUtc));
        if (timestampUtc.Offset != TimeSpan.Zero) throw new ArgumentException("Timestamp must be UTC.", nameof(timestampUtc));
        ValidateAgeBucket(ageBucket);
        if (!Enum.IsDefined(decision)) throw new ArgumentException("Unknown decision.", nameof(decision));
        ValidateAppVersion(appVersion);
        ValidateRuleId(ruleId);
        TimestampUtc = timestampUtc;
        AgeBucket = ageBucket;
        Decision = decision;
        AppVersion = appVersion;
        RuleId = ruleId;
    }

    /// <summary>Throws <see cref="ArgumentException"/> unless the value is "unknown", "withheld", "N+" or "N-M" with N lower than M (one or two digits).</summary>
    internal static void ValidateAgeBucket(string? ageBucket)
    {
        var m = BucketPattern.Match(ageBucket ?? "");
        var ok = m.Success && (!m.Groups["lo"].Success
            || (int.TryParse(m.Groups["lo"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var lo)
                && int.TryParse(m.Groups["hi"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var hi)
                && lo < hi));
        if (!ok) throw new ArgumentException("Age bucket must look like \"13-15\" (lower below upper), \"18+\", \"unknown\" or \"withheld\".", nameof(ageBucket));
    }

    /// <summary>Throws <see cref="ArgumentException"/> unless the value looks like "1.4.2" or "1.4.2-beta1".</summary>
    internal static void ValidateAppVersion(string? appVersion)
    {
        if (!VersionPattern.IsMatch(appVersion ?? ""))
            throw new ArgumentException("App version must look like \"1.4.2\" or \"1.4.2-beta1\" (up to four numeric parts, optional alpha/beta/rc/preview/dev tag).", nameof(appVersion));
    }

    /// <summary>Throws <see cref="ArgumentException"/> unless the value is a short identifier such as "gate-v1" that is not date-like, number-like or hex-run-like.</summary>
    internal static void ValidateRuleId(string? ruleId)
    {
        if (!RulePattern.IsMatch(ruleId ?? "") || LooksLikeDate.IsMatch(ruleId!) || HexRun.IsMatch(ruleId!))
            throw new ArgumentException("Rule id must be a short identifier such as \"gate-v1\" (starts with a letter; no dates, long numbers or hex runs of 8 or more characters).", nameof(ruleId));
    }

    /// <summary>Builds the entry for a result and the decision made from it, using the app version, rule id and bucket-logging setting in the options.</summary>
    /// <param name="result">The platform result.</param>
    /// <param name="decision">The decision the app made.</param>
    /// <param name="options">Supplies the app version, rule id and <see cref="AgeSignalsOptions.AgeBucketLogging"/>.</param>
    /// <param name="nowUtc">The time to record (truncated to whole seconds).</param>
    /// <exception cref="ArgumentException">The options' app version or rule id is not an allowed value, or <paramref name="decision"/> is not a defined <see cref="AgeDecision"/>.</exception>
    public static DecisionLogEntry From(AgeSignalsOutcome result, AgeDecision decision, AgeSignalsOptions options, DateTimeOffset nowUtc) =>
        new(WholeSeconds(nowUtc.ToUniversalTime()), BucketText(result, options.AgeBucketLogging), decision, options.AppVersion, options.RuleId);

    // "unknown" = no range was received; "withheld" = a range was received and not recorded: by the app's setting, or because it cannot be written
    // in the allowed forms (for example equal bounds or a bound of 100 or more), so the decision is still recorded.
    // Fails closed: an undefined setting never writes the real bucket.
    private static string BucketText(AgeSignalsOutcome result, AgeBucketLogging logging)
    {
        if (!result.Bucket.IsKnown) return "unknown";
        if (logging != AgeBucketLogging.Exact) return "withheld";
        var label = result.Bucket.Label;
        try { ValidateAgeBucket(label); return label; }
        catch (ArgumentException) { return "withheld"; }
    }

    /// <summary>The log's own writer records whole seconds only (less linkable); readers still accept a fraction.</summary>
    private static DateTimeOffset WholeSeconds(DateTimeOffset t) => new(t.Ticks - t.Ticks % TimeSpan.TicksPerSecond, t.Offset);

    // The reader's whole timestamp grammar: seconds, an optional fraction of 1-7 digits ("12:00:00." is not valid), then "Z" or "+00:00".
    private static readonly Regex TimestampPattern = new(@"^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(\.[0-9]{1,7})?(Z|\+00:00)\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static readonly string[] TimestampFormats = ["yyyy-MM-dd'T'HH:mm:ssK", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"];

    /// <summary>
    /// Accepts only ISO 8601 date-times with an explicit UTC marker: a trailing "Z" or "+00:00". Text without an offset or with any other offset is refused.
    /// </summary>
    internal static bool TryParseTimestamp(string? text, out DateTimeOffset value)
    {
        value = default;
        if (text is null || !TimestampPattern.IsMatch(text)) return false;
        return DateTimeOffset.TryParseExact(text, TimestampFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out value) && value.Offset == TimeSpan.Zero;
    }

    /// <summary>
    /// Builds an entry from loose key/value data (for example from an app's own code). Any key outside
    /// <see cref="FieldNames"/> is refused, so "birthdate", "installId", "accountId", "sourceTier", "deviceId" and the like cannot be logged.
    /// </summary>
    /// <param name="fields">Exactly the five keys of <see cref="FieldNames"/>, each with its string value.</param>
    /// <exception cref="ArgumentNullException"><paramref name="fields"/> is null.</exception>
    /// <exception cref="ArgumentException">A key is not allowed or missing, the timestamp is not UTC ISO 8601, the decision is not one of the four names, or a value is not in its allowed shape.</exception>
    public static DecisionLogEntry FromFields(IReadOnlyDictionary<string, string> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        foreach (var key in fields.Keys)
            if (!FieldNames.Contains(key, StringComparer.Ordinal))
                throw new ArgumentException($"Field \"{key}\" is not allowed in the decision log.", nameof(fields));
        foreach (var key in FieldNames)
            if (!fields.ContainsKey(key)) throw new ArgumentException($"Field \"{key}\" is missing.", nameof(fields));
        if (!TryParseTimestamp(fields["timestampUtc"], out var ts))
            throw new ArgumentException("Timestamp must be ISO 8601 UTC ending in \"Z\" or \"+00:00\".", nameof(fields));
        if (!DecisionNames.TryParse(fields["decision"], out var decision))
            throw new ArgumentException("Unknown decision.", nameof(fields));
        return new(ts, fields["ageBucket"], decision, fields["appVersion"], fields["ruleId"]);
    }
}

/// <summary>
/// The exact JSON form of one entry: an object with the five string properties. Timestamps are written as whole seconds, <c>2026-10-24T12:00:00Z</c>
/// (UTC, ISO 8601), and read only with an explicit "Z" or "+00:00" (no offset, or any other offset, is rejected). Decisions are PascalCase names only.
/// All five properties are required; unknown properties are skipped and not kept; a repeated property name or any invalid value rejects the whole entry.
/// </summary>
internal sealed class DecisionLogEntryConverter : JsonConverter<DecisionLogEntry>
{
    public override DecisionLogEntry Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("Entry must be an object.");
        DateTimeOffset? ts = null;
        string? bucket = null, version = null, rule = null;
        AgeDecision? decision = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var name = reader.GetString()!;
            if (!seen.Add(name)) throw new JsonException("Duplicate key.");
            reader.Read();
            switch (name)
            {
                case "timestampUtc":
                    if (reader.TokenType != JsonTokenType.String || !DecisionLogEntry.TryParseTimestamp(reader.GetString(), out var t)) throw new JsonException("Timestamp must be ISO 8601 UTC ending in Z or +00:00.");
                    ts = t;
                    break;
                case "ageBucket": bucket = ReadString(ref reader); break;
                case "decision":
                    if (!DecisionNames.TryParse(ReadString(ref reader), out var d)) throw new JsonException("Unknown decision.");
                    decision = d;
                    break;
                case "appVersion": version = ReadString(ref reader); break;
                case "ruleId": rule = ReadString(ref reader); break;
                default: reader.Skip(); break;
            }
        }
        if (ts is null || bucket is null || decision is null || version is null || rule is null) throw new JsonException("A required field is missing.");
        return new DecisionLogEntry(ts.Value, bucket, decision.Value, version, rule);   // ArgumentException when a value is invalid
    }

    private static string ReadString(ref Utf8JsonReader reader) =>
        reader.TokenType == JsonTokenType.String ? reader.GetString()! : throw new JsonException("Value must be a string.");

    public override void Write(Utf8JsonWriter writer, DecisionLogEntry value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("timestampUtc", value.TimestampUtc.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
        writer.WriteString("ageBucket", value.AgeBucket);
        writer.WriteString("decision", value.Decision.ToString());
        writer.WriteString("appVersion", value.AppVersion);
        writer.WriteString("ruleId", value.RuleId);
        writer.WriteEndObject();
    }
}

/// <summary>Decisions are PascalCase names only: integers, other casing, flag lists and padded text are rejected.</summary>
internal static class DecisionNames
{
    private static readonly string[] Names = Enum.GetNames<AgeDecision>();

    public static bool TryParse(string? text, out AgeDecision decision)
    {
        decision = default;
        if (text is null || Array.IndexOf(Names, text) < 0) return false;
        decision = Enum.Parse<AgeDecision>(text);
        return true;
    }
}

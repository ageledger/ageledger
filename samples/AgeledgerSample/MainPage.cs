using System.Diagnostics;
using Switch = Microsoft.Maui.Controls.Switch;
using Ageledger;
using Microsoft.Extensions.DependencyInjection;

namespace AgeledgerSample;

/// <summary>
/// One page that drives the library for the device test plan. It shows only what the library returns (state, bucket label, error
/// category, decision, Logged) and the five-field log. No identifiers, no exception messages, no network.
/// </summary>
public sealed partial class MainPage : ContentPage
{
    private static readonly string AppData = FileSystem.AppDataDirectory;
    private static readonly string LogFile = SampleServices.LogFile;
    private const string LaunchKey = "evaluate-at-launch";
    private const string LaunchDelayKey = "evaluate-at-launch-delay-ms";
    internal const string EnabledKey = "enabled";
    private const string TimeoutKey = "request-timeout-ms";
    internal const string UseRealManagerKey = "use-real-manager";

    private static readonly (string Name, int[] Gates)[] ValidPresets =
    [
        ("[2]", [2]), ("[18]", [18]), ("[13,16]", [13, 16]), ("[13,16,18]", [13, 16, 18]),
        ("[12,14,17]", [12, 14, 17]), ("[2,4,6]", [2, 4, 6]), ("[97,99]", [97, 99]),
    ];
#if DEBUG
    private static readonly (string Name, int[] Gates)[] InvalidPresets =
    [
        ("invalid [13,13]", [13, 13]), ("invalid [1]", [1]), ("invalid [13,100]", [13, 100]), ("invalid [13,16,18,21]", [13, 16, 18, 21]),
        ("invalid [18,16]", [18, 16]), ("invalid []", []), ("invalid [100]", [100]), ("invalid [13,14]", [13, 14]),
    ];
#endif
    private static readonly (string Name, int[] Gates)[] Presets =
#if DEBUG
        [.. ValidPresets, .. InvalidPresets];
#else
        ValidPresets;
#endif

    private readonly Switch enabled = new() { IsToggled = SafePref(EnabledKey), AutomationId = "enabled" };
    private readonly Picker bucketLogging = new() { ItemsSource = new[] { "Exact", "Withheld" }, SelectedIndex = 0, AutomationId = "age-bucket-logging" };
    private readonly Picker gates = new() { ItemsSource = Presets.Select(p => p.Name).ToList(), SelectedIndex = 3, AutomationId = "age-gates" };
    private readonly Entry minimumAge = new() { Text = "13", Keyboard = Keyboard.Numeric, AutomationId = "minimum-age" };
    private readonly Entry ruleId = new() { Text = "TEST" };
    private readonly Switch customPolicy = new();
    private readonly Entry cancelMs = new() { Text = "0", Keyboard = Keyboard.Numeric };
    private readonly Switch launch = new() { AutomationId = "evaluate-at-launch" };
    private readonly Entry launchDelay = new() { Text = "0", Keyboard = Keyboard.Numeric };
    private readonly Picker requestTimeout = new() { AutomationId = "request-timeout-ms" };
    private readonly Label status = new() { AutomationId = "status" };
    private readonly Label result = new() { AutomationId = "result" };
    private readonly Editor export = new() { IsReadOnly = true, HeightRequest = 160, AutoSize = EditorAutoSizeOption.Disabled };
    private readonly VerticalStackLayout root = new() { Spacing = 8, Padding = new Thickness(16, 48, 16, 24) };

    private AgeSignalsService? service;
    private CancellationTokenSource? inFlight; // the Evaluate that the Cancel button stops
    private string startupNote = ""; // shown once after the first Rebuild
    private IDecisionLog log = new JsonFileDecisionLog(LogFile);
#if DEBUG
    private readonly Switch unwritableLog = new();
#endif

    private partial void AddPlatformServices(IServiceCollection services);
    private partial void AddPlatformControls();

    public MainPage()
    {
        Content = new ScrollView { Content = root };

        Add("Enabled", enabled);
        Add("AgeBucketLogging", bucketLogging);
        Add("Age gates", gates);
        Add("MinimumAge", minimumAge);
        Add("Rule id", ruleId);
        Add("Request timeout (ms)", requestTimeout);
        Add("Custom SharedRangePolicy (always Blocked)", customPolicy);
        enabled.Toggled += (_, e) => { SetPref(EnabledKey, e.Value); Rebuild(); };
        bucketLogging.SelectedIndexChanged += (_, _) => Rebuild();
        gates.SelectedIndexChanged += (_, _) => Rebuild();
        minimumAge.Completed += (_, _) => Rebuild();
        ruleId.Completed += (_, _) => Rebuild();
        customPolicy.Toggled += (_, _) => Rebuild();
        SetUpRequestTimeout();

        // Evaluate and Cancel side by side, so both are on screen without scrolling on a small phone.
        var evaluateButton = new Button { Text = "Evaluate", AutomationId = "evaluate" };
        evaluateButton.Clicked += async (_, _) => await RunEvaluate(CancellationToken.None, null);
        var cancelButton = new Button { Text = "Cancel", AutomationId = "cancel" };
        cancelButton.Clicked += (_, _) => CancelEvaluate();
        root.Add(new HorizontalStackLayout { Spacing = 8, Children = { evaluateButton, cancelButton } });
        AddButton("Evaluate on a background thread", () => RunEvaluate(CancellationToken.None, null, background: true));
        Add("Cancel after (ms)", cancelMs);
        AddButton("Evaluate, cancel after N ms", () =>
        {
            _ = int.TryParse(cancelMs.Text, out var n);
            var cts = new CancellationTokenSource();
            if (n <= 0) cts.Cancel(); else cts.CancelAfter(TimeSpan.FromMilliseconds(n));
            return RunEvaluate(cts.Token, cts);
        });
        Add("Evaluate at launch", launch);
        Add("Launch delay (ms)", launchDelay);
        launch.IsToggled = SafePref(LaunchKey);
        launchDelay.Text = SafePrefString(LaunchDelayKey);
        launch.Toggled += (_, e) => { SetPref(LaunchKey, e.Value); SetPref(LaunchDelayKey, launchDelay.Text ?? "0"); };
        launchDelay.Completed += (_, _) => SaveLaunchDelay();
        launchDelay.Unfocused += (_, _) => SaveLaunchDelay();
        root.Add(status);
        root.Add(result);

        AddPlatformControls();

        root.Add(new Label { Text = "Log", FontAttributes = FontAttributes.Bold });
        AddButton("Export log", ExportLog, "export-log");
        AddButton("Copy export", async () => { try { await Clipboard.Default.SetTextAsync(export.Text ?? ""); } catch { } });
        AddButton("Clear log", async () =>
        {
            try { await log.ClearAsync(); export.Text = ""; status.Text = "Cleared."; }
            catch (Exception ex) { status.Text = "Clear failed: " + ex.GetType().Name; }
        }, "clear-log");
        root.Add(export);
#if DEBUG
        root.Add(new Label { Text = "Debug", FontAttributes = FontAttributes.Bold });
        Add("Unwritable log path", unwritableLog);
        unwritableLog.Toggled += (_, _) => { SetLogPath(); Rebuild(); };
        AddButton("Create leftover .tmp files", () =>
        {
            for (var i = 0; i < 2; i++) File.WriteAllText(Path.Combine(AppData, $"age-decisions.jsonl.{Guid.NewGuid():N}.tmp"), "x");
            status.Text = "Created 2 .tmp files.";
            return Task.CompletedTask;
        });
        AddButton("Append 1001 made-up entries", async () =>
        {
            try
            {
                var target = new JsonFileDecisionLog(LogFile);
                var now = DateTimeOffset.UtcNow;
                for (var i = 0; i < 1001; i++) await target.AppendAsync(new DecisionLogEntry(now, "13-15", AgeDecision.AllowedWithParentalConsent, "1.0", "TEST"));
                status.Text = "Appended 1001.";
            }
            catch (Exception ex) { status.Text = "Append failed: " + ex.GetType().Name; }
        });
        AddButton("List files in data directory", () =>
        {
            status.Text = "Files: " + string.Join(", ", Directory.EnumerateFiles(AppData).Select(Path.GetFileName));
            return Task.CompletedTask;
        });
#endif

        Rebuild();

#if DEBUG
        // The self-test builds its own services and never reads these controls; evaluate-at-launch is skipped while it runs.
        var selfTest = SelfTestLaunch.Current;
        if (selfTest is not null) SelfTestHost.Start(selfTest, text => MainThread.BeginInvokeOnMainThread(() => result.Text = text));
#endif
        if (launch.IsToggled
#if DEBUG
            && selfTest is null
#endif
            )
        {
            SaveLaunchDelay(); // Unfocused does not fire when the field is left with Back
            _ = int.TryParse(SafePrefString(LaunchDelayKey), out var delay);
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                if (delay > 0) await Task.Delay(delay);
                await RunEvaluate(CancellationToken.None, null);
            });
        }
    }

    /// <summary>Fills the timeout picker ("None" or a number of milliseconds) and selects the stored choice; a stored value that is not on the list is added.</summary>
    private void SetUpRequestTimeout()
    {
        var choices = new List<string> { "None", "5000", "15000", "60000" };
        _ = int.TryParse(SafePrefString(TimeoutKey), out var ms);
        if (ms > 0 && !choices.Contains(ms.ToString())) choices.Add(ms.ToString());
        requestTimeout.ItemsSource = choices;
        requestTimeout.SelectedIndex = ms > 0 ? choices.IndexOf(ms.ToString()) : 0;
        requestTimeout.SelectedIndexChanged += (_, _) =>
        {
            SetPref(TimeoutKey, RequestTimeoutMs().ToString());
            Rebuild();
        };
    }

    /// <summary>The picked timeout in milliseconds; 0 means no timeout.</summary>
    private int RequestTimeoutMs() => requestTimeout.SelectedItem is string s && int.TryParse(s, out var ms) && ms > 0 ? ms : 0;

    /// <summary>Cancels the most recent Evaluate that is still running and says in the status line whether there was one.</summary>
    private void CancelEvaluate()
    {
        var running = inFlight;
        status.Text = running is null ? "Cancel: nothing in flight" : "Cancel requested";
        try { running?.Cancel(); } catch (ObjectDisposedException) { }
    }

    private void SaveLaunchDelay() => SetPref(LaunchDelayKey, launchDelay.Text ?? "0");

    protected override void OnDisappearing()
    {
        SaveLaunchDelay();
        base.OnDisappearing();
    }

    internal static void SetPref(string key, bool value) { try { Preferences.Set(key, value); } catch { } }
    private static void SetPref(string key, string value) { try { Preferences.Set(key, value); } catch { } }
    internal static bool SafePref(string key) { try { return Preferences.Get(key, false); } catch { return false; } }
    private static string SafePrefString(string key) { try { return Preferences.Get(key, "0"); } catch { return "0"; } }

    private void Add(string label, View view)
    {
        root.Add(new HorizontalStackLayout { Spacing = 8, Children = { new Label { Text = label, VerticalOptions = LayoutOptions.Center }, view } });
    }

    private void AddButton(string text, Func<Task> action, string? automationId = null)
    {
        var button = new Button { Text = text, AutomationId = automationId };
        button.Clicked += async (_, _) => await action();
        root.Add(button);
    }

    private void AddButton(string text, Action action, string? automationId = null) => AddButton(text, () => { action(); return Task.CompletedTask; }, automationId);

#if DEBUG
    private void SetLogPath()
    {
        string path = LogFile;
        if (unwritableLog.IsToggled)
        {
            // An existing file used as a directory component, so the log can never be created.
            var blocker = Path.Combine(AppData, "not-a-directory");
            File.WriteAllText(blocker, "x");
            path = Path.Combine(blocker, "sub", "age-decisions.jsonl");
        }
        log = new JsonFileDecisionLog(path);
    }
#endif

    /// <summary>Builds a new service from the controls. An invalid setting (checked only when enabled) returns false; Evaluate then does not run.</summary>
    private bool Rebuild()
    {
        var isEnabled = enabled.IsToggled;
        var gateSet = Presets[Math.Max(0, gates.SelectedIndex)].Gates;
        var minimum = int.TryParse(minimumAge.Text, out var min) ? min : 13;
        var bucket = bucketLogging.SelectedIndex == 1 ? AgeBucketLogging.Withheld : AgeBucketLogging.Exact;
        var rule = ruleId.Text ?? "";
        var policy = customPolicy.IsToggled;
        var timeout = RequestTimeoutMs() is var t and > 0 ? TimeSpan.FromMilliseconds(t) : (TimeSpan?)null;
        try
        {
            // A new container each time, so the settings can change; the page's own log (which a debug option can point elsewhere) and, in Debug, the Fake backend are registered first and kept.
            // This per-change container is a convenience for this test page only: an app registers once in MauiProgram and resolves that one service.
            var services = new ServiceCollection();
            services.AddSingleton(log);
            AddPlatformServices(services);
            SampleServices.Register(services, o =>
            {
                o.Enabled = isEnabled;
                o.AgeGates = gateSet;
                o.MinimumAge = minimum;
                o.AgeBucketLogging = bucket;
                o.AppVersion = AppInfo.VersionString;
                o.RuleId = rule;
                o.SharedRangePolicy = policy ? _ => AgeDecision.Blocked : null;
                o.RequestTimeout = timeout;
            });
            service = services.BuildServiceProvider().GetRequiredService<AgeSignalsService>();
            status.Text = $"Service built (enabled={isEnabled}).{startupNote}";
            startupNote = "";
            return true;
        }
        catch (Exception ex)
        {
            status.Text = $"Service not rebuilt: {ex.GetType().Name}";
            return false;
        }
    }

    private async Task RunEvaluate(CancellationToken token, CancellationTokenSource? cts, bool background = false)
    {
        if (!Rebuild()) // pick up edited MinimumAge / rule id without needing Return
        {
            service = null; // a refused rebuild must not evaluate the previous service
            result.Text = "Not evaluated: invalid settings (see status).";
            cts?.Dispose();
            return;
        }
        if (service is null) { cts?.Dispose(); return; }
        var svc = service;
        var sw = Stopwatch.StartNew();
        // The Cancel button cancels this source; the caller's own token (the "cancel after N ms" button) is linked in.
        using var own = CancellationTokenSource.CreateLinkedTokenSource(token);
        inFlight = own;
        var linked = own.Token;
        try
        {
            var evaluation = background ? await Task.Run(() => svc.EvaluateAsync(linked)) : await svc.EvaluateAsync(linked);
            var r = evaluation.Result;
            result.Text = $"Platform: {r.Platform}\nState: {r.State}\nBucket: {r.Bucket.Label}\nError: {r.Error}\n" +
                          $"SignificantChange: {r.SignificantChange}\nParentalControls: {r.ParentalControls}\n" +
                          $"Decision: {evaluation.Decision}\nLogged: {evaluation.Logged}\nElapsed ms: {sw.ElapsedMilliseconds}";
        }
        catch (OperationCanceledException ex)
        {
            result.Text = $"Outcome: Cancelled\nException: {ex.GetType().Name}\nElapsed ms: {sw.ElapsedMilliseconds}";
        }
        catch (Exception ex)
        {
            result.Text = $"{ex.GetType().Name}\nElapsed ms: {sw.ElapsedMilliseconds}";
        }
        finally
        {
            if (ReferenceEquals(inFlight, own)) inFlight = null;
            cts?.Dispose();
        }
    }

    private async Task ExportLog()
    {
        try { export.Text = await log.ExportJsonAsync(); }
        catch (Exception ex) { export.Text = "Export failed: " + ex.GetType().Name; }
    }
}

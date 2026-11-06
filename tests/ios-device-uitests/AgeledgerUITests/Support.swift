import XCTest

/// Attaches a JSON value. The script keeps only attachments whose names are on its allowlist, so a new name must be added there.
func addJSON<T: Encodable>(to test: XCTestCase, _ value: T, name: String) {
    let encoder = JSONEncoder()
    encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
    guard let data = try? encoder.encode(value) else { return }
    let attachment = XCTAttachment(data: data, uniformTypeIdentifier: "public.json")
    attachment.name = name
    attachment.lifetime = XCTAttachment.Lifetime.keepAlways
    test.add(attachment)
}

/// Registers the always-run cleanup: attach the Settings trail and stop Settings and the sample, so evidence and cleanup do not
/// depend on a thrown error. Call it FIRST in a test: teardown blocks run last-registered first, so a restore registered later
/// still runs before this one.
func addAlwaysCleanup(to test: XCTestCase) {
    test.addTeardownBlock { SandboxSettings.attachTrailAndStop() }
}

struct OriginalScenario: Codable { var scenario: String? }

/// Prints the original scenario on its own line the moment it is read (before any change), so the script can write its restore
/// file at once. Only labels from the fixed list are ever printed.
func announceOriginalScenario(_ scenario: String?) {
    guard let scenario, SandboxSettings.scenarios.contains(scenario) else { return }
    print("ORIGINAL-SCENARIO: \(scenario)")
    fflush(stdout)
    NSLog("ORIGINAL-SCENARIO: %@", scenario)
}

/// Printed from a teardown when the test threw before it changed anything (the scenario was never touched): the script then drops its
/// "unknown" restore marker by itself. A run that may have changed something never prints it.
func announceNothingChanged(_ changed: Bool) {
    guard !changed else { return }
    print("NOTHING-CHANGED")
    fflush(stdout)
    NSLog("NOTHING-CHANGED")
}

struct ExplorationRow: Codable {
    var row: String
    var scenario: String
    var sheetSeen: Bool
    var sheetFindings: [SheetFinding]
    var result: [String: String]
    /// "recorded", or "unknown" when an earlier answer may still be remembered (never a pass).
    var verdict: String
    var note: String?
    /// Which process and container type matched, so the real test can target it directly.
    var matchedProcess: String?
    var matchedContainer: String?
    /// What the plan expects for this scenario with gates 13/16/18 (bucket and decision), and the outcome of the comparison:
    /// "match", "mismatch" or "not-checked". A mismatch is a finding, not a failure.
    var expected: String?
    var check: String?
}

/// Expected bucket and decision per scenario (device test plan 6.4 and 7.3, gates 13/16/18, default policy). "Off" has none.
func expectedOutcome(for scenario: String) -> (bucket: String, decision: String)? {
    switch scenario {
    case "User under 13, significant change approved": return ("0-12", "Blocked")
    case "User 13-15, significant change approved": return ("13-15", "AllowedWithParentalConsent")
    case "User 16-17, significant change declined": return ("16-17", "AllowedWithParentalConsent")
    case "User 18+, age not confirmed, significant change not applicable",
         "User 18+, age confirmed, significant change not applicable",
         "User 18+, age confirmed, significant change applicable": return ("18+", "Allowed")
    default: return nil
    }
}

struct ExplorationReport: Codable {
    var originalScenario: String?
    var originalUnreadable = false
    /// True when the original was not read from the page but assumed to be "Off" because the script was started with --assume-original-off.
    var originalAssumed = false
    /// How the restore was confirmed: "read-back" (the only value that counts), "unconfirmed" or "failed". nil if no restore was needed.
    var restored: String?
    /// Row I18 only: whether the sample's request timeout was put back to None ("read-back" or "failed").
    var timeoutReset: String?
    var declineRemembered: Bool?
    /// Set when the rows were stopped early, for example "system age sheet".
    var stoppedBy: String?
    var rows: [ExplorationRow] = []
}

/// Which of the sample, Settings and SpringBoard report being in the foreground, and their state codes. Bundle ids of system apps and
/// the sample only; nothing about the person.
func foregroundSummary(_ sample: SampleApp, includeSettings: Bool = true) -> String {
    let settings = XCUIApplication(bundleIdentifier: SandboxSettings.bundleId)
    let springboard = XCUIApplication(bundleIdentifier: SheetProbe.springboard)
    let apps = [("sample", sample.app), ("settings", settings), ("springboard", springboard)].filter { includeSettings || $0.0 != "settings" }
    return apps.map { "\($0.0)=\($0.1.state.rawValue)" }.joined(separator: " ") + " (4 = foreground)"
}

/// True only when a screenshot of the sample cannot show anything else: the sample is the foreground app, Settings is not (and, if it
/// is running, has no alert or sheet), and SpringBoard shows no alert or sheet.
private func screenshotConditionsHold(_ sample: SampleApp) -> Bool {
    let settings = XCUIApplication(bundleIdentifier: SandboxSettings.bundleId)
    let springboard = XCUIApplication(bundleIdentifier: SheetProbe.springboard)
    guard sample.app.state == .runningForeground, settings.state != .runningForeground,
          springboard.alerts.count == 0, springboard.sheets.count == 0 else { return false }
    if settings.state == .runningBackground, settings.alerts.count > 0 || settings.sheets.count > 0 { return false }
    return true
}

/// Attaches a screenshot of the SAMPLE only. The conditions above are checked right before the capture and again right after it; the
/// picture is dropped if anything changed in between. Otherwise returns a note with the states instead. (A kept PNG is not
/// text-scanned by the script.)
func attachSampleScreenshot(_ test: XCTestCase, _ sample: SampleApp, name: String) -> String? {
    guard screenshotConditionsHold(sample) else { return "no screenshot: \(foregroundSummary(sample))" }
    let shot = XCTAttachment(screenshot: sample.screenshot())
    guard screenshotConditionsHold(sample) else { return "screenshot dropped: the screen changed while it was taken (\(foregroundSummary(sample)))" }
    shot.name = name
    shot.lifetime = XCTAttachment.Lifetime.keepAlways
    test.add(shot)
    return nil
}

/// What an Evaluate row is compared with. A mismatch is a finding, never a failure.
enum Expectation {
    /// The table in expectedOutcome(for:) for the scenario (gates 13/16/18, default policy).
    case table
    /// Nothing is compared: Apple decides the value and it is only recorded.
    case record
    /// An expected bucket and decision named by the row.
    case fixed(bucket: String, decision: String)
}

/// Evaluates once in a fresh sample process and records whether a sheet appeared. `answer` is tapped if one shows.
/// `setup` runs after the relaunch and before Evaluate (nothing is in flight and no sheet can be up then, so it may scroll and pick);
/// it returns notes for the row, and a note starting with "unknown:" also makes the row "unknown". With `relaunch` false the sample's
/// current process is used as it is (the caller has scrolled Evaluate into view). `touchSettings` false keeps the row from querying
/// the Settings app at all. `terminate` false leaves the sample running for a following step.
func evaluateRow(_ test: XCTestCase, _ sample: SampleApp, row: String, scenario: String, answer: String?,
                 verdict: String = "recorded", screenshotName: String? = nil, expectation: Expectation = .table,
                 relaunch: Bool = true, setup: ((SampleApp) throws -> [String])? = nil, touchSettings: Bool = true,
                 terminate: Bool = true) throws -> ExplorationRow {
    var notes: [String] = []
    var verdict = verdict
    var stale: String?
    if relaunch {
        sample.relaunch()
        // A newer sample keeps its request timeout across relaunches; the age-scenario rows must run without one.
        if let cleared = sample.ensureNoTimeout(), !cleared { notes.append("request timeout could not be set to None"); verdict = "unknown" }
        if let setup {
            for note in try setup(sample) {
                if note.hasPrefix("unknown:") { verdict = "unknown"; notes.append(String(note.dropFirst("unknown:".count)).trimmingCharacters(in: .whitespaces)) } else { notes.append(note) }
            }
        }
    } else {
        stale = sample.resultText // an older result must not be taken for this one
    }
    try sample.tapEvaluate()
    let probe = SheetProbe.look(timeout: 10)
    if probe.seen {
        if let screenshotName, let skipped = attachSampleScreenshot(test, sample, name: screenshotName) { notes.append(skipped) }
        if let answer {
            if !SheetProbe.tapButton(label: answer, probe: probe) {
                notes.append(probe.ageRangeText ? "button \"\(answer)\" not found" : "button \"\(answer)\" not tapped: no age-range text in the same container")
                verdict = "unknown"
            }
        } else {
            notes.append("sheet left unanswered"); verdict = "unknown"
        }
    }
    var text = sample.waitForResult(timeout: 20, differentFrom: stale)
    var emptyResult = false
    var late = ProbeResult() // a sheet that shows up after the first probe (or while the result is missing) still counts
    if text.isEmpty {
        // Whatever the app shows is recorded: wait longer and note what is on screen and which app is in the foreground (no screenshot).
        text = sample.waitForResult(timeout: 40, differentFrom: stale)
        if text.isEmpty {
            emptyResult = true
            late = SheetProbe.look(timeout: 3)
            notes.append("foreground when the result was missing: \(foregroundSummary(sample, includeSettings: touchSettings))")
            if touchSettings { SandboxSettings.noteSystemSheets("system sheet while the result was missing") }
            notes.append(late.findings.isEmpty ? "no result label after 60 s; nothing found on screen"
                                               : "no result label after 60 s; something is on screen: \(late.container ?? "?") in \(late.process ?? "?")")
            verdict = "unknown"
        }
    }
    if !emptyResult, !probe.seen { late = SheetProbe.look(timeout: 1) }
    if late.seen { notes.append("a sheet appeared after the first probe and was left unanswered"); verdict = "unknown" }
    let status = sample.statusText
    if terminate { sample.app.terminate() } // so a lingering sheet cannot block the next Settings step; nothing is tapped to dismiss it
    var result = SampleApp.parse(text)
    if emptyResult && !status.isEmpty { result["Status"] = status } // the app's own status line, e.g. "Service built (enabled=True)."
    var expected: String?
    var check = "not-checked"
    let want: (bucket: String, decision: String)?
    switch expectation {
    case .table: want = expectedOutcome(for: scenario)
    case .record: want = nil
    case .fixed(let bucket, let decision): want = (bucket, decision)
    }
    if let want {
        expected = "\(want.bucket) / \(want.decision)"
        // An empty or unparsed result is not a mismatch: there was nothing to compare.
        if !emptyResult, result["Bucket"] != nil || result["Decision"] != nil {
            check = result["Bucket"] == want.bucket && result["Decision"] == want.decision ? "match" : "mismatch"
        }
    }
    result["Outcome"] = ResultParser.outcome(of: text) // one spelling for every row: returned, cancelled, timed-out, ...
    let seen = probe.seen ? probe : late
    return ExplorationRow(row: row, scenario: scenario, sheetSeen: probe.seen || late.seen, sheetFindings: seen.findings,
                          result: result, verdict: verdict, note: notes.isEmpty ? nil : notes.joined(separator: "; "),
                          matchedProcess: seen.seen ? seen.process : nil, matchedContainer: seen.seen ? seen.container : nil,
                          expected: expected, check: check)
}

extension ExplorationRow {
    /// Whether a result label was read (an empty one is stored only as the sample's status line).
    var resultShown: Bool { result["Status"] == nil && result["Outcome"] != "no-result" && !result.isEmpty }
}

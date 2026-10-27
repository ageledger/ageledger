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
func foregroundSummary(_ sample: SampleApp) -> String {
    let settings = XCUIApplication(bundleIdentifier: SandboxSettings.bundleId)
    let springboard = XCUIApplication(bundleIdentifier: SheetProbe.springboard)
    let apps = [("sample", sample.app), ("settings", settings), ("springboard", springboard)]
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

/// Evaluates once in a fresh sample process and records whether a sheet appeared. `answer` is tapped if one shows.
func evaluateRow(_ test: XCTestCase, _ sample: SampleApp, row: String, scenario: String, answer: String?,
                 verdict: String = "recorded", screenshotName: String? = nil) throws -> ExplorationRow {
    sample.relaunch()
    var notes: [String] = []
    var verdict = verdict
    // A newer sample keeps its request timeout across relaunches; the age-scenario rows must run without one.
    if let cleared = sample.ensureNoTimeout(), !cleared { notes.append("request timeout could not be set to None"); verdict = "unknown" }
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
    var text = sample.waitForResult(timeout: 20)
    var emptyResult = false
    if text.isEmpty {
        // Whatever the app shows is recorded: wait longer and note what is on screen and which app is in the foreground (no screenshot).
        text = sample.waitForResult(timeout: 40)
        if text.isEmpty {
            emptyResult = true
            let late = SheetProbe.look(timeout: 3)
            notes.append("foreground when the result was missing: \(foregroundSummary(sample))")
            SandboxSettings.noteSystemSheets("system sheet while the result was missing")
            notes.append(late.findings.isEmpty ? "no result label after 60 s; nothing found on screen"
                                               : "no result label after 60 s; something is on screen: \(late.container ?? "?") in \(late.process ?? "?")")
            verdict = "unknown"
        }
    }
    let status = sample.statusText
    sample.app.terminate() // so a lingering sheet cannot block the next Settings step; nothing is tapped to dismiss it
    var result = SampleApp.parse(text)
    if emptyResult && !status.isEmpty { result["Status"] = status } // the app's own status line, e.g. "Service built (enabled=True)."
    var expected: String?
    var check = "not-checked"
    if let want = expectedOutcome(for: scenario) {
        expected = "\(want.bucket) / \(want.decision)"
        // An empty or unparsed result is not a mismatch: there was nothing to compare.
        if !emptyResult, result["Bucket"] != nil || result["Decision"] != nil {
            check = result["Bucket"] == want.bucket && result["Decision"] == want.decision ? "match" : "mismatch"
        }
    }
    return ExplorationRow(row: row, scenario: scenario, sheetSeen: probe.seen, sheetFindings: probe.findings,
                          result: result, verdict: verdict, note: notes.isEmpty ? nil : notes.joined(separator: "; "),
                          matchedProcess: probe.seen ? probe.process : nil, matchedContainer: probe.seen ? probe.container : nil,
                          expected: expected, check: check)
}

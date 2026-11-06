import XCTest

/// The sandbox scenario a row needs. These rows never open Settings and never change the scenario: the person running them sets it
/// first, and names it in ROW_SCENARIO (scripts/ios-uitests.sh passes `--scenario` as TEST_RUNNER_ROW_SCENARIO). The test cannot read
/// the scenario back without Settings, so the name is recorded as declared, not as verified.
enum RowScenario {
    static let thirteenToFifteen = "User 13-15, significant change approved"
    static let sixteenToSeventeen = "User 16-17, significant change declined"
    /// Every scenario except Off (with Off, Evaluate has not returned within 60 s on the phone; that is row I18).
    static let notOff = SandboxSettings.scenarios.filter { $0 != "Off" }
    static let any = SandboxSettings.scenarios

    /// The scenario named in ROW_SCENARIO when it is one of `allowed`; otherwise an error, before the sample is touched.
    static func required(allowed: [String]) throws -> String {
        let named = ProcessInfo.processInfo.environment["ROW_SCENARIO"] ?? ""
        if let problem = ResultParser.rowScenarioProblem(named: named, known: SandboxSettings.scenarios, allowed: allowed) {
            throw SandboxSettingsError.verificationFailed(problem)
        }
        return named
    }
}

/// Device test plan rows I7b, I7c, I11, I12, I13, I14, 8.1 (e) (with the sheet part of 8.1 (b)) and 8.3 (iOS), opt-in: scripts/ios-uitests.sh --row <name>.
/// Not part of the default run, and not run on a device yet. Rows whose outcome Apple decides are recorded, never asserted.
///
/// What every row here keeps to: the sandbox scenario is chosen by the person (RowScenario) and never changed, Settings is never opened,
/// queried or screenshotted, nothing is scrolled or tapped once an Apple sheet could be on screen (controls are scrolled into view only
/// before the action that starts a request), a sheet that appears is left unanswered, and the sample is terminated at the end. The
/// sample's "Evaluate at launch" switch must read as off, as for I18.
final class PlannedRowTests: XCTestCase {
    private var report = ExplorationReport()
    private var scenario = ""

    override func setUp() { continueAfterFailure = false }

    // MARK: rows

    /// I7b: gates [13,16,18], MinimumAge 16, scenario 13-15: the range is 13-15 and the library's default policy blocks it.
    func testI7b() throws {
        let sample = try begin(allowed: [RowScenario.thirteenToFifteen])
        var made = try recordRow {
            try evaluateRow(self, sample, row: "i7b", scenario: scenario, answer: nil, expectation: .fixed(bucket: "13-15", decision: "Blocked"),
                            setup: { s in
                                var notes = try Self.gatesSetup("[13,16,18]")(s)
                                if try s.setMinimumAgePreset("16") { notes.append("MinimumAge 16 (Debug preset, read back)") }
                                else { notes.append("unknown: the MinimumAge preset did not read back 16") }
                                return notes
                            }, touchSettings: false)
        }
        let confirmed = made.verdict == "recorded" // gates and the MinimumAge preset both read back
        made.result["MinimumAge"] = confirmed ? "16" : "not confirmed"
        report.rows[report.rows.count - 1] = made
        // The library's rule, not Apple's: a 13-15 range with MinimumAge 16 must be Blocked. Checked only when the settings read back and
        // the range arrived as 13-15.
        if confirmed, made.result["Bucket"] == "13-15" { XCTAssertEqual(made.result["Decision"], "Blocked") }
    }

    /// I7c: gates [18], scenario 13-15 or 16-17. Apple decides the range it returns under that gate; the plan expects 0-17 and Blocked.
    func testI7c() throws {
        let sample = try begin(allowed: [RowScenario.thirteenToFifteen, RowScenario.sixteenToSeventeen])
        try recordRow {
            try evaluateRow(self, sample, row: "i7c", scenario: scenario, answer: nil, expectation: .fixed(bucket: "0-17", decision: "Blocked"),
                            setup: Self.gatesSetup("[18]"), touchSettings: false)
        }
    }

    /// I11: each valid gate preset, one fresh sample process each. The bucket and decision are recorded, not compared.
    func testI11() throws {
        let sample = try begin(allowed: RowScenario.notOff)
        for preset in ["[18]", "[13,16]", "[12,14,17]", "[2,4,6]", "[97,99]"] {
            let made = try recordRow {
                try evaluateRow(self, sample, row: "i11 \(preset)", scenario: scenario, answer: nil, expectation: .record,
                                setup: Self.gatesSetup(preset), touchSettings: false)
            }
            // After a request was started, go on only when a result was shown and no sheet was seen (the row's probes include a late one).
            if !ResultParser.mayContinue(sheetSeen: made.sheetSeen, lateSheetSeen: false, resultShown: made.resultShown) || made.verdict == "unknown" {
                report.stoppedBy = "the remaining presets were not run: a sheet appeared, no result was shown, or a setting did not read back"
                break
            }
        }
    }

    /// I12: IsEligibleForAgeFeaturesAsync, recorded as true, false or n/a for the scenario the person set.
    func testI12() throws {
        try recordCall(button: "is-eligible", prefix: "IsEligibleForAgeFeatures: ", row: "i12", waitSeconds: 20)
    }

    /// I13: GetRequiredRegulatoryFeaturesAsync, recorded as a value or n/a.
    func testI13() throws {
        try recordCall(button: "regulatory-features", prefix: "RegulatoryFeatures: ", row: "i13", waitSeconds: 20)
    }

    /// I14: ShowSignificantUpdateAcknowledgmentAsync, recorded as Acknowledged, Unsupported, Failed or whatever it returns. It may raise an
    /// Apple screen; nothing is tapped on it, and the sample is terminated after the wait.
    func testI14() throws {
        try recordCall(button: "significant-update", prefix: "Acknowledgment: ", row: "i14", waitSeconds: 40)
    }

    /// 8.1 (e), with the sheet part of 8.1 (b): "Evaluate, cancel after N ms" with N = 0 (a token that is already cancelled) must end as
    /// cancelled with no sheet; a normal Evaluate in the same process after it must then return a result. The second step runs only when
    /// the first ended as cancelled with no sheet seen (ResultParser.mayRunNormalEvaluate), and a sheet is probed for again right
    /// before the scroll and the tap.
    func testI81e() throws {
        let sample = try begin(allowed: RowScenario.notOff)
        var notes: [String] = []
        if let cleared = sample.ensureNoTimeout(), !cleared { notes.append("request timeout could not be set to None") }
        var verdict = "recorded"
        // Pin the gates the later Evaluate relies on, before any request; a picker that does not read back makes the row unknown
        // and the normal Evaluate is not run.
        let gatesPinned = try recording { try sample.setGatesChecked("[13,16,18]") }
        if !gatesPinned { notes.append("the gates picker did not read back [13,16,18]"); verdict = "unknown" }
        let before = sample.resultText
        try recording { try sample.tapAfterScrolling("evaluate-cancel-after") } // N is the sample's default, 0
        let probe = SheetProbe.look(timeout: 10) // as evaluateRow does
        let text = sample.waitForResult(timeout: 10, differentFrom: before)
        let late = probe.seen ? ProbeResult() : SheetProbe.look(timeout: 1)
        var result = SampleApp.parse(text)
        result["Outcome"] = ResultParser.outcome(of: text)
        if let elapsed = ResultParser.elapsedMs(text) { result["ElapsedMs"] = String(elapsed) }
        let proceed = ResultParser.mayRunNormalEvaluate(sheetSeen: probe.seen, lateSheetSeen: late.seen, resultText: text, gatesPinned: gatesPinned)
        if text.isEmpty { notes.append("no result after the pre-cancelled call") }
        if probe.seen || late.seen { notes.append("a sheet appeared after the pre-cancelled call and was left unanswered") }
        if !proceed { verdict = "unknown" }
        if !gatesPinned { report.stoppedBy = "gates did not read back; the normal Evaluate was not run" }
        else if !proceed { report.stoppedBy = "the normal Evaluate was not run: the pre-cancelled call did not end as cancelled, or a sheet appeared" }
        report.rows.append(row("i81b-pre-cancelled", probe: probe.seen ? probe : late, result: result, verdict: verdict, notes: notes))
        // 8.1 (b) is the library's rule: a token that is already cancelled ends as cancelled (checked when no sheet got in the way).
        if !(probe.seen || late.seen), !text.isEmpty { XCTAssertEqual(result["Outcome"], "cancelled", "the pre-cancelled call did not end as cancelled") }
        guard proceed else { return }

        // Nothing is in flight and no sheet was seen. Probe once more right before any gesture, then scroll back only as far as Evaluate.
        let again = SheetProbe.look(timeout: 2)
        guard !again.seen else {
            report.rows[report.rows.count - 1].sheetSeen = true
            report.rows[report.rows.count - 1].verdict = "unknown"
            report.stoppedBy = "a sheet appeared before the normal Evaluate; nothing more was done"
            return
        }
        sample.scrollToTop(until: "evaluate")
        let second = try recordRow {
            try evaluateRow(self, sample, row: "i81e-normal-after-cancel", scenario: scenario, answer: nil, expectation: .table,
                            relaunch: false, touchSettings: false)
        }
        XCTAssertNotNil(second.result["State"], "the normal Evaluate after a cancelled one returned no state")
    }

    /// 8.3 (iOS): Evaluate during launch, through the Debug sample's launch argument (this launch only; the stored switch is not changed),
    /// once with no delay and once with a 2000 ms delay. NoPresenter, a result or a sheet are all recorded as they come.
    func testI83() throws {
        let sample = try begin(allowed: RowScenario.notOff)
        let passes: [(name: String, arguments: [String], note: String)] = [
            ("i83-no-delay", ["-ageledger-launch-evaluate-once", "1"], "evaluate during launch, no delay"),
            ("i83-delay-2000", ["-ageledger-launch-evaluate-once", "1", "-ageledger-launch-delay-ms", "2000"], "evaluate during launch, 2000 ms delay"),
        ]
        for pass in passes {
            var notes = [pass.note]
            var verdict = "recorded"
            sample.relaunch(arguments: pass.arguments)
            let probe = SheetProbe.look(timeout: 10)
            let text = sample.waitForResult(timeout: 30)
            let late = probe.seen ? ProbeResult() : SheetProbe.look(timeout: 1)
            let status = sample.statusText
            var result = SampleApp.parse(text)
            result["Outcome"] = ResultParser.outcome(of: text)
            if let elapsed = ResultParser.elapsedMs(text) { result["ElapsedMs"] = String(elapsed) }
            // The Debug sample puts this marker in its status line when it acts on the launch argument.
            if let gates = sample.gatesValue, gates != "[13,16,18]" { notes.append("the gates picker showed \(gates), not [13,16,18]"); verdict = "unknown" }
            else if sample.gatesValue == nil { notes.append("the gates picker could not be read"); verdict = "unknown" }
            if !status.contains("Launch evaluate (argument)") { notes.append("the sample did not report acting on the launch argument (status marker missing)"); verdict = "unknown" }
            if text.isEmpty { notes.append("no result within the 30 s wait; foreground: \(foregroundSummary(sample, includeSettings: false))"); verdict = "unknown" }
            if probe.seen || late.seen { notes.append("a sheet appeared and was left unanswered"); verdict = "unknown" }
            let go = ResultParser.mayContinue(sheetSeen: probe.seen, lateSheetSeen: late.seen, resultShown: !text.isEmpty)
            sample.app.terminate() // nothing is tapped to dismiss anything
            report.rows.append(row(pass.name, probe: probe.seen ? probe : late, result: result, verdict: verdict, notes: notes))
            if !go { report.stoppedBy = "the remaining pass was not run: a sheet appeared or no result was shown"; break }
        }
    }

    // MARK: helpers

    /// Registers the report and the sample stop, checks the scenario and the sample, and turns Enabled on. Nothing is tapped or scrolled
    /// until the "Evaluate at launch" switch has been read as off. Settings is never touched, so the scenario is never changed:
    /// NOTHING-CHANGED is printed at the end of every run (the script then has nothing to restore).
    private func begin(allowed: [String]) throws -> SampleApp {
        addTeardownBlock { [self] in
            XCUIApplication(bundleIdentifier: SampleApp.bundleId).terminate()
            addJSON(to: self, report, name: "rows-report")
            announceNothingChanged(false)
        }
        do { scenario = try RowScenario.required(allowed: allowed) } catch {
            report.stoppedBy = "\(error)"
            throw error
        }
        let sample = SampleApp()
        sample.relaunch()
        guard ResultParser.launchGuard(launchOn: sample.evaluateAtLaunch) == .proceed else {
            sample.app.terminate()
            report.stoppedBy = "Evaluate at launch is on; turn it off in the sample and re-run"
            throw SandboxSettingsError.verificationFailed("Evaluate at launch is on (or could not be read); turn it off in the sample and re-run")
        }
        try recording { try sample.setEnabled(true) }
        return sample
    }

    /// Runs a step and, when it throws, puts the reason in the report before rethrowing (the report is attached in the teardown).
    @discardableResult
    private func recording<T>(_ step: () throws -> T) throws -> T {
        do { return try step() } catch {
            report.stoppedBy = "\(error)"
            throw error
        }
    }

    /// Runs a step that makes a row (an evaluateRow call), keeps the row in the report and returns it.
    @discardableResult
    private func recordRow(_ step: () throws -> ExplorationRow) throws -> ExplorationRow {
        let made = try recording(step)
        report.rows.append(made)
        return made
    }

    /// Setup for evaluateRow: picks a gates preset and notes it; the row is "unknown" when the picker does not read back.
    private static func gatesSetup(_ preset: String) -> (SampleApp) throws -> [String] {
        { sample in
            var notes = ["gates \(preset)"]
            if !(try sample.setGatesChecked(preset)) { notes.append("unknown: the gates picker did not read back \(preset)") }
            return notes
        }
    }

    private func row(_ name: String, probe: ProbeResult, result: [String: String], verdict: String, notes: [String]) -> ExplorationRow {
        ExplorationRow(row: name, scenario: scenario, sheetSeen: probe.seen, sheetFindings: probe.findings, result: result, verdict: verdict,
                       note: notes.isEmpty ? nil : notes.joined(separator: "; "),
                       matchedProcess: probe.seen ? probe.process : nil, matchedContainer: probe.seen ? probe.container : nil,
                       expected: nil, check: "not-checked")
    }

    /// One of the platform-call buttons (I12 to I14): scroll to it (nothing is in flight yet), tap once, watch for a sheet for 3 s
    /// without touching anything, then record the sample's status line. Only a short, plain value is kept.
    private func recordCall(button: String, prefix: String, row name: String, waitSeconds: TimeInterval) throws {
        let sample = try begin(allowed: RowScenario.any)
        let before = sample.statusText
        try recording { try sample.tapAfterScrolling(button) }
        let probe = SheetProbe.look(timeout: 3)
        let status = sample.waitForStatus(changedFrom: before, timeout: waitSeconds)
        let late = probe.seen ? ProbeResult() : SheetProbe.look(timeout: 1)
        var notes: [String] = []
        var verdict = "recorded"
        var observed = "no-status"
        if status != before, !status.isEmpty {
            let value = status.hasPrefix(prefix) ? String(status.dropFirst(prefix.count)) : "exception " + status
            // Only plain words are kept (a bool, a flags value, a returned enum name or an exception type name).
            observed = value.range(of: "^[A-Za-z0-9 ,._|-]{1,80}$", options: .regularExpression) != nil ? value : "other"
        } else {
            verdict = "unknown"
            notes.append("no status change after \(Int(waitSeconds)) s; foreground: \(foregroundSummary(sample, includeSettings: false))")
        }
        if probe.seen || late.seen { notes.append("a sheet appeared and was left unanswered"); verdict = "unknown" }
        sample.app.terminate() // nothing is tapped to dismiss anything
        report.rows.append(row(name, probe: probe.seen ? probe : late, result: ["Observed": observed], verdict: verdict, notes: notes))
    }
}

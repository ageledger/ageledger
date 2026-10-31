import XCTest

/// Device test plan row I18: with the sandbox scenario Off, does Evaluate return, time out or cancel instead of waiting indefinitely?
/// Not part of the default run: scripts/ios-uitests.sh --row i18. Pass 1 sets the sample's request timeout to 15 s; pass 2 sets none and
/// taps the sample's Cancel button after about 5 s. Both outcomes are recorded, never judged: the row is a recording, not a pass or fail.
final class I18Tests: XCTestCase {
    private var report = ExplorationReport()

    override func setUp() { continueAfterFailure = false }

    func testI18() throws {
        addAlwaysCleanup(to: self)
        var changed = false
        var sampleTouched = false

        // Teardown blocks run last-registered first. This one is registered BEFORE the scenario restore below, so it runs AFTER it: a
        // problem while putting the sample's settings back can never skip the restore. It also attaches the report, so the report
        // carries both the restore result and the timeout reset.
        addTeardownBlock { [self] in
            if sampleTouched {
                let sample = SampleApp()
                sample.relaunch() // sampleTouched is set only after "Evaluate at launch" read as off, so this start does not call Evaluate
                // Nothing is scrolled here: a picker that is not hittable counts as "failed".
                switch sample.ensureNoTimeout() {
                case .some(true): report.timeoutReset = "read-back"
                case .some(false): report.timeoutReset = "failed"
                case .none: report.timeoutReset = "not applicable"
                }
                sample.app.terminate()
            }
            addJSON(to: self, report, name: "i18-report")
            announceNothingChanged(changed)
        }
        // Teardown: restore the scenario first and record whether it worked.
        addTeardownBlock { [self] in
            guard changed, let original = report.originalScenario else { return }
            do {
                report.restored = try SandboxSettings.select(original)
                if report.restored != "read-back" { XCTFail("the restore was not confirmed by reading it back") }
            } catch {
                report.restored = "failed"
                XCTFail("could not restore the original scenario: \(error)")
            }
        }

        // Checked before Settings is touched, so a failure stops the run with nothing changed (NOTHING-CHANGED). The sample must have
        // the new controls (an older install does not), and "Evaluate at launch" must read as off: it is only read, never switched or
        // scrolled to, because a swipe could land on Apple's age sheet, which an evaluating relaunch can raise. The sample is stopped
        // before any throw, and sampleTouched is set only once launch is confirmed off.
        let sample = SampleApp()
        sample.relaunch()
        let missing = sample.missingI18Controls
        guard missing.isEmpty else {
            if ResultParser.launchGuard(launchOn: sample.evaluateAtLaunch) == .proceed { sample.attachTree() } // only with launch evaluation off: no Apple sheet text in the tree
            sample.app.terminate()
            throw SandboxSettingsError.elementNotFound("sample control(s) not found: \(missing.joined(separator: ", ")) (install the current Debug sample; the tree is attached as sample-tree)")
        }
        guard ResultParser.launchGuard(launchOn: sample.evaluateAtLaunch) == .proceed else {
            sample.app.terminate()
            report.stoppedBy = "Evaluate at launch is on; turn it off in the sample and re-run"
            throw SandboxSettingsError.verificationFailed("Evaluate at launch is on (or could not be read); turn it off in the sample and re-run")
        }
        sampleTouched = true

        let (current, assumed) = try SandboxSettings.originalScenario()
        announceOriginalScenario(current)
        addJSON(to: self, OriginalScenario(scenario: current), name: "original-scenario") // the script restores from this if the run dies
        guard let original = current else {
            report.originalUnreadable = true
            XCTFail("original unreadable")
            return
        }
        report.originalScenario = original
        report.originalAssumed = assumed

        changed = true
        let off = SandboxSettings.scenarios[0]
        let how = try SandboxSettings.select(off)
        let reliable = how == "read-back"

        sample.relaunch()
        try sample.setEnabled(true)
        try sample.setGates("[13,16,18]")

        // Pass 1: request timeout 15 s, wait up to 30 s.
        let set1 = try sample.setRequestTimeout("15000")
        report.rows.append(try i18Row(sample, row: "i18-timeout", note: "request timeout 15000 ms, waited up to 30 s",
                                      verdict: reliable && set1 ? "recorded" : "unknown", wait: 30, cancelAfter: nil))

        // Pass 2: no timeout; Cancel after about 5 s.
        sample.relaunch()
        try sample.setEnabled(true)
        try sample.setGates("[13,16,18]")
        let set2 = try sample.setRequestTimeout("None")
        report.rows.append(try i18Row(sample, row: "i18-cancel", note: "no request timeout, Cancel tapped after about 5 s",
                                      verdict: reliable && set2 ? "recorded" : "unknown", wait: 15, cancelAfter: 5))
    }

    /// One Evaluate in the sample's current process. `cancelAfter` seconds after the tap, the sample's Cancel button is tapped unless a
    /// result is already there, a sheet was seen, or the button cannot be tapped where it is (nothing is scrolled or tapped over system
    /// UI; the page may be scrolled once before Evaluate). The sheet probe covers the first 3 s after the tap and one more second just before Cancel; a sheet that appears later is
    /// only noted in the Settings trail, and makes the pass "unknown". A sheet is never answered. The outcome is one of
    /// ResultParser.outcome(of:).
    private func i18Row(_ sample: SampleApp, row: String, note: String, verdict: String, wait: TimeInterval, cancelAfter: TimeInterval?) throws -> ExplorationRow {
        var notes = [note]
        var verdict = verdict
        if !sample.resultText.isEmpty { notes.append("the result label was not empty before Evaluate"); verdict = "unknown" }
        // No swipe after Evaluate, because a swipe could land on Apple's sheet or a system prompt; so Cancel is scrolled into view
        // here, while nothing is in flight. If it is still not hittable at tap time, the pass is "unknown".
        if cancelAfter != nil { sample.scrollCancelIntoView() }
        try sample.tapEvaluate()
        let started = Date()
        let probe = SheetProbe.look(timeout: 3)
        var sheetSeen = probe.seen
        var text = ""
        var cancelStatus: String?
        if let cancelAfter {
            text = sample.waitForResult(timeout: max(0, cancelAfter - Date().timeIntervalSince(started)))
            if text.isEmpty {
                let late = SheetProbe.look(timeout: 1)
                sheetSeen = sheetSeen || late.seen
                let state = sample.cancelTappable
                if ResultParser.shouldTapCancel(sheetSeen: sheetSeen, hittable: state.hittable, foreground: state.foreground) {
                    try sample.tapCancel()
                    notes.append("Cancel tapped at \(Int(Date().timeIntervalSince(started))) s")
                    let status = sample.statusText
                    cancelStatus = status.hasPrefix("Cancel") ? status : "other" // the sample's own fixed texts only
                } else {
                    notes.append(sheetSeen ? "Cancel not tapped: a sheet was up"
                                           : "Cancel not tapped: the button was not hittable or the sample was not in the foreground")
                    verdict = "unknown"
                }
            } else {
                notes.append("a result was already shown before Cancel; Cancel not tapped")
            }
        }
        if text.isEmpty { text = sample.waitForResult(timeout: max(0, wait - Date().timeIntervalSince(started))) }
        let waited = Int(Date().timeIntervalSince(started) * 1000)
        let status = sample.statusText
        if SandboxSettings.noteSystemSheets("system sheet after the I18 pass") {
            notes.append("a system sheet was up in Settings or SpringBoard after the pass (see the Settings trail)")
            verdict = "unknown"
        }
        sample.app.terminate() // so a lingering sheet cannot block the next Settings step; nothing is tapped to dismiss it
        var result = SampleApp.parse(text)
        let outcome = ResultParser.outcome(of: text)
        if text.isEmpty && !status.isEmpty { result["Status"] = status }
        result["I18Outcome"] = outcome
        result["WaitedMs"] = String(waited)
        if let elapsed = ResultParser.elapsedMs(text) { result["ElapsedMs"] = String(elapsed) }
        if let cancelStatus { result["CancelStatus"] = cancelStatus }
        if sheetSeen { notes.append("a sheet was on screen and was left unanswered"); verdict = "unknown" }
        return ExplorationRow(row: row, scenario: SandboxSettings.scenarios[0], sheetSeen: sheetSeen, sheetFindings: probe.findings,
                              result: result, verdict: verdict, note: notes.joined(separator: "; "),
                              matchedProcess: probe.seen ? probe.process : nil, matchedContainer: probe.seen ? probe.container : nil,
                              expected: nil, check: "not-checked")
    }
}

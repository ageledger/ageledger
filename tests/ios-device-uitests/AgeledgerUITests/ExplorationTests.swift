import XCTest

/// Runs ONCE against the phone: the first appearance of Apple's sheet cannot be repeated, so every step is recorded.
final class ExplorationTests: XCTestCase {
    private var report = ExplorationReport()

    override func setUp() { continueAfterFailure = false }

    func testExploration() throws {
        addAlwaysCleanup(to: self)
        var changed = false
        // Teardown: restore first, record whether it worked, then attach.
        addTeardownBlock { [self] in
            // The report is attached BEFORE any failure is raised, so the evidence survives a failed restore.
            var failure: String?
            if changed, let original = report.originalScenario {
                do {
                    report.restored = try SandboxSettings.select(original)
                    if report.restored != "read-back" { failure = "the restore was not confirmed by reading it back" }
                } catch {
                    report.restored = "failed"
                    failure = "could not restore the original scenario: \(error)"
                }
            }
            addJSON(to: self, report, name: "exploration-report")
            announceNothingChanged(changed)
            if let failure { XCTFail(failure) }
        }

        // a. read and remember the current scenario (also the first Settings session); stop before changing anything if unreadable
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

        // b. adult, confirmed, not applicable
        let first = SandboxSettings.adultConfirmedNotApplicable
        changed = true
        let firstHow = try SandboxSettings.select(first)

        // c. configure the sample
        let sample = SampleApp()
        sample.relaunch()
        try sample.setEnabled(true)
        try sample.setGates("[13,16,18]")

        // d. first Evaluate: probe, record, screenshot of the sample only, answer Don't Share
        let firstRow = try evaluateRow(self, sample, row: "first-evaluate", scenario: first, answer: "Don't Share", verdict: firstHow == "read-back" ? "recorded" : "unknown", screenshotName: "sample-with-sheet")
        report.rows.append(firstRow)
        addJSON(to: self, firstRow.sheetFindings, name: "sheet-findings-first")

        // f. Evaluate again: does a sheet re-appear after Don't Share? If so, answer Share.
        let second = try evaluateRow(self, sample, row: "second-evaluate", scenario: first, answer: "Share", verdict: firstHow == "read-back" ? "recorded" : "unknown")
        report.rows.append(second)
        // A remembered Don't Share (or a first sheet never seen) means later rows may reflect a stored answer: unknown, not pass.
        report.declineRemembered = firstRow.sheetSeen && !second.sheetSeen
        // If neither Evaluate showed a sheet there was no answer to remember, so a row whose switch read back is "recorded". Only a sheet
        // that appeared once and not again (a remembered answer) makes the later rows "unknown".
        let reliable = !firstRow.sheetSeen || second.sheetSeen

        // g. the other scenarios, a fresh sample process each time
        for scenario in SandboxSettings.scenarios where scenario != first {
            do {
                // A switch that could not be read back makes the row unknown: the result may belong to another scenario.
                let how = try SandboxSettings.select(scenario)
                report.rows.append(try evaluateRow(self, sample, row: "scenario-\(report.rows.count)", scenario: scenario,
                                                   answer: "Share", verdict: reliable && how == "read-back" ? "recorded" : "unknown"))
            } catch SandboxSettingsError.systemAgeSheet {
                // Apple's own age sheet appeared in Settings: it was left unanswered and Settings was closed. The remaining rows are
                // not run; the teardown restores the scenario.
                report.stoppedBy = "system age sheet"
                XCTFail("a system age sheet appeared; the remaining rows were not run")
                break
            }
        }
    }
}

/// Selects the scenario named in RESTORE_SCENARIO (the script passes it as TEST_RUNNER_RESTORE_SCENARIO) and verifies it by reading back.
final class RestoreTests: XCTestCase {
    override func setUp() { continueAfterFailure = false }

    func testRestore() throws {
        addAlwaysCleanup(to: self)
        struct Outcome: Codable { var restored: Bool; var scenario: String; var confirmation: String }
        let target = ProcessInfo.processInfo.environment["RESTORE_SCENARIO"] ?? ""
        guard SandboxSettings.scenarios.contains(target) else {
            XCTFail("RESTORE_SCENARIO is not a known scenario label")
            return
        }
        do {
            let how = try SandboxSettings.select(target)
            addJSON(to: self, Outcome(restored: how == "read-back", scenario: target, confirmation: how), name: "restore-report")
            // Only a read-back counts: the script keeps its restore file until this test passes.
            XCTAssertEqual(how, "read-back", "the restore was not confirmed by reading it back")
        } catch {
            addJSON(to: self, Outcome(restored: false, scenario: target, confirmation: "failed"), name: "restore-report")
            throw error
        }
    }
}

/// Run after a reinstall of the sample (the script uninstalls and installs it): does the sheet come back? The sample's Enabled
/// preference is wiped by a reinstall, so it is turned on again here. Result is "unknown" when the sheet could not be answered.
final class ReinstallProbeTests: XCTestCase {
    private var report = ExplorationReport()

    override func setUp() { continueAfterFailure = false }

    func testReinstallProbe() throws {
        addAlwaysCleanup(to: self)
        var changed = false
        addTeardownBlock { [self] in
            // The report is attached BEFORE any failure is raised, so the evidence survives a failed restore.
            var failure: String?
            if changed, let original = report.originalScenario {
                do {
                    report.restored = try SandboxSettings.select(original)
                    if report.restored != "read-back" { failure = "the restore was not confirmed by reading it back" }
                } catch {
                    report.restored = "failed"
                    failure = "could not restore the original scenario: \(error)"
                }
            }
            addJSON(to: self, report, name: "reinstall-probe")
            announceNothingChanged(changed)
            if let failure { XCTFail(failure) }
        }
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
        let scenario = SandboxSettings.adultConfirmedNotApplicable
        try SandboxSettings.select(scenario)

        let sample = SampleApp()
        sample.relaunch()
        try sample.setEnabled(true)
        try sample.setGates("[13,16,18]")
        report.rows.append(try evaluateRow(self, sample, row: "after-reinstall", scenario: scenario, answer: "Don't Share", screenshotName: "sample-with-sheet"))
    }
}

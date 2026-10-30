import XCTest

/// The sample app, attached by bundle id. Controls are found by the AutomationIds set in samples/AgeledgerSample/MainPage.cs.
struct SampleApp {
    /// Run from the Xcode GUI without the variable, this falls back to the project's default id.
    /// The runner gets AGELEDGER_SAMPLE_ID from scripts/ios-uitests.sh (xcodebuild passes TEST_RUNNER_* variables on without the prefix).
    static let bundleId = ProcessInfo.processInfo.environment["AGELEDGER_SAMPLE_ID"] ?? "com.ageledger.sample"
    let app = XCUIApplication(bundleIdentifier: SampleApp.bundleId)

    /// Terminates (if running) and launches a fresh process.
    func relaunch() {
        app.terminate()
        app.launch()
        _ = app.buttons["evaluate"].waitForExistence(timeout: 15)
    }

    func setEnabled(_ on: Bool) throws {
        let toggle = app.switches["enabled"]
        guard toggle.waitForExistence(timeout: 8) else { throw SandboxSettingsError.elementNotFound("switch enabled") }
        if (toggle.value as? String == "1") != on { toggle.tap(); Thread.sleep(forTimeInterval: 0.4) }
    }

    /// Picks a preset by its name, such as "[13,16,18]".
    func setGates(_ name: String) throws { try pick("age-gates", name) }

    /// Picks the request timeout by its name: "None" or a number of milliseconds such as "15000". Returns true when the picker then shows it.
    func setRequestTimeout(_ name: String) throws -> Bool {
        try pick("request-timeout-ms", name)
        return pickerValue("request-timeout-ms") == name
    }

    func tapEvaluate() throws { try tapButton("evaluate") }

    /// True when the sample has the request-timeout picker (an older sample does not).
    var hasRequestTimeoutPicker: Bool { app.textFields["request-timeout-ms"].exists || app.buttons["request-timeout-ms"].exists }

    /// Sets the timeout to "None" with read-back, but only if the picker exists and shows something else. nil = no picker (older sample).
    /// Never scrolls: a picker that is not hittable where it is counts as not set (false), because a swipe could land on a system sheet.
    func ensureNoTimeout() -> Bool? {
        guard hasRequestTimeoutPicker else { return nil }
        if pickerValue("request-timeout-ms") == "None" { return true }
        do {
            try pick("request-timeout-ms", "None", scroll: false)
            return pickerValue("request-timeout-ms") == "None"
        } catch { return false }
    }

    /// The value of "Evaluate at launch", read from the element without scrolling or tapping (its value is readable even when it is
    /// off screen). nil = it could not be read (switch missing or no value); the caller must then treat it as on.
    var evaluateAtLaunch: Bool? {
        let toggle = app.switches["evaluate-at-launch"]
        guard toggle.waitForExistence(timeout: 5), let value = toggle.value as? String else { return nil }
        return value == "1"
    }

    /// Whether the I18 controls exist (the picker and the Cancel button).
    var hasI18Controls: Bool { hasRequestTimeoutPicker && app.buttons["cancel"].waitForExistence(timeout: 5) }

    /// Taps the sample's Cancel button (it cancels the most recent Evaluate still running; with none it says so in the status line).
    /// Never scrolls: it throws unless the button is hittable where it is and the sample is the foreground app, so the tap cannot land
    /// on a system sheet or another app. The caller decides first with ResultParser.shouldTapCancel.
    func tapCancel() throws {
        let button = app.buttons["cancel"]
        guard button.waitForExistence(timeout: 3) else { throw SandboxSettingsError.elementNotFound("button cancel") }
        guard ResultParser.shouldTapCancel(sheetSeen: false, hittable: button.isHittable, foreground: app.state == .runningForeground) else {
            throw SandboxSettingsError.refusedTap("Cancel: not hittable or the sample is not in the foreground")
        }
        button.tap()
    }

    /// Whether Cancel could be tapped right now without scrolling (hittable, sample in the foreground).
    var cancelTappable: (hittable: Bool, foreground: Bool) {
        (app.buttons["cancel"].isHittable, app.state == .runningForeground)
    }

    var resultText: String { text(of: "result") }
    var statusText: String { text(of: "status") }

    /// Reads a label through a fresh query and one snapshot, so a process that is gone gives "" instead of a test failure.
    private func text(of id: String) -> String {
        let query = app.staticTexts.matching(identifier: id)
        guard query.count > 0, let snap = try? query.firstMatch.snapshot() else { return "" }
        return snap.label
    }

    /// Waits until the result label shows an outcome, or the timeout passes. Returns the label.
    func waitForResult(timeout: TimeInterval) -> String {
        let end = Date().addingTimeInterval(timeout)
        while Date() < end {
            let text = resultText
            if text.contains("State:") || text.contains("Outcome:") || text.contains("Not evaluated") || text.contains("Exception") { return text }
            Thread.sleep(forTimeInterval: 0.5)
        }
        return resultText
    }

    /// Parses the result label by key name (see ResultParser); elapsed time is left out.
    static func parse(_ text: String) -> [String: String] { ResultParser.parse(text) }

    func screenshot() -> XCUIScreenshot { app.screenshot() }

    private func pickerValue(_ id: String) -> String? {
        var control = app.textFields[id]
        if !control.exists { control = app.buttons[id] }
        guard control.exists, let snap = try? control.snapshot() else { return nil }
        return snap.value as? String
    }

    private func tapButton(_ id: String) throws {
        let button = app.buttons[id]
        guard button.waitForExistence(timeout: 8) else { throw SandboxSettingsError.elementNotFound("button \(id)") }
        if !button.isHittable { app.swipeUp() }
        button.tap()
    }

    private func pick(_ id: String, _ value: String, scroll: Bool = true) throws {
        var control = app.textFields[id]
        if !control.waitForExistence(timeout: 5) { control = app.buttons[id] }
        guard control.exists else { throw SandboxSettingsError.elementNotFound("picker \(id)") }
        if !control.isHittable {
            guard scroll else { throw SandboxSettingsError.refusedTap("picker \(id) is not hittable; not scrolling") }
            app.swipeUp()
        }
        control.tap()
        let wheel = app.pickerWheels.firstMatch
        guard wheel.waitForExistence(timeout: 5) else { throw SandboxSettingsError.elementNotFound("picker wheel for \(id)") }
        wheel.adjust(toPickerWheelValue: value)
        let done = app.buttons["Done"]
        if done.exists { done.tap() }
        Thread.sleep(forTimeInterval: 0.4)
    }
}

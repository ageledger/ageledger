import XCTest

enum SandboxSettingsError: Error, CustomStringConvertible {
    case elementNotFound(String)
    case verificationFailed(String)
    case unexpectedAlert
    case refusedTap(String)
    case authenticationTimedOut
    case systemAgeSheet
    var description: String {
        switch self {
        case .authenticationTimedOut: return "the Touch ID prompt was still up after the wait; nothing was tapped"
        case .systemAgeSheet: return "Apple's age sheet appeared in Settings; it was left unanswered and Settings was closed"
        case .refusedTap(let why): return "refused tap: \(why)"
        case .unexpectedAlert: return "unexpected alert in Settings or SpringBoard (title and button labels recorded; nothing tapped)"
        case .elementNotFound(let what): return "element not found: \(what)"
        case .verificationFailed(let what): return "verification failed: \(what)"
        }
    }
}

/// Drives Settings > Developer > Sandbox Apple Account > (account) > Manage > Account Settings > Age Assurance. This is the only
/// file that knows that path. Rules:
/// - Settings is terminated and relaunched only when no live session can be reused (one authenticated session per run).
/// - Only exact identifiers or exact labels on the navigation path are tapped, and after every tap the expected next screen
///   is checked before the next tap. Nothing else on the Developer page is reachable.
/// - The account cell is the cell directly under the "Sandbox Apple Account" section; it is recognised by the "@" in its
///   label, which is never printed or recorded. There is no fallback to "some other cell".
/// - Nothing here takes a screenshot of Settings; a missing element is reported by the name we looked for, never by a tree dump.
/// - What is recorded about Settings is limited to a fixed list of known Apple labels plus counts of anything else.
enum SandboxSettings {
    static let bundleId = "com.apple.Preferences"

    /// Apple's scenario labels on iOS 27, exact.
    static let scenarios = [
        "Off",
        "User under 13, significant change approved",
        "User 13-15, significant change approved",
        "User 16-17, significant change declined",
        "User 18+, age not confirmed, significant change not applicable",
        "User 18+, age confirmed, significant change not applicable",
        "User 18+, age confirmed, significant change applicable",
    ]
    static let adultConfirmedNotApplicable = "User 18+, age confirmed, significant change not applicable"

    private static let accountSection = "Sandbox Apple Account"

    /// Labels that may be written to the trail. Anything else is only counted.
    private static let knownLabels: Set<String> = Set([
        "Settings", "Developer", "Sandbox Apple Account", "Manage", "Account Settings", "Done", "Sandbox Testing",
        "Clear Purchase History", "Age Assurance", "Revoke App Consent", "Sign Out", "Cancel", "OK", "Test Age Assurance",
        "Touch ID to continue with Settings.",
        "Age-Appropriate Experiences in Apps", "Continue", "Update Birthday",
    ]).union(scenarios)

    /// True while Settings is known to be on the Age Assurance screen after a full navigation (one authenticated session).
    private static var sessionValid = false

    /// Terminates and relaunches Settings, then navigates to the Age Assurance screen. Only used when no live session can be reused.
    static func open() throws -> XCUIApplication {
        invalidate()
        // A lingering Apple sheet in the sample must not block Settings; the sample is stopped, never tapped.
        XCUIApplication(bundleIdentifier: SampleApp.bundleId).terminate()
        let settings = XCUIApplication(bundleIdentifier: bundleId)
        settings.terminate()
        settings.launch()
        _ = settings.cells.firstMatch.waitForExistence(timeout: 10)
        trail.append(["screen": "open", "matchedType": "", "navBar": ""])
        if trail.count > 100 { trail.removeFirst(trail.count - 100) }
        do {
            try navigate(settings)
            if !recordedStructure { recordedStructure = true; attachTrail() }
            sessionValid = true
            return settings
        } catch {
            // Records an alert, if one is why we failed; the errors that already recorded one (or timed out waiting) are not checked again.
            switch error {
            case SandboxSettingsError.unexpectedAlert, SandboxSettingsError.authenticationTimedOut, SandboxSettingsError.systemAgeSheet: break
            default: try? failOnAlert(settings)
            }
            attachTrail()
            invalidate()
            settings.terminate() // do not leave Settings on a half-way screen; nothing is tapped to get out
            throw error
        }
    }

    private static func navigate(_ settings: XCUIApplication) throws {
        // Identifiers are guesses that are tried first; exact labels are the fallback.
        try tapRow(settings, ids: ["com.apple.settings.developer", "DEVELOPER_SETTINGS"], label: "Developer", scroll: true)
        try expect(settings, "Developer screen") { settings.navigationBars["Developer"].exists ? "navigationBar" : nil }

        try tapAccount(settings)
        try chooseManageInAccountSheet(settings)
        // After Manage the screen offers Age Assurance and Revoke App Consent (among other rows); either label on any element type will do.
        try expect(settings, "Manage screen") {
            manageScreenMarker(settings)
        }

        try scrollToAndTapAgeAssurance(settings)
        try expect(settings, "Age Assurance screen") { strongMarkers(settings) ? "strong markers" : nil }
    }

    /// Reuses the live Settings session when it is still on the Age Assurance screen (no terminate, no relaunch, so no new
    /// authentication prompt); otherwise navigates again.
    private static func session() throws -> XCUIApplication {
        let settings = XCUIApplication(bundleIdentifier: bundleId)
        if sessionValid {
            XCUIApplication(bundleIdentifier: SampleApp.bundleId).terminate()
            settings.activate()
            // Up to 15 s: an authentication sheet may appear a moment after Settings comes back. Never terminated here.
            let end = Date().addingTimeInterval(15)
            var diagnosis = ""
            repeat {
                do { try failOnAlert(settings) } catch {
                    invalidate()
                    settings.terminate()
                    throw error
                }
                if settings.state == .runningForeground, strongMarkers(settings) {
                    trail.append(["screen": "session reused", "matchedType": "", "navBar": ""])
                    return settings
                }
                diagnosis = lostDiagnosis(settings)
                Thread.sleep(forTimeInterval: 0.5)
            } while Date() < end
            // Exactly which check failed, so we learn whether iOS locks the page again when Settings goes to the background.
            trail.append(["screen": "session lost", "matchedType": "", "navBar": "", "labels": diagnosis])
        }
        return try open()
    }

    /// Which part of the Age Assurance check failed, as plain facts (state code, header present, nav bar title, long labels found).
    private static func lostDiagnosis(_ settings: XCUIApplication) -> String {
        let header = settings.staticTexts["Test Age Assurance"].exists
        let longLabels = scenarios.dropFirst().filter { find(settings, ids: [], label: $0) != nil }.count
        return "state=\(settings.state.rawValue) header=\(header) navBar=\(navBarTitle(settings)) longLabels=\(longLabels)"
    }

    /// The first navigation bar's title if it is a known Apple label, otherwise just "other" (or "none").
    private static func navBarTitle(_ settings: XCUIApplication) -> String {
        let bars = settings.navigationBars
        guard bars.count > 0, let snap = try? bars.firstMatch.snapshot() else { return "none" }
        return knownLabels.contains(snap.identifier) ? snap.identifier : "other"
    }

    /// Marks the session as not reusable and says where in this file that happened.
    private static func invalidate(_ line: Int = #line) {
        sessionValid = false
        trail.append(["screen": "session invalidated", "matchedType": "", "navBar": "", "labels": "SandboxSettings.swift:\(line)"])
    }

    /// The Age Assurance page, and only it: the "Test Age Assurance" text or an "Age Assurance" navigation bar, AND at least two of
    /// the long scenario labels present and hittable. "Off" alone never counts. Fresh queries only.
    private static func strongMarkers(_ settings: XCUIApplication) -> Bool {
        let header = settings.staticTexts["Test Age Assurance"].exists || settings.navigationBars["Age Assurance"].exists
        guard header else { return false }
        var found = 0
        for label in scenarios.dropFirst() where find(settings, ids: [], label: label) != nil {
            found += 1
            if found >= 2 { return true }
        }
        return false
    }

    /// Fresh-query check of the strong markers, polled for up to 5 s.
    private static func onAgeAssuranceScreen(_ settings: XCUIApplication) -> Bool {
        let end = Date().addingTimeInterval(5)
        repeat {
            if settings.state == .runningForeground, strongMarkers(settings) { return true }
            Thread.sleep(forTimeInterval: 0.4)
        } while Date() < end
        return false
    }

    private typealias Node = (node: XCUIElementSnapshot, parent: XCUIElementSnapshot?)

    /// All nodes of one snapshot with their parents (capped), so a row can be judged together with its cell.
    private static func nodes(of settings: XCUIApplication) -> [Node] {
        guard let root = try? settings.snapshot() else { return [] }
        var all: [Node] = []
        func walk(_ node: XCUIElementSnapshot, _ parent: XCUIElementSnapshot?) {
            guard all.count < 1500 else { return }
            all.append((node, parent))
            for child in node.children { walk(child, node) }
        }
        walk(root, nil)
        return all
    }

    private static func valueText(_ node: XCUIElementSnapshot) -> String {
        node.value.map { "\($0)" } ?? "nil"
    }

    /// Reads which scenario is selected, from one snapshot. Observed on iOS 27.0.1: the selected row's container has the value "1" and
    /// every other row "0"; all long rows at "0" means "Off". Any other OS version may differ, in which case this returns nil
    /// (no signal) and nothing is confirmed.
    private static func readSelected(_ settings: XCUIApplication) -> String? {
        readSelectedDetail(settings).label
    }

    /// "Off" is concluded only when ALL six long rows are present and every one reads "0"; with fewer rows seen the answer is nil.
    private static func readSelectedDetail(_ settings: XCUIApplication) -> (label: String?, zeroRows: Int) {
        let all = nodes(of: settings)
        var zero = Set<String>()
        for scenario in scenarios {
            for (node, _) in all where node.label == scenario && node.elementType != .staticText {
                let v = valueText(node)
                if v == "1" { return (scenario, zero.count) }
                if v == "0" && scenario != scenarios[0] { zero.insert(scenario) }
            }
        }
        return (zero.count == scenarios.count - 1 ? scenarios[0] : nil, zero.count)
    }

    /// The scenario Settings starts in, read from the page. When nothing reads as selected (an OS version without the signal above)
    /// the original is "Off" only if the script was started with --assume-original-off (ASSUME_ORIGINAL=Off); otherwise the label
    /// is nil and the test stops before changing anything.
    static func originalScenario() throws -> (label: String?, assumed: Bool) {
        let settings = try session()
        let detail = readSelectedDetail(settings)
        trail.append(["screen": "scenario rows read", "matchedType": "", "navBar": "", "labels": "zeroRows=\(detail.zeroRows) of \(scenarios.count - 1)"])
        if let selected = detail.label { return (selected, false) }
        if strongMarkers(settings), ProcessInfo.processInfo.environment["ASSUME_ORIGINAL"] == "Off" {
            trail.append(["screen": "original assumed Off", "matchedType": "", "navBar": ""])
            return ("Off", true)
        }
        return (nil, false)
    }

    /// Selects a scenario by its exact label (it must be one of `scenarios`), on the Age Assurance page only. Returns "read-back" when
    /// the page then shows exactly that scenario as selected, and "unconfirmed" when the page was left or shows no selection. A
    /// different scenario reading as selected is an error. Callers that need certainty (a restore) must require "read-back".
    @discardableResult
    static func select(_ scenario: String) throws -> String {
        guard scenarios.contains(scenario) else { throw SandboxSettingsError.elementNotFound("unknown scenario label") }
        var settings = try session()
        // The markers are re-checked after the scroll and immediately before the row is looked up: a tap only lands on this page.
        var onPage = false
        for attempt in 0..<2 {
            try scrollToTop(settings)
            if strongMarkers(settings) { onPage = true; break }
            trail.append(["screen": "session lost", "matchedType": "", "navBar": ""])
            invalidate()
            if attempt == 0 { settings = try open() }
        }
        guard onPage else {
            settings.terminate()
            throw SandboxSettingsError.verificationFailed("not on the Age Assurance page before the tap")
        }
        // Already selected: nothing to tap. A restore to an Off that is already selected must not touch the row (tapping it could raise
        // Apple's own sheet).
        if readSelected(settings) == scenario {
            trail.append(["screen": "select", "matchedType": "read-back (already selected)", "navBar": "", "labels": scenario])
            return "read-back"
        }
        guard let element = findRow(settings, scenario) else {
            invalidate()
            settings.terminate()
            throw SandboxSettingsError.elementNotFound("scenario \"\(scenario)\"")
        }
        try safeTap(element)
        Thread.sleep(forTimeInterval: 0.8)
        try failOnAlert(settings)
        var confirmation = "unconfirmed"
        if onAgeAssuranceScreen(settings) {
            if let shown = readSelected(settings) {
                guard shown == scenario else {
                    invalidate()
                    settings.terminate()
                    throw SandboxSettingsError.verificationFailed("wanted \"\(scenario)\", Settings reports \"\(shown)\"")
                }
                confirmation = "read-back"
            }
        } else {
            invalidate() // the page was left after the tap: the next call navigates again
            trail.append(["screen": "page left after tap", "matchedType": "", "navBar": ""])
        }
        trail.append(["screen": "select", "matchedType": confirmation, "navBar": "", "labels": scenario])
        return confirmation
    }

    // MARK: - Internals

    /// Swipes down (never taps) so the list is at its top before a row is looked for.
    private static func scrollToTop(_ settings: XCUIApplication) throws {
        for _ in 0..<3 { try guardedSwipe(settings, up: false) }
    }

    /// Every swipe in Settings goes through here: it is refused while the Touch ID prompt is on screen, because the gesture could land on it.
    private static func guardedSwipe(_ settings: XCUIApplication, up: Bool) throws {
        if authPromptVisible() { throw SandboxSettingsError.refusedTap("the Touch ID prompt is on screen; not swiping") }
        if up { settings.swipeUp() } else { settings.swipeDown() }
    }

    /// A row whose label OR value is exactly this text (the "Off" row only carries it as a value), on any of the usual element types;
    /// the row container is preferred over the text inside it.
    private static func findRow(_ settings: XCUIApplication, _ text: String) -> XCUIElement? {
        let predicate = NSPredicate(format: "label == %@ OR value == %@", text, text)
        let queries: [XCUIElementQuery] = [settings.otherElements, settings.cells, settings.buttons, settings.staticTexts]
        for query in queries {
            let match = query.matching(predicate).firstMatch
            if match.exists && match.isHittable { return match }
        }
        return nil
    }

    // MARK: Taps

    /// The only way anything is tapped in this file. Refuses labels that sound destructive ("Sign Out", reset, delete, clear, erase,
    /// revoke), by the label we mean to tap and by the element's own label.
    private static func safeTap(_ element: XCUIElement, expecting label: String? = nil) throws {
        for text in [label ?? "", element.exists ? element.label : ""] {
            for word in ["sign out", "reset", "delete", "clear", "erase", "revoke"] where text.range(of: word, options: .caseInsensitive) != nil {
                throw SandboxSettingsError.refusedTap("a control whose label contains \"\(word)\"")
            }
        }
        // Never tap while the Touch ID prompt is on screen: the tap could land on it.
        if authPromptVisible() { throw SandboxSettingsError.refusedTap("the Touch ID prompt is on screen") }
        element.tap()
    }

    /// On the "Account Settings" screen, swipes up (never taps) at most 8 times until a control labelled exactly "Age Assurance" is
    /// hittable, then taps it. The trail records which known labels were visible at each step, and how many others.
    private static func scrollToAndTapAgeAssurance(_ settings: XCUIApplication) throws {
        try expect(settings, "Account Settings screen") { settings.navigationBars["Account Settings"].exists ? "navigationBar" : nil }
        let predicate = NSPredicate(format: "label == %@", "Age Assurance")
        for step in 0...8 {
            try failOnAlert(settings)
            recordVisibleLabels(settings, step: step)
            let queries = [settings.cells, settings.buttons, settings.staticTexts, settings.links]
            for query in queries {
                let match = query.matching(predicate).firstMatch
                if match.exists && match.isHittable {
                    try safeTap(match, expecting: "Age Assurance")
                    Thread.sleep(forTimeInterval: 0.6)
                    return
                }
            }
            if step < 8 { try guardedSwipe(settings, up: true); Thread.sleep(forTimeInterval: 0.5) }
        }
        throw SandboxSettingsError.elementNotFound("Age Assurance")
    }

    /// Records, from one snapshot, the visible cell, button and text labels that are on the known list, and only the count of the rest.
    private static func recordVisibleLabels(_ settings: XCUIApplication, step: Int) {
        let all = nodes(of: settings)
        var known: [String] = []
        var others = 0
        for (node, _) in all where node.elementType == .cell || node.elementType == .button || node.elementType == .staticText {
            guard !node.label.isEmpty else { continue }
            if knownLabels.contains(node.label) { if !known.contains(node.label) { known.append(node.label) } } else { others += 1 }
        }
        trail.append(["screen": "account settings, swipe \(step)", "matchedType": "", "navBar": "",
                      "labels": known.prefix(25).joined(separator: " | ") + " | others=\(others)"])
    }

    /// The account cell opens an action sheet. Only a sheet or alert in Settings titled exactly "Sandbox Apple Account" whose
    /// buttons are exactly Manage, Sign Out and Cancel (any order) is accepted; then only "Manage", found inside that sheet, is
    /// tapped. Anything else is recorded (known labels only) and the run stops with nothing tapped.
    private static func chooseManageInAccountSheet(_ settings: XCUIApplication) throws {
        let expectedButtons: Set<String> = ["Manage", "Sign Out", "Cancel"]
        // Only fresh queries and one snapshot are used: a stored element is never touched after a tap that can remove it.
        var query: XCUIElementQuery?
        var snapshot: XCUIElementSnapshot?
        let end = Date().addingTimeInterval(8)
        repeat {
            for candidate in [settings.sheets, settings.alerts] where candidate.count > 0 {
                if let snap = try? candidate.firstMatch.snapshot() { query = candidate; snapshot = snap; break }
            }
            if snapshot != nil { break }
            Thread.sleep(forTimeInterval: 0.4)
        } while Date() < end
        guard let sheetQuery = query, let snap = snapshot else {
            trail.append(["screen": "account sheet", "matchedType": "none", "navBar": ""])
            throw SandboxSettingsError.verificationFailed("did not reach the account sheet")
        }
        let buttons = buttonLabels(in: snap)
        let titleMatches = snap.label == accountSection || staticTextLabels(in: snap).contains(accountSection)
        guard titleMatches, buttons.count == expectedButtons.count, Set(buttons) == expectedButtons else {
            try failOnAlert(settings) // records the title and buttons (known labels only) and throws
            throw SandboxSettingsError.unexpectedAlert
        }
        let manage = sheetQuery.buttons.matching(NSPredicate(format: "label == %@", "Manage")).firstMatch
        guard manage.exists else { throw SandboxSettingsError.elementNotFound("Manage in the account sheet") }
        let kind = typeName(snap.elementType)
        try safeTap(manage, expecting: "Manage")
        trail.append(["screen": "account sheet -> Manage", "matchedType": kind, "navBar": ""])
        // The sheet must be gone before the next screen is judged: re-query, never reuse the old element.
        let gone = Date().addingTimeInterval(8)
        while settings.alerts.count + settings.sheets.count > 0 && Date() < gone { Thread.sleep(forTimeInterval: 0.3) }
        guard settings.alerts.count + settings.sheets.count == 0 else { throw SandboxSettingsError.verificationFailed("account sheet did not close") }
    }

    private static func buttonLabels(in node: XCUIElementSnapshot) -> [String] {
        var labels: [String] = []
        for child in node.children {
            if child.elementType == .button { labels.append(child.label) }
            labels += buttonLabels(in: child)
        }
        return labels
    }

    private static func elements(of type: XCUIElement.ElementType, in node: XCUIElementSnapshot) -> [XCUIElementSnapshot] {
        var found: [XCUIElementSnapshot] = []
        for child in node.children {
            if child.elementType == type { found.append(child) }
            found += elements(of: type, in: child)
        }
        return found
    }

    private static func staticTextLabels(in node: XCUIElementSnapshot) -> [String] {
        var labels: [String] = []
        for child in node.children {
            if child.elementType == .staticText { labels.append(child.label) }
            labels += staticTextLabels(in: child)
        }
        return labels
    }

    // MARK: Trail and alerts

    /// What matched at each step (screen name, element type, known nav bar title): Apple UI text only.
    private static var trail: [[String: String]] = []
    private static var recordedStructure = false

    static func typeName(_ type: XCUIElement.ElementType) -> String {
        switch type {
        case .cell: return "cell"
        case .button: return "button"
        case .staticText: return "staticText"
        case .other: return "other"
        case .link: return "link"
        case .image: return "image"
        default: return "type\(type.rawValue)"
        }
    }

    /// Finds an element with one of the labels on any element type, using firstMatch.exists only. Returns the type name.
    private static func anyElement(_ app: XCUIApplication, labels: [String]) -> String? {
        let predicate = NSPredicate(format: "label IN %@", labels)
        let types: [XCUIElement.ElementType] = [.cell, .button, .staticText, .other, .link]
        for type in types where app.descendants(matching: type).matching(predicate).firstMatch.exists { return typeName(type) }
        return app.descendants(matching: .any).matching(predicate).firstMatch.exists ? "any" : nil
    }

    private static func attachJSON(_ value: some Encodable, name: String) {
        guard let data = try? JSONEncoder().encode(value) else { return }
        let attachment = XCTAttachment(data: data, uniformTypeIdentifier: "public.json")
        attachment.name = name
        attachment.lifetime = XCTAttachment.Lifetime.keepAlways
        XCTContext.runActivity(named: name) { $0.add(attachment) }
    }

    private static func attachTrail() { attachJSON(trail, name: "settings-structure") }

    /// For teardown: attaches the trail and stops Settings and the sample, whether or not a thrown error got us here.
    /// Nothing is tapped.
    static func attachTrailAndStop() {
        if !trail.isEmpty { attachTrail() }
        invalidate()
        XCUIApplication(bundleIdentifier: bundleId).terminate()
        XCUIApplication(bundleIdentifier: SampleApp.bundleId).terminate()
    }

    /// Notes, in the trail only, any alert or sheet that is up in Settings or SpringBoard right now: which process, and the title and
    /// button labels if they are known labels. Taps nothing and does not throw. Returns true when it saw one.
    @discardableResult
    static func noteSystemSheets(_ reason: String) -> Bool {
        var found = false
        for id in [bundleId, SheetProbe.springboard] {
            let app = XCUIApplication(bundleIdentifier: id)
            guard app.state == .runningForeground || app.state == .runningBackground else { continue }
            for query in [app.alerts, app.sheets] where query.count > 0 {
                // Same pattern as failOnAlert: a Touch ID element that is not on screen is not a sheet; anything else is noted.
                let all = query.allElementsBoundByIndex
                let flags = all.map { (isAuth: $0.identifier == authSheetIdentifier, visible: elementOnScreen($0)) }
                guard let index = ResultParser.alertToHandle(flags), let snap = try? all[index].snapshot() else { continue }
                let buttons = buttonLabels(in: snap).compactMap(known).joined(separator: " | ")
                trail.append(["screen": reason, "matchedType": id == bundleId ? "settings" : "springboard", "navBar": "",
                              "labels": "title=\(known(snap.label) ?? "other") buttons=\(buttons)"])
                found = true
            }
        }
        return found
    }

    /// Text for the record: only strings on the known list are kept, anything else is dropped (and counted by the caller).
    private static func known(_ text: String) -> String? {
        knownLabels.contains(text) ? text : nil
    }

    private static let systemAgeSheetTitle = "Age-Appropriate Experiences in Apps"

    private static let authSheetIdentifier = "com.apple.localauthentication.ax.authentication.alert"

    /// The SpringBoard elements that look like the Touch ID prompt (by identifier, or by the text "Touch ID to continue with Settings").
    /// SpringBoard can report such an element while nothing is on screen, so an element existing does not mean the prompt is up.
    private static func authElements() -> [XCUIElement] {
        let springboard = XCUIApplication(bundleIdentifier: SheetProbe.springboard)
        let queries: [XCUIElementQuery] = [
            springboard.alerts.matching(identifier: authSheetIdentifier),
            springboard.sheets.matching(identifier: authSheetIdentifier),
            springboard.otherElements.matching(identifier: authSheetIdentifier),
            springboard.staticTexts.matching(NSPredicate(format: "label CONTAINS[c] %@", "Touch ID to continue with Settings")),
        ]
        return queries.filter { $0.count > 0 }.map { $0.firstMatch }
    }

    /// True when such an element exists (it may not be on screen).
    private static func authElementPresent() -> Bool { !authElements().isEmpty }

    /// True only when the prompt is really up: an element that exists, is hittable and has a non-empty frame inside the screen.
    private static func authPromptVisible() -> Bool { authElements().contains { elementOnScreen($0) } }

    /// True when the element exists, is hittable and has a non-empty frame inside the screen.
    private static func elementOnScreen(_ element: XCUIElement) -> Bool {
        let springboard = XCUIApplication(bundleIdentifier: SheetProbe.springboard)
        var screen = springboard.frame
        if screen.isEmpty { screen = springboard.windows.firstMatch.frame }
        guard element.exists, element.isHittable else { return false }
        let frame = element.frame
        if frame.isEmpty { return false }
        return screen.isEmpty || screen.insetBy(dx: -2, dy: -2).contains(frame)
    }

    /// True when Settings shows the Manage screen (or the Age Assurance page behind it): the labels or nav bar the navigation looks for
    /// next. If these are there, the person has got past the prompt.
    private static func authenticationDone(_ settings: XCUIApplication) -> Bool {
        manageScreenMarker(settings, includeStaticText: false) != nil || strongMarkers(settings)
    }

    /// What shows after Manage is tapped: the Age Assurance and Revoke App Consent rows, a "Manage" nav bar, or the page titled
    /// "Account Settings" (a nav bar; the title as a static text only for the navigation check, not for the authentication wait, because
    /// a static text can also be seen behind a prompt). Manage has been seen to open that page with no prompt. Returns what matched.
    private static func manageScreenMarker(_ settings: XCUIApplication, includeStaticText: Bool = true) -> String? {
        if let found = anyElement(settings, labels: ["Age Assurance", "Revoke App Consent"]) { return found }
        if settings.navigationBars["Manage"].exists || settings.navigationBars["Account Settings"].exists { return "navigationBar" }
        return includeStaticText && settings.staticTexts["Account Settings"].exists ? "staticText" : nil
    }

    private static func say(_ message: String) {
        print(message)
        fflush(stdout)
        NSLog("%@", message)
    }

    /// Called when a prompt-like element exists. Waits up to 120 s in all, tapping nothing (never Cancel). The decision is
    /// ResultParser.AuthWait:
    /// - the prompt counts as up only when it is on screen (see authPromptVisible); then "ACTION NEEDED" is printed once;
    /// - if the element is there but not on screen, one line says so and the wait continues for the Manage screen;
    /// - the wait ends with "manage-screen" only after the Manage screen markers have been present with no prompt on screen for about
    ///   2.5 s of polls in a row (any sample with the prompt on screen starts the count again), or with "alert-dismissed" when the
    ///   element is gone. Throws only when a prompt that is really on screen is still up after the limit.
    private static func awaitAuthentication(_ settings: XCUIApplication) throws {
        let end = Date().addingTimeInterval(120)
        var announced = false, noted = false, wasVisible = false
        var wait = ResultParser.AuthWait()
        var last = ResultParser.AuthSample(present: true, visible: false, markers: false)
        while Date() < end {
            let present = authElementPresent()
            let visible = present && authPromptVisible()
            let markers = !visible && authenticationDone(settings)
            last = ResultParser.AuthSample(present: present, visible: visible, markers: markers)
            if visible {
                wasVisible = true
                if !announced {
                    announced = true
                    say("ACTION NEEDED: touch the Touch ID sensor on the iPhone now (120 s)")
                    trail.append(["screen": "awaiting authentication on the phone", "matchedType": "", "navBar": ""])
                }
            } else if present && !noted {
                noted = true
                say("authentication alert element present but not on screen; continuing to wait for the Manage screen")
                trail.append(["screen": "authentication alert element present but not on screen", "matchedType": "", "navBar": ""])
            }
            switch wait.feed(last) {
            case .manageScreen:
                trail.append(["screen": "authentication wait ended", "matchedType": "manage-screen", "navBar": ""])
                return
            case .alertDismissed:
                trail.append(["screen": "authentication wait ended", "matchedType": "alert-dismissed", "navBar": ""])
                return
            default: break
            }
            Thread.sleep(forTimeInterval: 0.5)
        }
        // Limit reached: one more sample decides. Only a prompt that is really on screen stops the run; an invisible element is left to
        // the next screen check.
        let finalPresent = authElementPresent()
        let finalVisible = finalPresent && authPromptVisible()
        last = ResultParser.AuthSample(present: finalPresent, visible: finalVisible, markers: false)
        if wait.timeUp(last: last) == .timedOut {
            trail.append(["screen": "authentication timed out", "matchedType": wasVisible ? "visible" : "", "navBar": ""])
            throw SandboxSettingsError.authenticationTimedOut
        }
        trail.append(["screen": "authentication wait ended", "matchedType": "limit-without-visible-alert", "navBar": ""])
    }

    /// If a Settings or SpringBoard alert is up, records its shape (title and button labels if they are known labels, counts of
    /// everything else) and throws. No alert button is ever tapped, and no field value is read.
    private static func failOnAlert(_ settings: XCUIApplication) throws {
        if authElementPresent() { try awaitAuthentication(settings) }
        for id in [bundleId, "com.apple.springboard"] {
            let app = id == bundleId ? settings : XCUIApplication(bundleIdentifier: id)
            for query in [app.alerts, app.sheets] where query.count > 0 {
                // The Touch ID prompt was dealt with above. Only an element with its identifier that is not on screen is skipped; a
                // visible one, and every other alert or sheet, is handled below (and throws).
                let found = query.allElementsBoundByIndex
                let flags = found.map { (isAuth: $0.identifier == authSheetIdentifier, visible: elementOnScreen($0)) }
                guard let index = ResultParser.alertToHandle(flags), let snap = try? found[index].snapshot() else { continue }
                if snap.label == systemAgeSheetTitle || staticTextLabels(in: snap).contains(systemAgeSheetTitle) {
                    // Apple's own age sheet: record it (known labels), tap nothing, close Settings so the sheet goes away, and let the
                    // caller stop the rows and restore. The script prints a plain line when it sees SYSTEM-AGE-SHEET.
                    let line = "SYSTEM-AGE-SHEET: Apple's age sheet appeared; leaving it unanswered"
                    print(line); fflush(stdout); NSLog("%@", line)
                    trail.append(["screen": "system age sheet", "matchedType": id == bundleId ? "settings" : "springboard", "navBar": "",
                                  "labels": "title=\(known(snap.label) ?? "other") buttons=\(buttonLabels(in: snap).compactMap(known).joined(separator: " | "))"])
                    invalidate()
                    XCUIApplication(bundleIdentifier: bundleId).terminate()
                    throw SandboxSettingsError.systemAgeSheet
                }
                let allButtons = buttonLabels(in: snap)
                let allTexts = staticTextLabels(in: snap)
                let secure = elements(of: .secureTextField, in: snap)
                let plain = elements(of: .textField, in: snap)
                struct AlertRecord: Encodable {
                    var process: String; var title: String; var knownButtons: [String]; var otherButtons: Int
                    var secureTextFields: Int; var textFields: Int
                    var knownTexts: [String]; var otherTexts: Int; var identifier: String
                }
                attachJSON(AlertRecord(process: id, title: known(snap.label) ?? "other",
                                       knownButtons: allButtons.prefix(6).compactMap(known), otherButtons: allButtons.filter { known($0) == nil }.count,
                                       secureTextFields: secure.count, textFields: plain.count,
                                       knownTexts: allTexts.prefix(6).compactMap(known), otherTexts: allTexts.filter { known($0) == nil }.count,
                                       identifier: snap.identifier == authSheetIdentifier ? snap.identifier : ""), name: "settings-alert")
                throw SandboxSettingsError.unexpectedAlert
            }
        }
    }

    /// Waits up to 8 s for a screen check to hold (it returns what matched), looking for an alert first on every pass.
    private static func expect(_ settings: XCUIApplication, _ what: String, _ check: () -> String?) throws {
        let end = Date().addingTimeInterval(8)
        repeat {
            try failOnAlert(settings)
            if let matched = check() {
                trail.append(["screen": what, "matchedType": matched, "navBar": navBarTitle(settings)])
                return
            }
            Thread.sleep(forTimeInterval: 0.4)
        } while Date() < end
        try failOnAlert(settings)
        trail.append(["screen": what, "matchedType": "none", "navBar": navBarTitle(settings)])
        throw SandboxSettingsError.verificationFailed("did not reach \(what)")
    }

    /// Taps the cell directly under the "Sandbox Apple Account" section header (the cell whose label holds an "@"). If that name is
    /// a row that opens a further screen instead, that row is tapped and the "@" cell is looked for there.
    private static func tapAccount(_ settings: XCUIApplication) throws {
        for attempt in 0..<8 {
            if let cell = accountCell(settings) { try safeTap(cell); Thread.sleep(forTimeInterval: 0.6); return }
            if let row = find(settings, ids: [], label: accountSection), row.elementType != .staticText {
                try safeTap(row)
                Thread.sleep(forTimeInterval: 0.6)
                let cell = settings.cells.matching(NSPredicate(format: "label CONTAINS '@'")).firstMatch
                if cell.waitForExistence(timeout: 8), cell.isHittable { try safeTap(cell); Thread.sleep(forTimeInterval: 0.6); return }
                throw SandboxSettingsError.elementNotFound("account cell under \(accountSection)")
            }
            if attempt < 7 { try guardedSwipe(settings, up: true) }
        }
        throw SandboxSettingsError.elementNotFound("account cell under \(accountSection)")
    }

    private static func accountCell(_ settings: XCUIApplication) -> XCUIElement? {
        let header = settings.staticTexts.matching(NSPredicate(format: "label ==[c] %@", accountSection)).firstMatch
        guard header.exists else { return nil }
        let below = header.frame.maxY - 1
        var best: XCUIElement?
        var bestY = CGFloat.greatestFiniteMagnitude
        for cell in settings.cells.matching(NSPredicate(format: "label CONTAINS '@'")).allElementsBoundByIndex.prefix(10) {
            let frame = cell.frame
            if frame.minY >= below && frame.minY < bestY && cell.isHittable { best = cell; bestY = frame.minY }
        }
        return best
    }

    /// Taps one row by identifier or exact label, scrolling the list up to ten times to find it.
    private static func tapRow(_ settings: XCUIApplication, ids: [String], label: String, scroll: Bool) throws {
        for attempt in 0..<10 {
            if let element = find(settings, ids: ids, label: label) {
                try safeTap(element)
                Thread.sleep(forTimeInterval: 0.6)
                return
            }
            if !scroll { break }
            if attempt < 9 { try guardedSwipe(settings, up: true) }
        }
        throw SandboxSettingsError.elementNotFound(label)
    }

    /// Looks for a hittable cell, button or static text; identifier first, then exact label. Typed queries only.
    private static func find(_ app: XCUIApplication, ids: [String], label: String) -> XCUIElement? {
        let queries = [app.cells, app.buttons, app.staticTexts]
        for id in ids {
            for query in queries {
                let match = query.matching(identifier: id).firstMatch
                if match.exists && match.isHittable { return match }
            }
        }
        for query in queries {
            let match = query.matching(NSPredicate(format: "label == %@", label)).firstMatch
            if match.exists && match.isHittable { return match }
        }
        return nil
    }
}

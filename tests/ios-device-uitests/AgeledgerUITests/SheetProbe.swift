import XCTest

/// One element seen inside a candidate alert or sheet: for buttons the label (short, and without anything that looks like an email);
/// for texts only a summary line, never the text itself.
struct SheetFinding: Codable {
    var process: String
    var type: String
    var label: String
    var identifier: String
}

/// What one scan or poll found. `seen` is true only when a Share or Don't Share button sits inside an alert or sheet, or (for the
/// sample app and SpringBoard) a direct button match was found. When `seen` is false only the container type and process are kept.
struct ProbeResult {
    var seen = false
    var process: String?
    /// "alert", "sheet", "popover", or "button" when only a direct button match found it.
    var container: String?
    var findings: [SheetFinding] = []
    /// True when a text in the same container (or window, for a direct match) mentions both "age" and "range". A button is only
    /// tapped when this is true.
    var ageRangeText = false
    /// Direct matches only: where the matched button is, and the region of its parent, so a sibling can be tapped later.
    var matchedFrame: CGRect?
    var parentFrame: CGRect?
}

/// Looks for Apple's age-range sheet in the sample app, in SpringBoard and in other likely processes, and taps its buttons.
/// Only the alert / sheet / popover subtree is read, and of it only button labels and a count of the texts.
enum SheetProbe {
    static let springboard = "com.apple.springboard"

    /// Sample first, then SpringBoard, then processes that may host system sheets. Only running ones are queried.
    static let candidates = [
        SampleApp.bundleId,
        springboard,
        "com.apple.ScreenTimeUI",
        "com.apple.FamilyControlsUI",
        "com.apple.AuthKitUIService",
        "com.apple.SafariViewService",
    ]

    private static let maxItemsPerContainer = 20
    private static let directLabels = ["Share", "Don't Share", "Share Age Range"]

    /// A short button label with nothing that looks like an email; nil otherwise.
    static func scrub(_ text: String) -> String? {
        let t = text.trimmingCharacters(in: .whitespacesAndNewlines)
        if t.isEmpty || t.contains("@") { return nil }
        return String(t.prefix(30))
    }

    static func normalize(_ s: String) -> String {
        s.replacingOccurrences(of: "\u{2019}", with: "'").lowercased()
    }

    /// Exactly "Share", "Don't Share" or "Share Age Range" (case and apostrophe style ignored); nothing else counts as a share button.
    static func isShareLabel(_ label: String) -> Bool {
        ["share", "don't share", "share age range"].contains(normalize(label))
    }

    /// True when a text contains the phrase "age range" on word boundaries (case ignored).
    static func isAgeRangeText(_ text: String) -> Bool {
        text.lowercased().range(of: "\\bage range\\b", options: .regularExpression) != nil
    }

    /// Walks a snapshot: collects button labels (capped), counts static texts, notes whether a Share button and an age-range text exist.
    private static func summarize(_ root: XCUIElementSnapshot, process: String) -> (findings: [SheetFinding], hasShare: Bool, ageText: Bool) {
        var findings: [SheetFinding] = []
        var texts = 0
        var hasShare = false
        var ageText = false
        func walk(_ node: XCUIElementSnapshot) {
            for child in node.children {
                if child.elementType == .button {
                    if isShareLabel(child.label) { hasShare = true }
                    if isAgeRangeText(child.label) { ageText = true } // a button such as "Share Age Range" counts as well
                    if findings.count < maxItemsPerContainer, let label = scrub(child.label) {
                        findings.append(SheetFinding(process: process, type: "button", label: label, identifier: ""))
                    }
                } else if child.elementType == .staticText {
                    texts += 1
                    if isAgeRangeText(child.label) { ageText = true }
                }
                walk(child)
            }
        }
        walk(root)
        findings.append(SheetFinding(process: process, type: "staticTextSummary", label: "texts=\(texts) ageRangeText=\(ageText)", identifier: ""))
        return (findings, hasShare, ageText)
    }

    /// Reads one snapshot per alert / sheet / popover in one process, if it is running.
    static func scan(bundleId: String) -> ProbeResult {
        let app = XCUIApplication(bundleIdentifier: bundleId)
        guard app.state == .runningForeground || app.state == .runningBackground else { return ProbeResult() }
        var result = ProbeResult()
        let containers: [(String, XCUIElementQuery)] = [("alert", app.alerts), ("sheet", app.sheets), ("popover", app.popovers)]
        for (type, query) in containers {
            guard query.count > 0, let snapshot = try? query.firstMatch.snapshot() else { continue }
            let summary = summarize(snapshot, process: bundleId)
            if summary.hasShare {
                result.findings += [SheetFinding(process: bundleId, type: type, label: "", identifier: "")] + summary.findings
                result.seen = true; result.process = bundleId; result.container = type; result.ageRangeText = summary.ageText
            } else if !result.seen {
                // Not a Share sheet: keep only that something of this type exists in this process, no labels.
                result.findings.append(SheetFinding(process: bundleId, type: type, label: "", identifier: ""))
                result.process = bundleId; result.container = type
            }
        }
        if !result.seen, let direct = scanDirect(app, bundleId: bundleId) { return direct }
        return result
    }

    /// Fallback when no alert, sheet or popover holds a Share button, for the sample app (only when in the foreground) and
    /// SpringBoard only: a direct query for a button with one of the known labels. Records the matched button and the other button
    /// labels around it (the window's in the sample app; only the matched button's parent in SpringBoard, so home-screen content is
    /// never recorded; if that parent cannot be found, only the matched button).
    private static func scanDirect(_ app: XCUIApplication, bundleId: String) -> ProbeResult? {
        let isSpringboard = bundleId == springboard
        guard isSpringboard || (bundleId == SampleApp.bundleId && app.state == .runningForeground) else { return nil }
        for variant in directLabels {
            for candidate in [variant, variant.replacingOccurrences(of: "'", with: "\u{2019}")] {
                let predicate = NSPredicate(format: "label ==[c] %@", candidate)
                let matches = app.buttons.matching(predicate)
                guard matches.count > 0, let button = try? matches.firstMatch.snapshot() else { continue }
                var result = ProbeResult(seen: true, process: bundleId, container: "button", findings: [])
                result.matchedFrame = button.frame
                result.findings.append(SheetFinding(process: bundleId, type: "button", label: scrub(button.label) ?? "", identifier: ""))
                let windows = app.windows.containing(predicate)
                if windows.count > 0, let snapshot = try? windows.firstMatch.snapshot() {
                    let root: XCUIElementSnapshot? = isSpringboard ? parentOfMatch(in: snapshot, label: candidate) : snapshot
                    if let root {
                        result.parentFrame = root.frame
                        let summary = summarize(root, process: bundleId)
                        result.findings += summary.findings
                        result.ageRangeText = summary.ageText
                    }
                }
                return result
            }
        }
        return nil
    }

    /// The snapshot node that directly contains the button with this label.
    private static func parentOfMatch(in node: XCUIElementSnapshot, label: String) -> XCUIElementSnapshot? {
        for child in node.children {
            if child.elementType == .button && child.label.caseInsensitiveCompare(label) == .orderedSame { return node }
            if let found = parentOfMatch(in: child, label: label) { return found }
        }
        return nil
    }

    /// Polls until the timeout, checking the deadline inside each pass. Returns as soon as a sheet is seen; otherwise the last
    /// container type and process that had something (no labels) come back with `seen == false`.
    static func look(timeout: TimeInterval) -> ProbeResult {
        let end = Date().addingTimeInterval(timeout)
        var last = ProbeResult()
        repeat {
            for id in candidates {
                if Date() >= end { return last }
                let result = scan(bundleId: id)
                if result.seen { return result }
                if !result.findings.isEmpty { last = result }
            }
            Thread.sleep(forTimeInterval: 0.7)
        } while Date() < end
        return last
    }

    /// Taps the button with this label inside an alert, sheet or popover of the process (case and apostrophe style ignored), but
    /// only when a text in the same container mentions both "age" and "range". For a direct match ("button") only a button inside the
    /// matched button's parent region is tapped, never the first match anywhere in the process.
    @discardableResult
    static func tapButton(label wanted: String, probe: ProbeResult) -> Bool {
        guard let process = probe.process, probe.ageRangeText else { return false }
        let app = XCUIApplication(bundleIdentifier: process)
        let variants = [wanted, wanted.replacingOccurrences(of: "'", with: "\u{2019}")]
        if probe.container == "button" {
            for variant in variants {
                let matches = app.buttons.matching(NSPredicate(format: "label ==[c] %@", variant)).allElementsBoundByIndex.prefix(10)
                for button in matches {
                    guard let snap = try? button.snapshot() else { continue }
                    let frame = snap.frame
                    let inside = probe.parentFrame.map { $0.contains(CGPoint(x: frame.midX, y: frame.midY)) } ?? (probe.matchedFrame == frame)
                    if inside { button.tap(); return true }
                }
            }
            return false
        }
        // Only inside the container that passes the gate right now: re-snapshot it and check the age-range text again.
        for query in [app.alerts, app.sheets, app.popovers] where query.count > 0 {
            guard let snap = try? query.firstMatch.snapshot(), summarize(snap, process: process).ageText else { continue }
            for variant in variants {
                let found = query.firstMatch.buttons.matching(NSPredicate(format: "label ==[c] %@", variant))
                if found.count > 0 { found.firstMatch.tap(); return true }
            }
        }
        return false
    }
}

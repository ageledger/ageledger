import Foundation

/// Parses the sample's result label. The label has one "Key: value" per line, but the accessibility label can come back with the
/// line breaks turned into spaces or commas, so the keys are found by name rather than by line. Foundation only, so it can be
/// checked without a device: tests/ios-device-uitests/check-result-parser.sh.
enum ResultParser {
    static let keys = ["Platform", "State", "Bucket", "Error", "SignificantChange", "ParentalControls", "Decision", "Logged", "Outcome", "Exception", "Elapsed ms"]

    /// What an Evaluate came to, from the result label: "no-result" (empty label: nothing shown), "not-evaluated" (invalid settings),
    /// "cancelled", "timed-out" (State Error with Error Timeout), "returned" (any other state) or "exception" (anything else).
    static func outcome(of text: String) -> String {
        if text.isEmpty { return "no-result" }
        if text.contains("Not evaluated") { return "not-evaluated" }
        let fields = parse(text)
        if fields["Outcome"] == "Cancelled" { return "cancelled" }
        if fields["State"] == "Error" && fields["Error"] == "Timeout" { return "timed-out" }
        if fields["State"] != nil { return "returned" }
        return "exception"
    }

    /// Whether the UI test may tap the sample's Cancel button: only when no system sheet was seen, the button is hittable where it is
    /// (the test may scroll for it once, before Evaluate, never after) and the sample is the foreground app. Otherwise the tap could land on something else.
    static func shouldTapCancel(sheetSeen: Bool, hittable: Bool, foreground: Bool) -> Bool {
        !sheetSeen && hittable && foreground
    }

    /// One poll of the Touch ID prompt: whether a prompt-like element exists, whether it is really on screen, and whether the screen
    /// after Manage shows its markers (only looked at when the element is not on screen).
    struct AuthSample { var present: Bool; var visible: Bool; var markers: Bool }

    enum AuthDecision { case keepWaiting, manageScreen, alertDismissed, timedOut, limitWithoutVisibleAlert }

    /// The decision of the wait for the Touch ID prompt, over a sequence of polls (about 0.5 s apart, up to a 120 s limit). A prompt
    /// that is on screen always keeps the wait going and restarts the settle count. The wait ends with `manageScreen` only after the
    /// markers have been present with no prompt on screen for `settlePolls` polls in a row (about 2.5 s), so a prompt that appears just
    /// after the markers do is not tapped over. It ends with `alertDismissed` when the element is gone. At the limit, `timedOut` means a
    /// prompt was still on screen; otherwise the wait just ends (`limitWithoutVisibleAlert`) and the next screen check decides.
    struct AuthWait {
        static let settlePolls = 5
        private(set) var streak = 0

        mutating func feed(_ sample: AuthSample) -> AuthDecision {
            if sample.visible { streak = 0; return .keepWaiting }
            if !sample.present { return .alertDismissed }
            if sample.markers {
                streak += 1
                return streak >= AuthWait.settlePolls ? .manageScreen : .keepWaiting
            }
            streak = 0
            return .keepWaiting
        }

        func timeUp(last: AuthSample) -> AuthDecision {
            last.present && last.visible ? .timedOut : .limitWithoutVisibleAlert
        }
    }

    /// Which alert or sheet in a list the generic alert check must handle (the index of the first one), or nil. Only an element with the
    /// Touch ID prompt's identifier that is not on screen is skipped; a visible one, and every other alert, is handled (and throws).
    static func alertToHandle(_ alerts: [(isAuth: Bool, visible: Bool)]) -> Int? {
        alerts.firstIndex { !($0.isAuth && !$0.visible) }
    }

    enum LaunchGuard { case proceed, stop }

    /// Whether the I18 row may start. The sample's "Evaluate at launch" must be read as off: when it is on, or could not be read, a
    /// relaunch of the sample can raise Apple's age sheet by itself, and the test would have to scroll over it to turn the switch off.
    static func launchGuard(launchOn: Bool?) -> LaunchGuard {
        launchOn == false ? .proceed : .stop
    }

    /// The elapsed milliseconds the label reports, or nil when there is none. (`parse` leaves it out because it varies.)
    static func elapsedMs(_ text: String) -> Int? {
        guard let range = text.range(of: "Elapsed ms: ") else { return nil }
        let digits = text[range.upperBound...].prefix { $0.isNumber }
        return Int(digits)
    }

    /// Returns the fields without the elapsed time (it varies). Text without any known key comes back as {"Text": text}.
    static func parse(_ text: String) -> [String: String] {
        let pattern = "(?:^|[\\s,;])(" + keys.map { NSRegularExpression.escapedPattern(for: $0) }.joined(separator: "|") + "): "
        guard let regex = try? NSRegularExpression(pattern: pattern) else { return text.isEmpty ? [:] : ["Text": text] }
        let ns = text as NSString
        let matches = regex.matches(in: text, range: NSRange(location: 0, length: ns.length))
        let trim = CharacterSet.whitespacesAndNewlines.union(CharacterSet(charactersIn: ",;"))
        var fields: [String: String] = [:]
        for (index, match) in matches.enumerated() {
            let key = ns.substring(with: match.range(at: 1))
            let start = match.range.location + match.range.length
            let end = index + 1 < matches.count ? matches[index + 1].range.location : ns.length
            if key == "Elapsed ms" { continue }
            fields[key] = ns.substring(with: NSRange(location: start, length: end - start)).trimmingCharacters(in: trim)
        }
        if fields.isEmpty && !text.isEmpty { fields["Text"] = text }
        return fields
    }
}

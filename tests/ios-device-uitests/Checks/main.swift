import Foundation

// Checks ResultParser on made-up label text, joined by line breaks, spaces, commas and semicolons. No device needed.
// Run it with tests/ios-device-uitests/check-result-parser.sh.
let lines = ["Platform: iOS", "State: Shared", "Bucket: 18+", "Error: None", "SignificantChange: None",
             "ParentalControls: SignificantAppChangeApprovalRequired", "Decision: Allowed", "Logged: True", "Elapsed ms: 795"]
let want = ["Platform": "iOS", "State": "Shared", "Bucket": "18+", "Error": "None", "SignificantChange": "None",
            "ParentalControls": "SignificantAppChangeApprovalRequired", "Decision": "Allowed", "Logged": "True"]
var failures = 0
for separator in ["\n", " ", ", ", "; ", ","] {
    let fields = ResultParser.parse(lines.joined(separator: separator))
    if fields != want { print("FAIL for separator \(separator.debugDescription): \(fields)"); failures += 1 }
}
if ResultParser.parse("") != [:] { print("FAIL: empty text"); failures += 1 }
let odd = "SomethingException\nElapsed ms: 5"
if ResultParser.parse(odd) != ["Text": odd] { print("FAIL: text without fields"); failures += 1 }
if ResultParser.parse("State: Error, Bucket: unknown,") != ["State": "Error", "Bucket": "unknown"] { print("FAIL: trailing comma"); failures += 1 }
// The cancelled and timed-out outputs of the I18 row (made-up values).
let cancelled = "Outcome: Cancelled\nException: TaskCanceledException\nElapsed ms: 5012"
for separator in ["\n", " ", ", "] {
    let fields = ResultParser.parse(cancelled.replacingOccurrences(of: "\n", with: separator))
    if fields != ["Outcome": "Cancelled", "Exception": "TaskCanceledException"] { print("FAIL cancelled for \(separator.debugDescription): \(fields)"); failures += 1 }
}
if ResultParser.elapsedMs(cancelled) != 5012 { print("FAIL: elapsed of cancelled"); failures += 1 }
let timedOut = "Platform: iOS\nState: Error\nBucket: unknown\nError: Timeout\nSignificantChange: None\nParentalControls: None\nDecision: NotApplicable\nLogged: True\nElapsed ms: 15040"
if ResultParser.parse(timedOut)["State"] != "Error" || ResultParser.parse(timedOut)["Error"] != "Timeout" { print("FAIL: timed-out fields"); failures += 1 }
if ResultParser.elapsedMs(timedOut) != 15040 { print("FAIL: elapsed of timed-out"); failures += 1 }
if ResultParser.elapsedMs("State: Shared") != nil || ResultParser.elapsedMs("") != nil { print("FAIL: elapsed without a value"); failures += 1 }
// Outcome classification (made-up labels), whatever the line separator.
let outcomeCases: [(String, String)] = [
    ("", "no-result"),
    ("Not evaluated: invalid settings (see status).", "not-evaluated"),
    (cancelled, "cancelled"),
    (timedOut, "timed-out"),
    ("Platform: iOS\nState: Shared\nBucket: 18+\nError: None\nDecision: Allowed\nLogged: True\nElapsed ms: 900", "returned"),
    ("Platform: iOS\nState: Error\nBucket: unknown\nError: Other\nDecision: NotApplicable\nLogged: True\nElapsed ms: 90", "returned"),
    ("InvalidOperationException\nElapsed ms: 12", "exception"),
]
for (text, want) in outcomeCases {
    for separator in ["\n", " "] {
        let got = ResultParser.outcome(of: text.replacingOccurrences(of: "\n", with: separator))
        if got != want { print("FAIL outcome for \(text.prefix(30).debugDescription): \(got), wanted \(want)"); failures += 1 }
    }
}
// Cancel is tapped only with no sheet seen, a hittable button and the sample in the foreground.
for sheet in [false, true] { for hit in [false, true] { for front in [false, true] {
    let want = !sheet && hit && front
    if ResultParser.shouldTapCancel(sheetSeen: sheet, hittable: hit, foreground: front) != want {
        print("FAIL shouldTapCancel(\(sheet), \(hit), \(front))"); failures += 1
    }
} } }
// The I18 row starts only when "Evaluate at launch" reads as off.
if ResultParser.launchGuard(launchOn: false) != .proceed { print("FAIL launchGuard(false)"); failures += 1 }
if ResultParser.launchGuard(launchOn: true) != .stop { print("FAIL launchGuard(true)"); failures += 1 }
if ResultParser.launchGuard(launchOn: nil) != .stop { print("FAIL launchGuard(nil)"); failures += 1 }
// The wait for the Touch ID prompt, over made-up poll sequences.
func run(_ samples: [ResultParser.AuthSample]) -> (last: ResultParser.AuthDecision, wait: ResultParser.AuthWait) {
    var wait = ResultParser.AuthWait()
    var last = ResultParser.AuthDecision.keepWaiting
    for s in samples { last = wait.feed(s); if last != .keepWaiting { break } }
    return (last, wait)
}
let ghost = ResultParser.AuthSample(present: true, visible: false, markers: false)
let ghostMarkers = ResultParser.AuthSample(present: true, visible: false, markers: true)
let shown = ResultParser.AuthSample(present: true, visible: true, markers: false)
let shownOverMarkers = ResultParser.AuthSample(present: true, visible: true, markers: true)
let gone = ResultParser.AuthSample(present: false, visible: false, markers: false)
let n = ResultParser.AuthWait.settlePolls
// present, not visible, markers, then visible: keeps waiting, and the count starts again
if run(Array(repeating: ghostMarkers, count: n - 1) + [shown]).last != .keepWaiting { print("FAIL: marker ghost then visible"); failures += 1 }
if run(Array(repeating: ghostMarkers, count: n - 1) + [shown] + Array(repeating: ghostMarkers, count: n - 1)).last != .keepWaiting { print("FAIL: count not reset by a visible prompt"); failures += 1 }
if run(Array(repeating: ghostMarkers, count: n - 1) + [shown] + Array(repeating: ghostMarkers, count: n)).last != .manageScreen { print("FAIL: settle after a reset"); failures += 1 }
// a ghost with the markers for N polls: manage-screen, but not before
if run(Array(repeating: ghostMarkers, count: n - 1)).last != .keepWaiting { print("FAIL: ended before the settle period"); failures += 1 }
if run(Array(repeating: ghostMarkers, count: n)).last != .manageScreen { print("FAIL: ghost with markers"); failures += 1 }
// markers that come and go do not add up
if run([ghostMarkers, ghost, ghostMarkers, ghost, ghostMarkers, ghost, ghostMarkers, ghost, ghostMarkers, ghost]).last != .keepWaiting { print("FAIL: markers not consecutive"); failures += 1 }
// a prompt that is on screen, even with markers behind it, keeps the wait going for the whole limit and then times out
let limitPolls = 240
let whole = run(Array(repeating: shownOverMarkers, count: limitPolls))
if whole.last != .keepWaiting || whole.wait.timeUp(last: shownOverMarkers) != .timedOut { print("FAIL: visible for the whole limit"); failures += 1 }
if ResultParser.AuthWait().timeUp(last: ghost) != .limitWithoutVisibleAlert { print("FAIL: limit with only a ghost"); failures += 1 }
// the element going away ends the wait
if run([shown, shown, gone]).last != .alertDismissed { print("FAIL: alert dismissed"); failures += 1 }
// the generic alert check: a ghost Touch ID element is skipped, anything else is handled
if ResultParser.alertToHandle([(isAuth: true, visible: false), (isAuth: false, visible: false)]) != 1 { print("FAIL: [ghost auth, other alert]"); failures += 1 }
if ResultParser.alertToHandle([(isAuth: true, visible: false)]) != nil { print("FAIL: ghost auth alone"); failures += 1 }
if ResultParser.alertToHandle([(isAuth: true, visible: true)]) != 0 { print("FAIL: visible auth reaching the alert check"); failures += 1 }
if ResultParser.alertToHandle([]) != nil { print("FAIL: no alerts"); failures += 1 }
print(failures == 0 ? "ResultParser ok" : "ResultParser FAILED")
if failures != 0 { exit(1) }

// Thin Objective-C-visible wrapper over Apple's DeclaredAgeRange framework, for the .NET binding.
// API names checked against the DeclaredAgeRange swiftinterface in the iPhoneOS 27.1 SDK (Xcode 27.1).
// Every call is guarded by OS version, so the app still runs on iOS 15+ (the framework is weak-linked).

import Foundation
import UIKit
#if !canImport(DeclaredAgeRange)
#error("This SDK has no DeclaredAgeRange framework. Build with the iOS 26.4 SDK or newer (a framework built without it would always report not available).")
#endif
import DeclaredAgeRange

/// Result codes shared with the C# side (keep in sync with AppleResponseKind / AppleErrorKind).
///  kind: 0 sharing, 1 declinedSharing, 2 error
///  errorKind: 0 none, 1 notAvailable, 2 invalidRequest, 3 invalidAccount, 4 declinedOnboarding, 5 network, 6 other
///  parentalControls bitmask: 1 communicationLimits, 2 significantAppChangeApprovalRequired
@objc(MASAgeRangeResult)
public final class MASAgeRangeResult: NSObject {
    @objc public let kind: Int
    @objc public let lowerBound: NSNumber?
    @objc public let upperBound: NSNumber?
    @objc public let parentalControls: Int
    @objc public let errorKind: Int

    init(kind: Int, lower: Int? = nil, upper: Int? = nil, parentalControls: Int = 0, errorKind: Int = 0) {
        self.kind = kind
        self.lowerBound = lower.map { NSNumber(value: $0) }
        self.upperBound = upper.map { NSNumber(value: $0) }
        self.parentalControls = parentalControls
        self.errorKind = errorKind
    }
}

@objc(MASAgeSignalsBridge)
public final class MASAgeSignalsBridge: NSObject {

    /// requestAgeRange(ageGates:_:_:in:). gate2/gate3 may be nil. Declaration (self/guardian/confirmed) is deliberately not returned.
    @objc public static func requestAgeRange(gate1: Int, gate2: NSNumber?, gate3: NSNumber?,
                                             viewController: UIViewController,
                                             completion: @escaping (MASAgeRangeResult) -> Void) {
        #if canImport(DeclaredAgeRange)
        if #available(iOS 26.0, *) {
            Task { @MainActor in
                do {
                    let response = try await AgeRangeService.shared.requestAgeRange(
                        ageGates: gate1, gate2?.intValue, gate3?.intValue, in: viewController)
                    switch response {
                    case .declinedSharing:
                        completion(MASAgeRangeResult(kind: 1))
                    case .sharing(let range):
                        var controls = 0
                        if range.activeParentalControls.contains(.communicationLimits) { controls |= 1 }
                        if #available(iOS 26.2, *) {
                            if range.activeParentalControls.contains(.significantAppChangeApprovalRequired) { controls |= 2 }
                        }
                        completion(MASAgeRangeResult(kind: 0, lower: range.lowerBound, upper: range.upperBound, parentalControls: controls))
                    @unknown default:
                        completion(MASAgeRangeResult(kind: 2, errorKind: 6))
                    }
                } catch let error as AgeRangeService.Error {
                    let code: Int
                    switch error {
                    case .notAvailable: code = 1
                    case .invalidRequest: code = 2
                    case .invalidAccount: code = 3
                    case .declinedOnboarding: code = 4
                    case .network: code = 5
                    @unknown default: code = 6
                    }
                    completion(MASAgeRangeResult(kind: 2, errorKind: code))
                } catch {
                    completion(MASAgeRangeResult(kind: 2, errorKind: 6))
                }
            }
            return
        }
        #endif
        completion(MASAgeRangeResult(kind: 2, errorKind: 1))
    }

    /// isEligibleForAgeFeatures (iOS 26.2+). Completion value: -1 unknown or unsupported, 0 false, 1 true.
    @objc public static func isEligibleForAgeFeatures(completion: @escaping (Int) -> Void) {
        #if canImport(DeclaredAgeRange)
        if #available(iOS 26.2, *) {
            Task {
                do { completion(try await AgeRangeService.shared.isEligibleForAgeFeatures ? 1 : 0) }
                catch { completion(-1) }
            }
            return
        }
        #endif
        completion(-1)
    }

    /// requiredRegulatoryFeatures (iOS 26.4+). Completion value: -1 unknown or unsupported, else a bitmask:
    /// 1 declaredAgeRangeRequired, 2 significantAppChangeRequiresAdultNotification, 4 significantAppChangeRequiresParentalConsent.
    @objc public static func requiredRegulatoryFeatures(completion: @escaping (Int) -> Void) {
        #if canImport(DeclaredAgeRange)
        if #available(iOS 26.4, *) {
            Task {
                do {
                    let features = try await AgeRangeService.shared.requiredRegulatoryFeatures
                    var mask = 0
                    if features.contains(.declaredAgeRangeRequired) { mask |= 1 }
                    if features.contains(.significantAppChangeRequiresAdultNotification) { mask |= 2 }
                    if features.contains(.significantAppChangeRequiresParentalConsent) { mask |= 4 }
                    completion(mask)
                } catch { completion(-1) }
            }
            return
        }
        #endif
        completion(-1)
    }

    /// showSignificantUpdateAcknowledgment(in:updateDescription:) (iOS 26.4+, iOS only).
    /// Completion value: 0 acknowledged, 1 unsupported, 2 failed.
    @objc public static func showSignificantUpdateAcknowledgment(windowScene: UIWindowScene, updateDescription: String,
                                                                 completion: @escaping (Int) -> Void) {
        #if canImport(DeclaredAgeRange)
        if #available(iOS 26.4, *) {
            Task { @MainActor in
                do {
                    try await AgeRangeService.shared.showSignificantUpdateAcknowledgment(in: windowScene, updateDescription: updateDescription)
                    completion(0)
                } catch { completion(2) }
            }
            return
        }
        #endif
        completion(1)
    }
}

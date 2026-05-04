import ApplicationServices
import CoreGraphics
import Foundation

enum CaptureCommand {
    case toggleRemoteInput
    case emergencyStop
}

struct CapturedInputEvent {
    let action: InputEventAction
    let hidUsage: UInt8
    let modifiers: UInt8
    let eventFlags: UInt8
    let timestampMilliseconds: UInt64
}

protocol KeyboardCaptureManagerDelegate: AnyObject {
    func keyboardCaptureManager(_ manager: KeyboardCaptureManager, didUpdateStatus status: String)
    func keyboardCaptureManager(_ manager: KeyboardCaptureManager, didLog message: String)
    func keyboardCaptureManager(_ manager: KeyboardCaptureManager, didReceiveCommand command: CaptureCommand)
    func keyboardCaptureManager(_ manager: KeyboardCaptureManager, didCapture inputEvent: CapturedInputEvent)
}

final class KeyboardCaptureManager {
    weak var delegate: KeyboardCaptureManagerDelegate?

    private let suppressLocalInput = true
    private let logFileURL = FileManager.default.homeDirectoryForCurrentUser
        .appendingPathComponent("Library/Logs/\(AppIdentity.logFileName)")
    private var shortcutConfiguration = BridgeShortcutConfiguration(toggleKey: .return, emergencyKey: .escape)

    var isRemoteInputActive = false {
        didSet {
            if oldValue != isRemoteInputActive {
                let message = isRemoteInputActive ? "Exclusive keyboard mode enabled." : "Keyboard returned to Mac."
                delegate?.keyboardCaptureManager(self, didUpdateStatus: message)
                log(message)
            }
        }
    }

    private var eventTap: CFMachPort?
    private var runLoopSource: CFRunLoopSource?
    private var currentModifierBitmap: UInt8 = 0
    private var ignoredShortcutKeyUps = Set<CGKeyCode>()
    private var suppressingShortcutModifierTransitions = false

    func start() {
        installEventTapIfNeeded(promptIfNeeded: true)
    }

    func retryStart(promptIfNeeded: Bool = true) {
        teardownEventTap()
        installEventTapIfNeeded(promptIfNeeded: promptIfNeeded)
    }

    func applyShortcutConfiguration(_ configuration: BridgeShortcutConfiguration) {
        shortcutConfiguration = configuration
        if eventTap != nil {
            delegate?.keyboardCaptureManager(self, didUpdateStatus: captureInstalledStatusText())
        }
        log("Shortcut configuration updated. QuickSwitch=\(configuration.toggleDisplayText) emergency=\(configuration.emergencyDisplayText)")
    }

    private func requestInputMonitoringIfNeeded() {
        _ = CGRequestListenEventAccess()
    }

    private func requestAccessibilityIfNeeded() {
        let options = [kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: true] as CFDictionary
        _ = AXIsProcessTrustedWithOptions(options)
    }

    private func hasInputMonitoringAccess() -> Bool {
        CGPreflightListenEventAccess()
    }

    private func hasAccessibilityAccess() -> Bool {
        AXIsProcessTrusted()
    }

    private func installEventTapIfNeeded(promptIfNeeded: Bool) {
        guard eventTap == nil else {
            delegate?.keyboardCaptureManager(self, didUpdateStatus: "Keyboard capture already running.")
            return
        }

        if promptIfNeeded {
            requestInputMonitoringIfNeeded()
            requestAccessibilityIfNeeded()
        }

        let hasListenAccess = hasInputMonitoringAccess()
        let hasAccessibility = hasAccessibilityAccess()
        log(
            "Input Monitoring preflight=\(hasListenAccess ? "allowed" : "denied"). Accessibility=\(hasAccessibility ? "allowed" : "denied"). Capture mode=\(suppressLocalInput ? "intercept" : "listen-only")."
        )

        guard hasAccessibility else {
            delegate?.keyboardCaptureManager(
                self,
                didUpdateStatus: "Keyboard capture unavailable. Turn on Accessibility, then retry."
            )
            log("Accessibility permission is missing. Exclusive keyboard mode cannot start yet.")
            return
        }

        if !hasListenAccess {
            log("Input Monitoring is not granted. Continuing with Accessibility because exclusive event taps may still work.")
        }

        let mask = (1 << CGEventType.keyDown.rawValue)
            | (1 << CGEventType.keyUp.rawValue)
            | (1 << CGEventType.flagsChanged.rawValue)

        let callback: CGEventTapCallBack = { _, type, event, userInfo in
            guard let userInfo else {
                return Unmanaged.passUnretained(event)
            }

            let manager = Unmanaged<KeyboardCaptureManager>.fromOpaque(userInfo).takeUnretainedValue()
            return manager.handleEvent(type: type, event: event)
        }

        guard let tap = CGEvent.tapCreate(
            tap: .cgSessionEventTap,
            place: .headInsertEventTap,
            options: .defaultTap,
            eventsOfInterest: CGEventMask(mask),
            callback: callback,
            userInfo: Unmanaged.passUnretained(self).toOpaque()
        ) else {
            let status = hasListenAccess
                ? "Keyboard capture unavailable. Accessibility is on, but exclusive capture still failed."
                : "Keyboard capture unavailable. Turn on Input Monitoring too, then retry."
            delegate?.keyboardCaptureManager(self, didUpdateStatus: status)
            log("Failed to create exclusive CGEvent tap. Accessibility=\(hasAccessibility) InputMonitoring=\(hasListenAccess)")
            return
        }

        let source = CFMachPortCreateRunLoopSource(kCFAllocatorDefault, tap, 0)
        CFRunLoopAddSource(CFRunLoopGetMain(), source, .commonModes)
        CGEvent.tapEnable(tap: tap, enable: true)

        eventTap = tap
        runLoopSource = source

        let modeSuffix = suppressLocalInput
            ? "local keys will be suppressed while remote mode is active."
            : "listen-only mode; local Mac will still receive the same keystrokes."
        delegate?.keyboardCaptureManager(self, didUpdateStatus: captureInstalledStatusText())
        log("Installed CGEvent tap for keyboard capture in \(suppressLocalInput ? "exclusive" : "listen-only") mode; \(modeSuffix)")
    }

    private func teardownEventTap() {
        if let source = runLoopSource {
            CFRunLoopRemoveSource(CFRunLoopGetMain(), source, .commonModes)
        }

        runLoopSource = nil
        eventTap = nil
        currentModifierBitmap = 0
        ignoredShortcutKeyUps.removeAll()
        suppressingShortcutModifierTransitions = false
    }

    private func handleEvent(type: CGEventType, event: CGEvent) -> Unmanaged<CGEvent>? {
        if type == .tapDisabledByTimeout || type == .tapDisabledByUserInput {
            if let eventTap {
                CGEvent.tapEnable(tap: eventTap, enable: true)
            }
            log("Event tap was disabled temporarily; re-enabled it.")
            return Unmanaged.passUnretained(event)
        }

        let keyCode = CGKeyCode(event.getIntegerValueField(.keyboardEventKeycode))

        if type == .keyUp, ignoredShortcutKeyUps.remove(keyCode) != nil {
            log("Swallowed shortcut key up for keyCode \(keyCode).")
            return nil
        }

        if type == .keyDown, shouldTriggerToggleCommand(for: keyCode, flags: event.flags) {
            log("Detected quick switch hotkey.")
            beginShortcutSuppression(for: keyCode)
            delegate?.keyboardCaptureManager(self, didReceiveCommand: .toggleRemoteInput)
            return nil
        }

        if type == .keyDown, shouldTriggerEmergencyStop(for: keyCode, flags: event.flags) {
            log("Detected emergency exit hotkey.")
            beginShortcutSuppression(for: keyCode)
            delegate?.keyboardCaptureManager(self, didReceiveCommand: .emergencyStop)
            return nil
        }

        switch type {
        case .flagsChanged:
            synchronizeModifierBitmap(for: keyCode, flags: event.flags)
            if suppressingShortcutModifierTransitions, isShortcutModifierKey(keyCode) {
                finishShortcutSuppressionIfNeeded(currentFlags: event.flags)
                return nil
            }
            guard isRemoteInputActive else {
                return Unmanaged.passUnretained(event)
            }

            let usage = KeyboardUsageMapper.modifierTransition(for: keyCode, flags: event.flags)?.usage ?? 0
            let capturedEvent = CapturedInputEvent(
                action: .modifiersOnly,
                hidUsage: usage,
                modifiers: currentModifierBitmap,
                eventFlags: 0,
                timestampMilliseconds: currentTimestampMilliseconds()
            )
            delegate?.keyboardCaptureManager(self, didCapture: capturedEvent)
            return passThroughEvent(event)

        case .keyDown, .keyUp:
            guard isRemoteInputActive else {
                return Unmanaged.passUnretained(event)
            }

            guard let usage = KeyboardUsageMapper.hidUsage(for: keyCode) else {
                log("No HID usage mapping for keyCode \(keyCode); remote event ignored.")
                return passThroughEvent(event)
            }

            let autoRepeat = event.getIntegerValueField(.keyboardEventAutorepeat) == 1
            let action: InputEventAction
            if type == .keyDown {
                action = autoRepeat ? .keyRepeat : .keyDown
            } else {
                action = .keyUp
            }

            let capturedEvent = CapturedInputEvent(
                action: action,
                hidUsage: usage,
                modifiers: currentModifierBitmap,
                eventFlags: autoRepeat ? 0x01 : 0x00,
                timestampMilliseconds: currentTimestampMilliseconds()
            )

            delegate?.keyboardCaptureManager(self, didCapture: capturedEvent)
            return passThroughEvent(event)

        default:
            return Unmanaged.passUnretained(event)
        }
    }

    private func synchronizeModifierBitmap(for keyCode: CGKeyCode, flags: CGEventFlags) {
        guard let transition = KeyboardUsageMapper.modifierTransition(for: keyCode, flags: flags) else {
            return
        }

        if transition.isPressed {
            currentModifierBitmap |= transition.bitMask
        } else {
            currentModifierBitmap &= ~transition.bitMask
        }
    }

    private func shouldTriggerToggleCommand(for keyCode: CGKeyCode, flags: CGEventFlags) -> Bool {
        shortcutConfiguration.isQuickSwitchEnabled
            && keyCode == shortcutConfiguration.toggleKey.keyCode
            && flags.contains(shortcutConfiguration.requiredModifiers)
    }

    private func shouldTriggerEmergencyStop(for keyCode: CGKeyCode, flags: CGEventFlags) -> Bool {
        keyCode == shortcutConfiguration.emergencyKey.keyCode && flags.contains(shortcutConfiguration.requiredModifiers)
    }

    private func currentTimestampMilliseconds() -> UInt64 {
        UInt64(Date().timeIntervalSince1970 * 1000)
    }

    private func beginShortcutSuppression(for keyCode: CGKeyCode) {
        ignoredShortcutKeyUps.insert(keyCode)
        suppressingShortcutModifierTransitions = true
    }

    private func finishShortcutSuppressionIfNeeded(currentFlags: CGEventFlags) {
        if currentFlags.intersection(shortcutConfiguration.requiredModifiers).isEmpty {
            suppressingShortcutModifierTransitions = false
        }
    }

    private func isShortcutModifierKey(_ keyCode: CGKeyCode) -> Bool {
        shortcutModifierKeyCodes.contains(keyCode)
    }

    private func passThroughEvent(_ event: CGEvent) -> Unmanaged<CGEvent>? {
        suppressLocalInput && isRemoteInputActive ? nil : Unmanaged.passUnretained(event)
    }

    private func captureInstalledStatusText() -> String {
        "Quick Switch: \(shortcutConfiguration.toggleDisplayText). Emergency: \(shortcutConfiguration.emergencyDisplayText)."
    }

    private func log(_ message: String) {
        appendLogToDisk("[Capture] \(message)")
        delegate?.keyboardCaptureManager(self, didLog: message)
        NSLog("[\(AppIdentity.logPrefix)][Capture] %@", message)
    }

    private func appendLogToDisk(_ message: String) {
        let formatter = ISO8601DateFormatter()
        let line = "[\(formatter.string(from: Date()))] \(message)\n"
        let data = Data(line.utf8)

        do {
            try FileManager.default.createDirectory(
                at: logFileURL.deletingLastPathComponent(),
                withIntermediateDirectories: true
            )

            if !FileManager.default.fileExists(atPath: logFileURL.path) {
                try Data().write(to: logFileURL)
            }

            let handle = try FileHandle(forWritingTo: logFileURL)
            try handle.seekToEnd()
            try handle.write(contentsOf: data)
            try handle.close()
        } catch {
            NSLog("[\(AppIdentity.logPrefix)][Capture] Failed to append log file: %@", error.localizedDescription)
        }
    }

    private var shortcutModifierKeyCodes: Set<CGKeyCode> {
        [55, 54, 58, 61, 59, 62]
    }
}

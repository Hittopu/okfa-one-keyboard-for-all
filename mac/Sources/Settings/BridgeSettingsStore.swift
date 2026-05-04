import CoreGraphics
import Foundation

enum ShortcutKeyOption: String, CaseIterable {
    case disabled
    case digit1
    case `return`
    case space
    case escape
    case f18
    case f19
    case f20
    case f21

    var keyCode: CGKeyCode {
        switch self {
        case .disabled:
            return .max
        case .digit1:
            return 18
        case .return:
            return 36
        case .space:
            return 49
        case .escape:
            return 53
        case .f18:
            return 79
        case .f19:
            return 80
        case .f20:
            return 90
        case .f21:
            return 120
        }
    }

    var title: String {
        switch self {
        case .disabled:
            return "Disabled"
        case .digit1:
            return "1"
        case .return:
            return "Return"
        case .space:
            return "Space"
        case .escape:
            return "Escape"
        case .f18:
            return "F18"
        case .f19:
            return "F19"
        case .f20:
            return "F20"
        case .f21:
            return "F21"
        }
    }
}

struct BridgeShortcutConfiguration {
    let toggleKey: ShortcutKeyOption
    let emergencyKey: ShortcutKeyOption

    let requiredModifiers: CGEventFlags = [.maskCommand, .maskShift]
    let modifierDisplay = "Command + Shift"

    var isQuickSwitchEnabled: Bool {
        toggleKey != .disabled
    }

    var toggleDisplayText: String {
        isQuickSwitchEnabled ? "\(modifierDisplay) + \(toggleKey.title)" : "Disabled"
    }

    var emergencyDisplayText: String {
        "\(modifierDisplay) + \(emergencyKey.title)"
    }
}

final class BridgeSettingsStore {
    private enum Keys {
        static let toggleKey = "keyboard-bridge.settings.toggle-key"
        static let emergencyKey = "keyboard-bridge.settings.emergency-key"
        static let showLogs = "keyboard-bridge.settings.show-logs"
    }

    private static let sharedSuiteName = "com.keyboardforall.okfa.shared-settings"
    private static let legacySharedSuiteName = "com.keyboardforall.keyboardbridge.shared-settings"

    private let defaults: UserDefaults
    private let legacyDefaults: UserDefaults

    init(defaults: UserDefaults? = nil, legacyDefaults: UserDefaults? = nil) {
        self.defaults = defaults ?? UserDefaults(suiteName: Self.sharedSuiteName) ?? .standard
        self.legacyDefaults = legacyDefaults
            ?? UserDefaults(suiteName: Self.legacySharedSuiteName)
            ?? .standard
        migrateLegacyValuesIfNeeded()
    }

    var shortcutConfiguration: BridgeShortcutConfiguration {
        BridgeShortcutConfiguration(
            toggleKey: loadShortcut(for: Keys.toggleKey, fallback: .digit1),
            emergencyKey: loadShortcut(for: Keys.emergencyKey, fallback: .escape)
        )
    }

    var isLogVisible: Bool {
        get {
            if defaults.object(forKey: Keys.showLogs) == nil {
                return false
            }
            return defaults.bool(forKey: Keys.showLogs)
        }
        set {
            defaults.set(newValue, forKey: Keys.showLogs)
        }
    }

    func updateToggleKey(_ key: ShortcutKeyOption) {
        defaults.set(key.rawValue, forKey: Keys.toggleKey)
    }

    func updateEmergencyKey(_ key: ShortcutKeyOption) {
        defaults.set(key.rawValue, forKey: Keys.emergencyKey)
    }

    private func loadShortcut(for key: String, fallback: ShortcutKeyOption) -> ShortcutKeyOption {
        guard let rawValue = defaults.string(forKey: key),
              let option = ShortcutKeyOption(rawValue: rawValue) else {
            return fallback
        }

        return option
    }

    private func migrateLegacyValuesIfNeeded() {
        migrateLegacyValue(for: Keys.toggleKey)
        migrateLegacyValue(for: Keys.emergencyKey)
        migrateLegacyValue(for: Keys.showLogs)
    }

    private func migrateLegacyValue(for key: String) {
        guard defaults.object(forKey: key) == nil,
              let legacyValue = legacyDefaults.object(forKey: key) else {
            return
        }

        defaults.set(legacyValue, forKey: key)
    }
}

import CoreGraphics
import Foundation

struct ModifierTransition {
    let usage: UInt8
    let bitMask: UInt8
    let isPressed: Bool
}

enum KeyboardUsageMapper {
    static func hidUsage(for keyCode: CGKeyCode) -> UInt8? {
        usageByKeyCode[keyCode]
    }

    static func modifierTransition(for keyCode: CGKeyCode, flags: CGEventFlags) -> ModifierTransition? {
        switch keyCode {
        case 59:
            return ModifierTransition(usage: 0xE0, bitMask: 1 << 0, isPressed: flags.contains(.maskControl))
        case 56:
            return ModifierTransition(usage: 0xE1, bitMask: 1 << 1, isPressed: flags.contains(.maskShift))
        case 58:
            return ModifierTransition(usage: 0xE2, bitMask: 1 << 2, isPressed: flags.contains(.maskAlternate))
        case 55:
            return ModifierTransition(usage: 0xE3, bitMask: 1 << 3, isPressed: flags.contains(.maskCommand))
        case 62:
            return ModifierTransition(usage: 0xE4, bitMask: 1 << 4, isPressed: flags.contains(.maskControl))
        case 60:
            return ModifierTransition(usage: 0xE5, bitMask: 1 << 5, isPressed: flags.contains(.maskShift))
        case 61:
            return ModifierTransition(usage: 0xE6, bitMask: 1 << 6, isPressed: flags.contains(.maskAlternate))
        case 54:
            return ModifierTransition(usage: 0xE7, bitMask: 1 << 7, isPressed: flags.contains(.maskCommand))
        default:
            return nil
        }
    }

    static let toggleHotKeyCode: CGKeyCode = 36
    static let emergencyHotKeyCode: CGKeyCode = 53
    static let requiredToggleModifiers: CGEventFlags = [.maskControl, .maskAlternate, .maskCommand]

    private static let usageByKeyCode: [CGKeyCode: UInt8] = [
        0: 0x04, 1: 0x16, 2: 0x07, 3: 0x09, 4: 0x0B, 5: 0x0A,
        6: 0x1D, 7: 0x1B, 8: 0x06, 9: 0x19, 11: 0x05, 12: 0x14,
        13: 0x1A, 14: 0x08, 15: 0x15, 16: 0x1C, 17: 0x17, 18: 0x1E,
        19: 0x1F, 20: 0x20, 21: 0x21, 22: 0x23, 23: 0x22, 24: 0x2E,
        25: 0x26, 26: 0x24, 27: 0x2D, 28: 0x25, 29: 0x27, 30: 0x30,
        31: 0x12, 32: 0x18, 33: 0x2F, 34: 0x0C, 35: 0x13, 36: 0x28,
        37: 0x0F, 38: 0x0D, 39: 0x34, 40: 0x0E, 41: 0x33, 42: 0x31,
        43: 0x36, 44: 0x38, 45: 0x11, 46: 0x10, 47: 0x37, 48: 0x2B,
        49: 0x2C, 50: 0x35, 51: 0x2A, 53: 0x29, 57: 0x39,
        96: 0x3E, 97: 0x3F, 98: 0x40, 99: 0x3C, 100: 0x41, 101: 0x42,
        103: 0x44, 109: 0x43, 111: 0x45, 118: 0x3D, 120: 0x3B, 122: 0x3A,
        114: 0x49, 115: 0x4A, 116: 0x4B, 117: 0x4C, 119: 0x4D, 121: 0x4E,
        123: 0x50, 124: 0x4F, 125: 0x51, 126: 0x52,
    ]
}

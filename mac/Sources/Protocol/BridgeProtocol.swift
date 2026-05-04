import CoreBluetooth
import Foundation

enum BridgeProtocol {
    static let version: UInt8 = 1
}

enum BridgeUUIDs {
    static let service = CBUUID(string: "9F0A1000-6C5B-4E1B-9E15-3A4A7D91C100")
    static let control = CBUUID(string: "9F0A1001-6C5B-4E1B-9E15-3A4A7D91C100")
    static let inputEvent = CBUUID(string: "9F0A1002-6C5B-4E1B-9E15-3A4A7D91C100")
    static let inputSnapshot = CBUUID(string: "9F0A1003-6C5B-4E1B-9E15-3A4A7D91C100")
}

enum ControlMessageType: UInt8 {
    case clientHello = 0x01
    case trustStatus = 0x02
    case modeChange = 0x03
    case heartbeat = 0x04
    case releaseAll = 0x05
    case error = 0x06
}

enum TrustStatusCode: UInt8 {
    case pending = 0
    case approved = 1
    case denied = 2
}

enum TrustReason: UInt8 {
    case unknownClient = 1
    case userApproved = 2
    case userDenied = 3
    case autoTrusted = 4
}

enum RemoteInputMode: UInt8 {
    case idle = 0
    case remoteInputActive = 1
}

enum ModeChangeSource: UInt8 {
    case macUser = 1
    case windowsUser = 2
    case system = 3
}

enum InputEventAction: UInt8 {
    case keyDown = 1
    case keyUp = 2
    case keyRepeat = 3
    case modifiersOnly = 4
}

enum SnapshotReason: UInt8 {
    case modeStart = 1
    case periodic = 2
    case modeStop = 3
    case resync = 4
}

struct ControlHeader {
    let version: UInt8
    let type: UInt8
    let sequence: UInt16
}

struct ClientHelloMessage {
    let header: ControlHeader
    let clientId: UInt64
    let clientVersion: UInt16
    let capabilityFlags: UInt16
}

struct TrustStatusMessage {
    let header: ControlHeader
    let status: TrustStatusCode
    let reason: UInt8
    let serverFlags: UInt16
    let serverId: UInt64
}

struct ModeChangeMessage {
    let header: ControlHeader
    let mode: RemoteInputMode
    let source: ModeChangeSource
    let flags: UInt16
}

struct KeyEventMessage {
    let sequence: UInt16
    let action: InputEventAction
    let hidUsage: UInt8
    let modifiers: UInt8
    let eventFlags: UInt8
    let deltaMilliseconds: UInt16

    func encode() -> Data {
        var data = Data()
        data.append(BridgeProtocol.version)
        data.append(0x10)
        data.appendLittleEndian(sequence)
        data.append(action.rawValue)
        data.append(hidUsage)
        data.append(modifiers)
        data.append(eventFlags)
        data.appendLittleEndian(deltaMilliseconds)
        data.appendLittleEndian(UInt16(0))
        return data
    }
}

struct KeyboardSnapshotMessage {
    let sequence: UInt16
    let reason: SnapshotReason
    let modifiers: UInt8
    let usages: [UInt8]

    func encode() -> Data {
        var data = Data()
        data.append(BridgeProtocol.version)
        data.append(0x20)
        data.appendLittleEndian(sequence)
        data.append(reason.rawValue)
        data.append(modifiers)
        data.append(UInt8(usages.count))
        data.append(0)

        let clipped = Array(usages.prefix(6))
        for usage in clipped {
            data.append(usage)
        }
        if clipped.count < 6 {
            data.append(Data(repeating: 0, count: 6 - clipped.count))
        }

        return data
    }
}

enum ControlMessageCodec {
    static func decodeClientHello(_ data: Data) -> ClientHelloMessage? {
        guard data.count >= 16 else {
            return nil
        }

        let version = data[0]
        let type = data[1]
        guard version == BridgeProtocol.version, type == ControlMessageType.clientHello.rawValue else {
            return nil
        }

        let header = ControlHeader(
            version: version,
            type: type,
            sequence: data.readUInt16LE(at: 2)
        )

        return ClientHelloMessage(
            header: header,
            clientId: data.readUInt64LE(at: 4),
            clientVersion: data.readUInt16LE(at: 12),
            capabilityFlags: data.readUInt16LE(at: 14)
        )
    }

    static func encodeTrustStatus(
        sequence: UInt16,
        status: TrustStatusCode,
        reason: TrustReason,
        serverFlags: UInt16,
        serverId: UInt64
    ) -> Data {
        var data = Data()
        data.append(BridgeProtocol.version)
        data.append(ControlMessageType.trustStatus.rawValue)
        data.appendLittleEndian(sequence)
        data.append(status.rawValue)
        data.append(reason.rawValue)
        data.appendLittleEndian(serverFlags)
        data.appendLittleEndian(serverId)
        return data
    }

    static func encodeModeChange(sequence: UInt16, mode: RemoteInputMode, source: ModeChangeSource, flags: UInt16) -> Data {
        var data = Data()
        data.append(BridgeProtocol.version)
        data.append(ControlMessageType.modeChange.rawValue)
        data.appendLittleEndian(sequence)
        data.append(mode.rawValue)
        data.append(source.rawValue)
        data.appendLittleEndian(flags)
        return data
    }

    static func encodeReleaseAll(sequence: UInt16) -> Data {
        var data = Data()
        data.append(BridgeProtocol.version)
        data.append(ControlMessageType.releaseAll.rawValue)
        data.appendLittleEndian(sequence)
        return data
    }
}

extension Data {
    mutating func appendLittleEndian(_ value: UInt16) {
        var littleEndianValue = value.littleEndian
        Swift.withUnsafeBytes(of: &littleEndianValue) { buffer in
            append(buffer.bindMemory(to: UInt8.self))
        }
    }

    mutating func appendLittleEndian(_ value: UInt64) {
        var littleEndianValue = value.littleEndian
        Swift.withUnsafeBytes(of: &littleEndianValue) { buffer in
            append(buffer.bindMemory(to: UInt8.self))
        }
    }

    func readUInt16LE(at offset: Int) -> UInt16 {
        let end = offset + MemoryLayout<UInt16>.size
        let range = offset..<end
        return subdata(in: range).withUnsafeBytes { $0.load(as: UInt16.self) }.littleEndian
    }

    func readUInt64LE(at offset: Int) -> UInt64 {
        let end = offset + MemoryLayout<UInt64>.size
        let range = offset..<end
        return subdata(in: range).withUnsafeBytes { $0.load(as: UInt64.self) }.littleEndian
    }

    var hexString: String {
        map { String(format: "%02X", $0) }.joined(separator: " ")
    }
}

extension UInt64 {
    var hexClientId: String {
        String(format: "%016llX", self)
    }
}

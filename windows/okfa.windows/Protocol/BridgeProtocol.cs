using System.Buffers.Binary;

namespace KeyboardBridge.Windows.Protocol;

public static class BridgeProtocol
{
    public const byte Version = 1;
}

public static class BridgeUuids
{
    public static readonly Guid Service = Guid.Parse("9F0A1000-6C5B-4E1B-9E15-3A4A7D91C100");
    public static readonly Guid Control = Guid.Parse("9F0A1001-6C5B-4E1B-9E15-3A4A7D91C100");
    public static readonly Guid InputEvent = Guid.Parse("9F0A1002-6C5B-4E1B-9E15-3A4A7D91C100");
    public static readonly Guid InputSnapshot = Guid.Parse("9F0A1003-6C5B-4E1B-9E15-3A4A7D91C100");
}

public enum ControlMessageType : byte
{
    ClientHello = 0x01,
    TrustStatus = 0x02,
    ModeChange = 0x03,
    Heartbeat = 0x04,
    ReleaseAll = 0x05,
    Error = 0x06,
}

public enum TrustStatusCode : byte
{
    Pending = 0,
    Approved = 1,
    Denied = 2,
}

public enum TrustReason : byte
{
    UnknownClient = 1,
    UserApproved = 2,
    UserDenied = 3,
    AutoTrusted = 4,
}

public enum RemoteInputMode : byte
{
    Idle = 0,
    RemoteInputActive = 1,
}

public enum ModeChangeSource : byte
{
    MacUser = 1,
    WindowsUser = 2,
    System = 3,
}

public enum InputEventAction : byte
{
    KeyDown = 1,
    KeyUp = 2,
    KeyRepeat = 3,
    ModifiersOnly = 4,
}

public enum SnapshotReason : byte
{
    ModeStart = 1,
    Periodic = 2,
    ModeStop = 3,
    Resync = 4,
}

public readonly record struct ClientHelloMessage(ushort Sequence, ulong ClientId, ushort ClientVersion, ushort CapabilityFlags);
public readonly record struct TrustStatusMessage(ushort Sequence, TrustStatusCode Status, byte Reason, ushort ServerFlags, ulong ServerId);
public readonly record struct ModeChangeMessage(ushort Sequence, RemoteInputMode Mode, ModeChangeSource Source, ushort Flags);
public readonly record struct ReleaseAllMessage(ushort Sequence);
public readonly record struct InputEventMessage(ushort Sequence, InputEventAction Action, byte HidUsage, byte Modifiers, byte EventFlags, ushort DeltaMilliseconds);
public readonly record struct KeyboardSnapshotMessage(ushort Sequence, SnapshotReason Reason, byte Modifiers, byte PressedCount, IReadOnlyList<byte> Usages);

public static class ControlMessageCodec
{
    public static byte[] EncodeClientHello(ushort sequence, ulong clientId, ushort clientVersion, ushort capabilityFlags)
    {
        var buffer = new byte[16];
        buffer[0] = BridgeProtocol.Version;
        buffer[1] = (byte)ControlMessageType.ClientHello;
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(2, 2), sequence);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(4, 8), clientId);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(12, 2), clientVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(14, 2), capabilityFlags);
        return buffer;
    }

    public static byte[] EncodeTrustStatus(ushort sequence, TrustStatusCode status, byte reason, ushort serverFlags, ulong serverId)
    {
        var buffer = new byte[16];
        buffer[0] = BridgeProtocol.Version;
        buffer[1] = (byte)ControlMessageType.TrustStatus;
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(2, 2), sequence);
        buffer[4] = (byte)status;
        buffer[5] = reason;
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(6, 2), serverFlags);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(8, 8), serverId);
        return buffer;
    }

    public static byte[] EncodeModeChange(ushort sequence, RemoteInputMode mode, ModeChangeSource source, ushort flags)
    {
        var buffer = new byte[8];
        buffer[0] = BridgeProtocol.Version;
        buffer[1] = (byte)ControlMessageType.ModeChange;
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(2, 2), sequence);
        buffer[4] = (byte)mode;
        buffer[5] = (byte)source;
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(6, 2), flags);
        return buffer;
    }

    public static byte[] EncodeReleaseAll(ushort sequence)
    {
        var buffer = new byte[4];
        buffer[0] = BridgeProtocol.Version;
        buffer[1] = (byte)ControlMessageType.ReleaseAll;
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(2, 2), sequence);
        return buffer;
    }

    public static bool TryDecodeClientHello(byte[] payload, out ClientHelloMessage message)
    {
        message = default;

        if (payload.Length < 16)
        {
            return false;
        }

        if (payload[0] != BridgeProtocol.Version || payload[1] != (byte)ControlMessageType.ClientHello)
        {
            return false;
        }

        message = new ClientHelloMessage(
            Sequence: BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(2, 2)),
            ClientId: BinaryPrimitives.ReadUInt64LittleEndian(payload.AsSpan(4, 8)),
            ClientVersion: BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(12, 2)),
            CapabilityFlags: BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(14, 2))
        );

        return true;
    }

    public static bool TryDecodeTrustStatus(byte[] payload, out TrustStatusMessage message)
    {
        message = default;

        if (payload.Length < 16)
        {
            return false;
        }

        if (payload[0] != BridgeProtocol.Version || payload[1] != (byte)ControlMessageType.TrustStatus)
        {
            return false;
        }

        message = new TrustStatusMessage(
            Sequence: BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(2, 2)),
            Status: (TrustStatusCode)payload[4],
            Reason: payload[5],
            ServerFlags: BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(6, 2)),
            ServerId: BinaryPrimitives.ReadUInt64LittleEndian(payload.AsSpan(8, 8))
        );

        return true;
    }

    public static bool TryDecodeModeChange(byte[] payload, out ModeChangeMessage message)
    {
        message = default;

        if (payload.Length < 8)
        {
            return false;
        }

        if (payload[0] != BridgeProtocol.Version || payload[1] != (byte)ControlMessageType.ModeChange)
        {
            return false;
        }

        message = new ModeChangeMessage(
            Sequence: BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(2, 2)),
            Mode: (RemoteInputMode)payload[4],
            Source: (ModeChangeSource)payload[5],
            Flags: BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(6, 2))
        );

        return true;
    }

    public static bool TryDecodeReleaseAll(byte[] payload, out ReleaseAllMessage message)
    {
        message = default;

        if (payload.Length < 4)
        {
            return false;
        }

        if (payload[0] != BridgeProtocol.Version || payload[1] != (byte)ControlMessageType.ReleaseAll)
        {
            return false;
        }

        message = new ReleaseAllMessage(
            Sequence: BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(2, 2))
        );

        return true;
    }
}

public static class InputMessageCodec
{
    public static byte[] EncodeInputEvent(InputEventMessage message)
    {
        var buffer = new byte[12];
        buffer[0] = BridgeProtocol.Version;
        buffer[1] = 0x10;
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(2, 2), message.Sequence);
        buffer[4] = (byte)message.Action;
        buffer[5] = message.HidUsage;
        buffer[6] = message.Modifiers;
        buffer[7] = message.EventFlags;
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(8, 2), message.DeltaMilliseconds);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(10, 2), 0);
        return buffer;
    }

    public static byte[] EncodeSnapshot(KeyboardSnapshotMessage message)
    {
        var buffer = new byte[14];
        buffer[0] = BridgeProtocol.Version;
        buffer[1] = 0x20;
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(2, 2), message.Sequence);
        buffer[4] = (byte)message.Reason;
        buffer[5] = message.Modifiers;

        var clippedUsages = message.Usages.Take(6).ToArray();
        buffer[6] = (byte)Math.Min(message.PressedCount, clippedUsages.Length);
        buffer[7] = 0;
        for (var index = 0; index < clippedUsages.Length; index += 1)
        {
            buffer[8 + index] = clippedUsages[index];
        }

        return buffer;
    }

    public static bool TryDecodeInputEvent(byte[] payload, out InputEventMessage message)
    {
        message = default;

        if (payload.Length < 12)
        {
            return false;
        }

        if (payload[0] != BridgeProtocol.Version || payload[1] != 0x10)
        {
            return false;
        }

        message = new InputEventMessage(
            Sequence: BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(2, 2)),
            Action: (InputEventAction)payload[4],
            HidUsage: payload[5],
            Modifiers: payload[6],
            EventFlags: payload[7],
            DeltaMilliseconds: BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(8, 2))
        );

        return true;
    }

    public static bool TryDecodeSnapshot(byte[] payload, out KeyboardSnapshotMessage message)
    {
        message = default;

        if (payload.Length < 14)
        {
            return false;
        }

        if (payload[0] != BridgeProtocol.Version || payload[1] != 0x20)
        {
            return false;
        }

        var pressedCount = payload[6];
        var rawUsages = payload.Skip(8).Take(6).Where(static usage => usage != 0).ToArray();
        var clippedCount = (byte)Math.Min(pressedCount, rawUsages.Length);
        var usages = rawUsages.Take(clippedCount).ToArray();

        message = new KeyboardSnapshotMessage(
            Sequence: BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(2, 2)),
            Reason: (SnapshotReason)payload[4],
            Modifiers: payload[5],
            PressedCount: clippedCount,
            Usages: usages
        );

        return true;
    }
}

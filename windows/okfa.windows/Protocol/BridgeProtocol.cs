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
}

public static class InputMessageCodec
{
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

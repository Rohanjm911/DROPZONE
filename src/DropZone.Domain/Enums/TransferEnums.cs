namespace DropZone.Domain.Enums;

public enum TransferState
{
    Pending = 0,
    Connecting = 1,
    Transferring = 2,
    Paused = 3,
    Completed = 4,
    Failed = 5,
    Cancelled = 6,
    Quarantined = 7
}

public enum TransferDirection
{
    Inbound = 0,
    Outbound = 1
}

public enum ConnectionMode
{
    DirectLan = 0,
    StunP2P = 1,
    TurnRelay = 2
}

public enum DeviceTrustLevel
{
    Prompt = 0,
    Trusted = 1,
    Blocked = 2
}


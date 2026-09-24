namespace GeniaProxy.ControlPlane;

public sealed record JournalEvent(
    DateTimeOffset TimestampUtc,
    Guid SessionId,
    long Sequence,
    ConnectionEventKind Kind,
    ConnectionState State,
    ConnectionState? PreviousState,
    string? Reason,
    IReadOnlyDictionary<string, string?>? Data);

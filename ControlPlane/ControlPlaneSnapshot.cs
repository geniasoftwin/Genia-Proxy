namespace GeniaProxy.ControlPlane;

public sealed record ControlPlaneSnapshot(
    Guid SessionId,
    ConnectionState State,
    DateTimeOffset StateSinceUtc,
    string? ProfileName,
    string? Core,
    string? Mode,
    string? ExpectedExit,
    string? VerifiedExit,
    DateTimeOffset? VerifiedAtUtc,
    bool? VerificationSucceeded,
    string? VerificationSource,
    bool VerificationFresh,
    string? LastReason)
{
    public static ControlPlaneSnapshot Empty { get; } = new(
        Guid.Empty,
        ConnectionState.Idle,
        DateTimeOffset.MinValue,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        false,
        null
    );

    public static ControlPlaneSnapshot From(
        ConnectionStateMachine machine,
        TimeSpan verificationTtl) =>
        new(
            machine.Session.SessionId,
            machine.State,
            machine.StateSinceUtc,
            machine.Session.ProfileName,
            machine.Session.Core,
            machine.Session.Mode,
            machine.Session.ExpectedExit,
            machine.Session.VerifiedExit,
            machine.Session.VerifiedAtUtc,
            machine.Session.VerificationSucceeded,
            machine.Session.VerificationSource,
            machine.IsVerificationFresh(verificationTtl),
            machine.Session.LastReason
        );
}

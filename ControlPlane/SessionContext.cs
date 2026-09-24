namespace GeniaProxy.ControlPlane;

public sealed record SessionContext(
    Guid SessionId,
    DateTimeOffset StartedAtUtc,
    string? ProfileName,
    string? Core,
    string? Mode,
    string? ExpectedExit,
    string? VerifiedExit,
    DateTimeOffset? VerifiedAtUtc,
    bool? VerificationSucceeded,
    string? VerificationSource,
    string? LastReason)
{
    public static SessionContext Start(
        string? profileName,
        string? core,
        string? mode) =>
        new(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            profileName,
            core,
            mode,
            ExpectedExit: null,
            VerifiedExit: null,
            VerifiedAtUtc: null,
            VerificationSucceeded: null,
            VerificationSource: null,
            LastReason: null
        );
}

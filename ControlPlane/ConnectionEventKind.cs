namespace GeniaProxy.ControlPlane;

public enum ConnectionEventKind
{
    SessionStarted,
    StateTransition,
    LocalReady,
    TunWarmupStarted,
    VerificationStarted,
    VerificationSucceeded,
    VerificationRefreshed,
    VerificationFailed,
    SwitchStarted,
    DisconnectStarted,
    RecoveryStarted,
    RecoveryCompleted,
    SessionStopped,
    Warning,
    Error
}

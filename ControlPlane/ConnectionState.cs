namespace GeniaProxy.ControlPlane;

public enum ConnectionState
{
    Idle = 0,
    Connecting = 1,
    LocalReady = 2,
    TunWarmup = 3,
    Verified = 4,
    Switching = 5,
    Disconnecting = 6,
    Recovering = 7,
}

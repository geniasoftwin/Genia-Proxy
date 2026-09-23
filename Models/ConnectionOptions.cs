namespace GeniaProxy.Models
{
    public enum ProxyCoreKind
    {
        SingBox,
        Xray
    }

    public enum CorePreference
    {
        Automatic,
        SingBox,
        Xray
    }

    public enum ConnectionMode
    {
        LocalProxy,
        SystemProxy,
        Tun
    }

    public enum TunStackPreference
    {
        Mixed,
        System,
        GVisor
    }
}

namespace GeniaProxy.Models
{
    public sealed class TunNetworkSnapshot
    {
        public int FormatVersion { get; set; }

        public string SessionId { get; set; } = string.Empty;

        public DateTime CreatedUtc { get; set; }

        public int PhysicalInterfaceIndex { get; set; }

        public string PhysicalInterfaceId { get; set; } = string.Empty;

        public string PhysicalInterfaceAlias { get; set; } = string.Empty;

        public string PhysicalIpv4 { get; set; } = string.Empty;

        public string PhysicalGateway { get; set; } = string.Empty;

        public List<string> OriginalDnsServers { get; set; } = [];

        public bool OriginalDnsWasAutomatic { get; set; }

        public List<string> AppliedDnsServers { get; set; } = [];

        public int TunInterfaceIndex { get; set; }

        public string TunInterfaceId { get; set; } = string.Empty;

        public string TunInterfaceAlias { get; set; } = string.Empty;

        public string TunIpv4 { get; set; } = string.Empty;

        public List<string> ProxyEndpointIps { get; set; } = [];

        public List<TunAppliedRoute> AppliedRoutes { get; set; } = [];
    }

    public sealed class TunAppliedRoute
    {
        public string DestinationPrefix { get; set; } = string.Empty;

        public int InterfaceIndex { get; set; }

        public string NextHop { get; set; } = string.Empty;
    }
}

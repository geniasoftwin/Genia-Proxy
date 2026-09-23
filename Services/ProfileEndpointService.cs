using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;

namespace GeniaProxy.Services
{
    public sealed record ProfileEndpoint(
        string Host,
        int? Port
    );

    public sealed record ResolvedProfileEndpoint(
        ProfileEndpoint Endpoint,
        string Address
    );

    public static class ProfileEndpointService
    {
        private static readonly HashSet<string> NonProxySingBoxTypes =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "direct",
                "block",
                "dns",
                "selector",
                "urltest"
            };

        private static readonly HashSet<string> NonProxyXrayProtocols =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "freedom",
                "blackhole",
                "dns"
            };

        public static ProfileEndpoint? Inspect(string source)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(source);

            JsonObject root = JsonNode.Parse(source)?.AsObject()
                ?? throw new InvalidDataException(
                    "Корневой элемент профиля должен быть объектом."
                );

            if (root["outbounds"] is not JsonArray outbounds)
            {
                return null;
            }

            foreach (JsonNode? node in outbounds)
            {
                if (node is not JsonObject outbound)
                {
                    continue;
                }

                ProfileEndpoint? endpoint = TryInspectSingBox(outbound) ??
                    TryInspectXray(outbound);

                if (endpoint is not null)
                {
                    return endpoint;
                }
            }

            return null;
        }

        public static async Task<ResolvedProfileEndpoint?> ResolveAsync(
            ProfileEndpoint endpoint,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(endpoint);

            string host = NormalizeHost(endpoint.Host);

            if (IPAddress.TryParse(host, out IPAddress? literal))
            {
                return new ResolvedProfileEndpoint(
                    endpoint,
                    literal.ToString()
                );
            }

            IPAddress[] addresses = await Dns
                .GetHostAddressesAsync(host, cancellationToken);

            IPAddress? selected = addresses.FirstOrDefault(address =>
                address.AddressFamily == AddressFamily.InterNetwork
            ) ?? addresses.FirstOrDefault(address =>
                address.AddressFamily == AddressFamily.InterNetworkV6
            );

            return selected is null
                ? null
                : new ResolvedProfileEndpoint(
                    endpoint,
                    selected.ToString()
                );
        }

        private static ProfileEndpoint? TryInspectSingBox(
            JsonObject outbound)
        {
            string? type = TryGetString(outbound["type"]);

            if (string.IsNullOrWhiteSpace(type) ||
                NonProxySingBoxTypes.Contains(type))
            {
                return null;
            }

            string? host = TryGetString(outbound["server"]);

            if (string.IsNullOrWhiteSpace(host))
            {
                return null;
            }

            return new ProfileEndpoint(
                NormalizeHost(host),
                TryGetInteger(outbound["server_port"])
            );
        }

        private static ProfileEndpoint? TryInspectXray(
            JsonObject outbound)
        {
            string? protocol = TryGetString(outbound["protocol"]);

            if (string.IsNullOrWhiteSpace(protocol) ||
                NonProxyXrayProtocols.Contains(protocol) ||
                outbound["settings"] is not JsonObject settings)
            {
                return null;
            }

            ProfileEndpoint? endpoint = TryInspectXrayArray(
                settings["vnext"] as JsonArray
            );

            return endpoint ?? TryInspectXrayArray(
                settings["servers"] as JsonArray
            );
        }

        private static ProfileEndpoint? TryInspectXrayArray(
            JsonArray? servers)
        {
            if (servers is null)
            {
                return null;
            }

            foreach (JsonNode? node in servers)
            {
                if (node is not JsonObject server)
                {
                    continue;
                }

                string? host = TryGetString(server["address"]);

                if (string.IsNullOrWhiteSpace(host))
                {
                    continue;
                }

                return new ProfileEndpoint(
                    NormalizeHost(host),
                    TryGetInteger(server["port"])
                );
            }

            return null;
        }

        private static string NormalizeHost(string host)
        {
            string normalized = host.Trim();

            if (normalized.Length > 2 &&
                normalized[0] == '[' &&
                normalized[^1] == ']')
            {
                return normalized[1..^1];
            }

            return normalized;
        }

        private static string? TryGetString(JsonNode? node)
        {
            return node is JsonValue value &&
                   value.TryGetValue(out string? text) &&
                   !string.IsNullOrWhiteSpace(text)
                ? text.Trim()
                : null;
        }

        private static int? TryGetInteger(JsonNode? node)
        {
            if (node is not JsonValue value)
            {
                return null;
            }

            if (value.TryGetValue(out int integer))
            {
                return integer is >= 1 and <= 65535
                    ? integer
                    : null;
            }

            if (value.TryGetValue(out long longInteger) &&
                longInteger is >= 1 and <= 65535)
            {
                return (int)longInteger;
            }

            return null;
        }
    }
}

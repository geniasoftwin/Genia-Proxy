using System.Text.Json.Nodes;

namespace GeniaProxy.Services
{
    public static class ProtocolLabConfigSafetyService
    {
        public static void ValidateLocalProxyIsolation(
            string capabilityId,
            string json)
        {
            FeatureCapability capability =
                ProtocolLabFeatureCatalog.RequireSelectable(capabilityId);

            if (capability.EngineFamily !=
                ProtocolLabEngineFamily.SingBox)
            {
                throw new NotSupportedException(
                    "Protocol Lab local-proxy isolation currently " +
                    "supports only sing-box capabilities."
                );
            }

            JsonObject root =
                JsonNode.Parse(json)?.AsObject()
                ?? throw new InvalidDataException(
                    "Protocol Lab config must be a JSON object."
                );

            if (root.ContainsKey("experimental") ||
                root.ContainsKey("services") ||
                root.ContainsKey("endpoints"))
            {
                throw new InvalidDataException(
                    "Protocol Lab local-proxy config contains " +
                    "a forbidden top-level section."
                );
            }

            JsonArray inbounds =
                root["inbounds"]?.AsArray()
                ?? throw new InvalidDataException(
                    "Protocol Lab config has no inbounds."
                );

            if (inbounds.Count != 1 ||
                inbounds[0] is not JsonObject inbound)
            {
                throw new InvalidDataException(
                    "Protocol Lab local-proxy config must have " +
                    "exactly one inbound."
                );
            }

            string? type =
                inbound["type"]?.GetValue<string>();

            string? listen =
                inbound["listen"]?.GetValue<string>();

            bool? setSystemProxy =
                inbound["set_system_proxy"]?.GetValue<bool>();

            if (!string.Equals(
                    type,
                    "mixed",
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    listen,
                    "127.0.0.1",
                    StringComparison.Ordinal) ||
                setSystemProxy != false)
            {
                throw new InvalidDataException(
                    "Protocol Lab runtime config is not isolated " +
                    "to the loopback mixed inbound."
                );
            }

            if (root["route"] is JsonObject route &&
                (route.ContainsKey("auto_detect_interface") ||
                 route.ContainsKey("auto_route")))
            {
                throw new InvalidDataException(
                    "Protocol Lab local-proxy config may not " +
                    "request system route management."
                );
            }
        }
    }
}

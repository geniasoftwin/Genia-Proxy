using System.Text.Json.Nodes;
using GeniaProxy.Models;

namespace GeniaProxy.Services
{
    public static class ProfileInspectionService
    {
        public static ProfileInspection Inspect(string source)
        {
            ProfileDescriptor descriptor =
                ProfileFormatService.Inspect(source);

            string normalized = descriptor.Core == ProxyCoreKind.Xray
                ? XrayProfileImportService.NormalizeConfig(
                    source,
                    2080,
                    ConnectionMode.LocalProxy
                )
                : JsonProfileImportService.NormalizeConfig(
                    source,
                    2080,
                    ConnectionMode.LocalProxy
                );

            JsonObject original = JsonNode.Parse(source)?.AsObject()
                ?? throw new InvalidDataException(
                    "Корневой элемент профиля должен быть объектом."
                );

            JsonObject safe = JsonNode.Parse(normalized)?.AsObject()
                ?? throw new InvalidDataException(
                    "Не удалось сформировать безопасный профиль."
                );

            string[] protocols = descriptor.Protocols;
            int originalInboundCount =
                (original["inbounds"] as JsonArray)?.Count ?? 0;

            var changes = new List<string>();

            if (originalInboundCount != 1 ||
                !HasSafeLocalInbound(original, descriptor.Core))
            {
                changes.Add(
                    "inbounds будут заменены безопасным локальным прокси"
                );
            }

            if (original.ContainsKey("experimental"))
            {
                changes.Add("раздел experimental будет удалён");
            }

            if (original.ContainsKey("services"))
            {
                changes.Add("раздел services будет удалён");
            }

            bool insecure = descriptor.Core == ProxyCoreKind.Xray
                ? XrayProfileImportService.ContainsInsecureTls(source)
                : JsonProfileImportService.ContainsInsecureTls(source);

            if (insecure)
            {
                changes.Add(
                    "обнаружено insecure=true — требуется подтверждение"
                );
            }

            return new ProfileInspection(
                OutboundCount:
                    (safe["outbounds"] as JsonArray)?.Count ?? 0,
                EndpointCount:
                    (safe["endpoints"] as JsonArray)?.Count ?? 0,
                Protocols: protocols,
                ContainsInsecureTls: insecure,
                Changes: changes.ToArray(),
                Core: descriptor.Core,
                Transports: descriptor.Transports,
                Security: descriptor.Security
            );
        }

        public static string FormatSummary(
            string profileName,
            ProfileInspection inspection)
        {
            string protocols = inspection.Protocols.Length == 0
                ? "—"
                : string.Join(", ", inspection.Protocols);

            string changes = inspection.Changes.Length == 0
                ? "нет"
                : string.Join(
                    Environment.NewLine + "• ",
                    inspection.Changes
                );

            return string.Join(
                Environment.NewLine,
                $"Профиль: {profileName}",
                $"Ядро: {ProfileFormatService.FormatCore(inspection.Core)}",
                $"Протоколы: {protocols}",
                $"Транспорт: " +
                    (inspection.Transports.Length == 0
                        ? "—"
                        : string.Join(", ", inspection.Transports)),
                $"Защита: " +
                    (inspection.Security.Length == 0
                        ? "—"
                        : string.Join(", ", inspection.Security)),
                $"Outbounds: {inspection.OutboundCount}",
                $"Endpoints: {inspection.EndpointCount}",
                $"Небезопасный TLS: " +
                    (inspection.ContainsInsecureTls ? "да" : "нет"),
                string.Empty,
                "Изменения безопасного режима:",
                "• " + changes
            );
        }

        private static string[] GetProtocols(JsonObject root)
        {
            var protocols = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase
            );

            AddTypes(root["outbounds"] as JsonArray, protocols);
            AddTypes(root["endpoints"] as JsonArray, protocols);

            return protocols
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static void AddTypes(
            JsonArray? items,
            HashSet<string> protocols)
        {
            if (items is null)
            {
                return;
            }

            foreach (JsonNode? node in items)
            {
                string? type = TryGetString(node?["type"]);

                if (!string.IsNullOrWhiteSpace(type))
                {
                    protocols.Add(type);
                }
            }
        }

        private static bool HasSafeLocalInbound(
            JsonObject root,
            ProxyCoreKind core)
        {
            if (root["inbounds"] is not JsonArray inbounds ||
                inbounds.Count != 1 ||
                inbounds[0] is not JsonObject inbound)
            {
                return false;
            }

            string expectedProtocol = core == ProxyCoreKind.Xray
                ? "socks"
                : "mixed";

            string? actualProtocol = core == ProxyCoreKind.Xray
                ? TryGetString(inbound["protocol"])
                : TryGetString(inbound["type"]);

            return string.Equals(
                       actualProtocol,
                       expectedProtocol,
                       StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(
                       TryGetString(inbound["listen"]),
                       "127.0.0.1",
                       StringComparison.OrdinalIgnoreCase);
        }

        private static string? TryGetString(JsonNode? node)
        {
            return node is JsonValue value &&
                   value.TryGetValue(out string? text)
                ? text
                : null;
        }
    }

    public sealed record ProfileInspection(
        int OutboundCount,
        int EndpointCount,
        string[] Protocols,
        bool ContainsInsecureTls,
        string[] Changes,
        ProxyCoreKind Core,
        string[] Transports,
        string[] Security
    );
}

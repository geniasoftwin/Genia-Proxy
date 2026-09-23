using GeniaProxy.Models;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GeniaProxy.Services
{
    public static class ProfileFormatService
    {
        public static ProfileDescriptor Inspect(string source)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(source);

            JsonObject root;

            try
            {
                root = JsonNode.Parse(source)?.AsObject()
                    ?? throw new InvalidDataException(
                        "Корневой элемент профиля должен быть объектом."
                    );
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException(
                    "Профиль содержит некорректный JSON.",
                    ex
                );
            }

            JsonArray outbounds = root["outbounds"] as JsonArray
                ?? new JsonArray();

            JsonArray endpoints = root["endpoints"] as JsonArray
                ?? new JsonArray();

            bool hasSingBox = outbounds.Any(node =>
                TryGetString(node?["type"]) is not null) ||
                endpoints.Any(node =>
                    TryGetString(node?["type"]) is not null);

            bool hasXray = outbounds.Any(node =>
                TryGetString(node?["protocol"]) is not null);

            if (hasSingBox == hasXray)
            {
                throw new InvalidDataException(
                    hasSingBox
                        ? "Профиль смешивает форматы sing-box и Xray."
                        : "Не удалось определить формат ядра профиля."
                );
            }

            ProxyCoreKind core = hasXray
                ? ProxyCoreKind.Xray
                : ProxyCoreKind.SingBox;

            var protocols = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase
            );
            var transports = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase
            );
            var security = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase
            );

            foreach (JsonNode? node in outbounds)
            {
                string? protocol = core == ProxyCoreKind.Xray
                    ? TryGetString(node?["protocol"])
                    : TryGetString(node?["type"]);

                if (!string.IsNullOrWhiteSpace(protocol))
                {
                    protocols.Add(protocol);
                }

                if (core == ProxyCoreKind.Xray)
                {
                    JsonNode? stream = node?["streamSettings"];
                    string? transport =
                        TryGetString(stream?["network"]) ??
                        TryGetString(stream?["method"]);
                    string? protection =
                        TryGetString(stream?["security"]);

                    if (!string.IsNullOrWhiteSpace(transport))
                    {
                        transports.Add(transport);
                    }

                    if (!string.IsNullOrWhiteSpace(protection) &&
                        !protection.Equals(
                            "none",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        security.Add(protection);
                    }
                }
                else
                {
                    if (node?["transport"] is JsonObject transportObject)
                    {
                        string? transport = TryGetString(
                            transportObject["type"]
                        );

                        if (!string.IsNullOrWhiteSpace(transport))
                        {
                            transports.Add(transport);
                        }
                    }

                    if (node?["tls"] is JsonObject tls &&
                        !IsFalse(tls["enabled"]))
                    {
                        security.Add("tls");
                    }
                }
            }

            foreach (JsonNode? endpoint in endpoints)
            {
                string? protocol = TryGetString(endpoint?["type"]);

                if (!string.IsNullOrWhiteSpace(protocol))
                {
                    protocols.Add(protocol);
                }
            }

            return new ProfileDescriptor(
                core,
                protocols.OrderBy(value => value).ToArray(),
                transports.OrderBy(value => value).ToArray(),
                security.OrderBy(value => value).ToArray()
            );
        }

        public static ProxyCoreKind ResolveCore(
            ProfileDescriptor profile,
            CorePreference preference)
        {
            ProxyCoreKind requested = preference switch
            {
                CorePreference.SingBox => ProxyCoreKind.SingBox,
                CorePreference.Xray => ProxyCoreKind.Xray,
                _ => profile.Core
            };

            if (requested != profile.Core)
            {
                throw new NotSupportedException(
                    $"Профиль имеет формат {FormatCore(profile.Core)}, " +
                    $"поэтому его нельзя запустить через " +
                    $"{FormatCore(requested)} без преобразования. " +
                    "Верните выбор ядра в режим «Автоматически»."
                );
            }

            return requested;
        }

        public static string FormatCore(ProxyCoreKind core) =>
            core == ProxyCoreKind.Xray ? "Xray" : "sing-box";

        private static string? TryGetString(JsonNode? node)
        {
            return node is JsonValue value &&
                   value.TryGetValue(out string? text) &&
                   !string.IsNullOrWhiteSpace(text)
                ? text.Trim()
                : null;
        }

        private static bool IsFalse(JsonNode? node)
        {
            return node is JsonValue value &&
                   value.TryGetValue(out bool enabled) &&
                   !enabled;
        }
    }

    public sealed record ProfileDescriptor(
        ProxyCoreKind Core,
        string[] Protocols,
        string[] Transports,
        string[] Security
    );
}

using GeniaProxy.Models;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GeniaProxy.Services
{
    public static class ProfileShareLinkService
    {
        public static ProfileShareLink Create(
            string profileName,
            string source)
        {
            string normalizedName = ProfileNameValidator.Normalize(profileName);
            string? nameError = ProfileNameValidator.GetValidationError(normalizedName);

            if (nameError is not null)
            {
                throw new InvalidDataException(nameError);
            }

            ArgumentException.ThrowIfNullOrWhiteSpace(source);

            JsonObject root;

            try
            {
                root = JsonNode.Parse(source)?.AsObject()
                    ?? throw new InvalidDataException(
                        "Корневой элемент профиля должен быть JSON-объектом."
                    );
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException(
                    "Профиль содержит некорректный JSON.",
                    ex
                );
            }

            ProfileDescriptor descriptor = ProfileFormatService.Inspect(source);

            return descriptor.Core == ProxyCoreKind.Xray
                ? CreateXrayVlessXhttp(normalizedName, root)
                : CreateSingBoxHysteria2(normalizedName, root);
        }

        private static ProfileShareLink CreateSingBoxHysteria2(
            string profileName,
            JsonObject root)
        {
            JsonObject outbound = FindSingleOutbound(
                root,
                "type",
                "hysteria2",
                ["direct", "block", "dns"]
            );

            string server = RequireString(outbound, "server", "Hysteria2 server");
            int port = RequirePort(outbound, "server_port", "Hysteria2 server_port");
            string password = RequireString(outbound, "password", "Hysteria2 password");

            var query = new List<KeyValuePair<string, string>>();

            if (outbound["tls"] is JsonObject tls)
            {
                string? sni = TryGetString(tls["server_name"]);
                if (!string.IsNullOrWhiteSpace(sni) &&
                    !sni.Equals(server, StringComparison.OrdinalIgnoreCase))
                {
                    query.Add(new("sni", sni));
                }

                if (TryGetBoolean(tls["insecure"]) == true)
                {
                    query.Add(new("insecure", "1"));
                }

                if (tls["alpn"] is JsonArray alpn)
                {
                    string value = string.Join(",", alpn
                        .Select(TryGetString)
                        .Where(value => !string.IsNullOrWhiteSpace(value))!);

                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        query.Add(new("alpn", value));
                    }
                }
            }

            AddIntegerQuery(outbound, "up_mbps", "upmbps", query);
            AddIntegerQuery(outbound, "down_mbps", "downmbps", query);

            if (outbound["obfs"] is JsonObject obfs)
            {
                string? type = TryGetString(obfs["type"]);
                string? secret = TryGetString(obfs["password"]);

                if (!string.IsNullOrWhiteSpace(type))
                {
                    query.Add(new("obfs", type));
                }

                if (!string.IsNullOrWhiteSpace(secret))
                {
                    query.Add(new("obfs-password", secret));
                }
            }

            string payload =
                $"hysteria2://{EscapeUserInfo(password)}@{FormatHost(server)}:{port}" +
                BuildQuery(query) +
                BuildFragment(profileName);

            return new ProfileShareLink(
                profileName,
                payload,
                "Hysteria2",
                "hysteria2://"
            );
        }

        private static ProfileShareLink CreateXrayVlessXhttp(
            string profileName,
            JsonObject root)
        {
            JsonObject outbound = FindSingleOutbound(
                root,
                "protocol",
                "vless",
                ["freedom", "blackhole", "dns"]
            );

            if (outbound["settings"] is not JsonObject settings ||
                settings["vnext"] is not JsonArray vnext ||
                vnext.Count != 1 ||
                vnext[0] is not JsonObject serverNode)
            {
                throw new NotSupportedException(
                    "Для компактного QR поддерживается VLESS-профиль с одним сервером."
                );
            }

            string server = RequireString(serverNode, "address", "VLESS address");
            int port = RequirePort(serverNode, "port", "VLESS port");

            if (serverNode["users"] is not JsonArray users ||
                users.Count != 1 ||
                users[0] is not JsonObject user)
            {
                throw new NotSupportedException(
                    "Для компактного QR поддерживается VLESS-профиль с одним пользователем."
                );
            }

            string uuid = RequireString(user, "id", "VLESS UUID");

            if (outbound["streamSettings"] is not JsonObject stream)
            {
                throw new NotSupportedException(
                    "VLESS-профиль не содержит streamSettings."
                );
            }

            string network = TryGetString(stream["network"]) ?? string.Empty;
            bool usesXhttp =
                network.Equals("xhttp", StringComparison.OrdinalIgnoreCase) ||
                network.Equals("splithttp", StringComparison.OrdinalIgnoreCase);
            bool usesRaw =
                network.Equals("raw", StringComparison.OrdinalIgnoreCase) ||
                network.Equals("tcp", StringComparison.OrdinalIgnoreCase);

            if (!usesXhttp && !usesRaw)
            {
                throw new NotSupportedException(
                    "Компактный QR поддерживает VLESS + XHTTP и " +
                    "VLESS + REALITY + Vision + RAW/TCP."
                );
            }

            string security = TryGetString(stream["security"]) ?? "none";
            bool usesTls = security.Equals(
                "tls",
                StringComparison.OrdinalIgnoreCase
            );
            bool usesReality = security.Equals(
                "reality",
                StringComparison.OrdinalIgnoreCase
            );

            if (!usesTls && !usesReality)
            {
                throw new NotSupportedException(
                    "Компактный QR поддерживает VLESS + XHTTP + TLS/REALITY " +
                    "и VLESS + REALITY + Vision + RAW/TCP."
                );
            }

            if (usesRaw && !usesReality)
            {
                throw new NotSupportedException(
                    "RAW/TCP поддерживается только с REALITY + Vision."
                );
            }

            string? flow = TryGetString(user["flow"]);
            if (usesRaw && !string.Equals(
                    flow,
                    "xtls-rprx-vision",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new NotSupportedException(
                    "RAW/TCP профиль должен использовать flow=xtls-rprx-vision."
                );
            }

            var query = new List<KeyValuePair<string, string>>
            {
                new("encryption", TryGetString(user["encryption"]) ?? "none"),
                new("security", usesReality ? "reality" : "tls"),
                new("type", usesRaw ? "tcp" : "xhttp")
            };

            if (!string.IsNullOrWhiteSpace(flow))
            {
                query.Add(new("flow", flow));
            }

            if (usesXhttp && stream["xhttpSettings"] is JsonObject xhttp)
            {
                AddStringQuery(xhttp, "path", "path", query, "/");
                AddStringQuery(xhttp, "host", "host", query);
                AddStringQuery(xhttp, "mode", "mode", query);

                // extra содержит расширенные XHTTP/XMUX-настройки. Это стандартный
                // параметр нашего VLESS importer, но добавляем его только если он
                // реально есть: обычный мобильный QR остаётся максимально коротким.
                if (xhttp["extra"] is JsonObject extra && extra.Count > 0)
                {
                    string compactExtra = extra.ToJsonString(
                        new JsonSerializerOptions { WriteIndented = false }
                    );
                    query.Add(new("extra", compactExtra));
                }
            }

            if (usesTls && stream["tlsSettings"] is JsonObject tls)
            {
                string? sni = TryGetString(tls["serverName"]);
                if (!string.IsNullOrWhiteSpace(sni))
                {
                    query.Add(new("sni", sni));
                }

                if (TryGetBoolean(tls["allowInsecure"]) == true)
                {
                    query.Add(new("allowInsecure", "1"));
                }

                string? fingerprint = TryGetString(tls["fingerprint"]);
                if (!string.IsNullOrWhiteSpace(fingerprint))
                {
                    query.Add(new("fp", fingerprint));
                }

                if (tls["alpn"] is JsonArray alpn)
                {
                    string value = string.Join(",", alpn
                        .Select(TryGetString)
                        .Where(value => !string.IsNullOrWhiteSpace(value))!);

                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        query.Add(new("alpn", value));
                    }
                }
            }
            else if (usesReality)
            {
                if (stream["realitySettings"] is not JsonObject reality)
                {
                    throw new NotSupportedException(
                        "VLESS + REALITY не содержит realitySettings."
                    );
                }

                string sni = RequireString(
                    reality,
                    "serverName",
                    "REALITY serverName"
                );
                string fingerprint = RequireString(
                    reality,
                    "fingerprint",
                    "REALITY fingerprint"
                );
                string? publicKey =
                    TryGetString(reality["publicKey"]) ??
                    TryGetString(reality["password"]);

                if (string.IsNullOrWhiteSpace(publicKey))
                {
                    throw new NotSupportedException(
                        "REALITY-профиль не содержит publicKey/password."
                    );
                }

                query.Add(new("sni", sni));
                query.Add(new("fp", fingerprint));
                query.Add(new("pbk", publicKey));

                string? shortId = TryGetString(reality["shortId"]);
                if (shortId is not null)
                {
                    query.Add(new("sid", shortId));
                }

                string? spiderX = TryGetString(reality["spiderX"]);
                if (!string.IsNullOrWhiteSpace(spiderX))
                {
                    query.Add(new("spx", spiderX));
                }
            }

            string payload =
                $"vless://{EscapeUserInfo(uuid)}@{FormatHost(server)}:{port}" +
                BuildQuery(query) +
                BuildFragment(profileName);

            return new ProfileShareLink(
                profileName,
                payload,
                usesRaw
                    ? "VLESS · REALITY · Vision · RAW/TCP"
                    : usesReality
                        ? "VLESS · XHTTP · REALITY"
                        : "VLESS · XHTTP · TLS",
                "vless://"
            );
        }

        private static JsonObject FindSingleOutbound(
            JsonObject root,
            string discriminator,
            string requiredValue,
            string[] allowedOtherValues)
        {
            if (root["outbounds"] is not JsonArray outbounds)
            {
                throw new NotSupportedException(
                    "В профиле отсутствует массив outbounds."
                );
            }

            var matching = new List<JsonObject>();

            foreach (JsonNode? node in outbounds)
            {
                if (node is not JsonObject outbound)
                {
                    continue;
                }

                string? value = TryGetString(outbound[discriminator]);
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                if (value.Equals(requiredValue, StringComparison.OrdinalIgnoreCase))
                {
                    matching.Add(outbound);
                    continue;
                }

                if (!allowedOtherValues.Contains(
                        value,
                        StringComparer.OrdinalIgnoreCase))
                {
                    throw new NotSupportedException(
                        "Профиль содержит дополнительные outbound-настройки, " +
                        "которые нельзя безопасно представить одной мобильной ссылкой."
                    );
                }
            }

            if (matching.Count != 1)
            {
                throw new NotSupportedException(
                    $"Для компактного QR требуется ровно один {requiredValue} outbound."
                );
            }

            return matching[0];
        }

        private static string RequireString(
            JsonObject obj,
            string key,
            string description)
        {
            string? value = TryGetString(obj[key]);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidDataException($"Отсутствует {description}.");
            }
            return value;
        }

        private static int RequirePort(
            JsonObject obj,
            string key,
            string description)
        {
            int? value = TryGetInteger(obj[key]);
            if (value is null or < 1 or > 65535)
            {
                throw new InvalidDataException($"Некорректный {description}.");
            }
            return value.Value;
        }

        private static void AddStringQuery(
            JsonObject source,
            string sourceKey,
            string queryKey,
            List<KeyValuePair<string, string>> target,
            string? defaultValue = null)
        {
            string? value = TryGetString(source[sourceKey]);
            if (string.IsNullOrWhiteSpace(value))
            {
                value = defaultValue;
            }
            if (!string.IsNullOrWhiteSpace(value))
            {
                target.Add(new(queryKey, value));
            }
        }

        private static void AddIntegerQuery(
            JsonObject source,
            string sourceKey,
            string queryKey,
            List<KeyValuePair<string, string>> target)
        {
            int? value = TryGetInteger(source[sourceKey]);
            if (value.HasValue)
            {
                target.Add(new(
                    queryKey,
                    value.Value.ToString(CultureInfo.InvariantCulture)
                ));
            }
        }

        private static string BuildQuery(
            IEnumerable<KeyValuePair<string, string>> values)
        {
            string[] items = values
                .Where(item => !string.IsNullOrWhiteSpace(item.Value))
                .Select(item =>
                    Uri.EscapeDataString(item.Key) + "=" +
                    Uri.EscapeDataString(item.Value))
                .ToArray();

            return items.Length == 0 ? string.Empty : "?" + string.Join("&", items);
        }

        private static string BuildFragment(string profileName) =>
            string.IsNullOrWhiteSpace(profileName)
                ? string.Empty
                : "#" + Uri.EscapeDataString(profileName);

        private static string EscapeUserInfo(string value) =>
            Uri.EscapeDataString(value);

        private static string FormatHost(string host) =>
            host.Contains(':') &&
            !host.StartsWith('[')
                ? "[" + host + "]"
                : host;

        private static string? TryGetString(JsonNode? node)
        {
            return node is JsonValue value &&
                   value.TryGetValue(out string? text) &&
                   !string.IsNullOrWhiteSpace(text)
                ? text.Trim()
                : null;
        }

        private static bool? TryGetBoolean(JsonNode? node)
        {
            if (node is not JsonValue value)
            {
                return null;
            }

            if (value.TryGetValue(out bool boolean))
            {
                return boolean;
            }

            return null;
        }

        private static int? TryGetInteger(JsonNode? node)
        {
            if (node is not JsonValue value)
            {
                return null;
            }

            if (value.TryGetValue(out int integer))
            {
                return integer;
            }

            if (value.TryGetValue(out long longInteger) &&
                longInteger is >= int.MinValue and <= int.MaxValue)
            {
                return (int)longInteger;
            }

            return null;
        }
    }

    public sealed record ProfileShareLink(
        string ProfileName,
        string Payload,
        string DisplayFormat,
        string Scheme
    );
}

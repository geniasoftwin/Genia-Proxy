using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GeniaProxy.Models;

namespace GeniaProxy.Services
{
    public static class JsonProfileImportService
    {
        public const int MaxProfileBytes = 1024 * 1024;

        private const int MaxJsonDepth = 64;
        private const int MaxOutbounds = 128;
        private const int MaxEndpoints = 32;

        public static string NormalizeConfig(
            string source,
            int localPort)
        {
            return NormalizeConfig(
                source,
                localPort,
                ConnectionMode.LocalProxy
            );
        }

        public static string NormalizeConfig(
            string source,
            int localPort,
            ConnectionMode mode,
            TunStackPreference tunStackPreference =
                TunStackPreference.Mixed)
        {
            ValidateLocalPort(localPort);

            JsonObject root = ParseRoot(source);

            JsonNode? outboundsNode = root["outbounds"];
            JsonNode? endpointsNode = root["endpoints"];

            if (outboundsNode is not null &&
                outboundsNode is not JsonArray)
            {
                throw new InvalidDataException(
                    "Поле outbounds должно быть JSON-массивом."
                );
            }

            if (endpointsNode is not null &&
                endpointsNode is not JsonArray)
            {
                throw new InvalidDataException(
                    "Поле endpoints должно быть JSON-массивом."
                );
            }

            JsonArray? outbounds = outboundsNode as JsonArray;
            JsonArray? endpoints = endpointsNode as JsonArray;

            if ((outbounds is null || outbounds.Count == 0) &&
                (endpoints is null || endpoints.Count == 0))
            {
                throw new InvalidDataException(
                    "В конфигурации отсутствуют outbounds и endpoints."
                );
            }

            if (outbounds is not null)
            {
                ValidateTypedObjects(
                    outbounds,
                    "outbound",
                    MaxOutbounds
                );
            }

            if (endpoints is not null)
            {
                ValidateEndpoints(endpoints);
            }

            string inboundTag = GetCompatibleInboundTag(root);

            // Пользовательский профиль не должен незаметно открывать
            // дополнительные порты, TUN-интерфейсы или API управления.
            // GeniaProxy всегда создаёт ровно один локальный mixed inbound.
            root["inbounds"] = new JsonArray
            {
                mode == ConnectionMode.Tun
                    ? CreateTunInbound(inboundTag, tunStackPreference)
                    : CreateControlledInbound(localPort, inboundTag)
            };

            if (mode == ConnectionMode.Tun)
            {
                JsonObject route = root["route"] as JsonObject
                    ?? new JsonObject();

                route["auto_detect_interface"] = true;
                root["route"] = route;
            }

            root.Remove("experimental");
            root.Remove("services");

            // Уровень warn уменьшает объём журнала и не записывает
            // каждое открытое браузером соединение.
            root["log"] = new JsonObject
            {
                ["level"] = "warn",
                ["timestamp"] = true
            };

            return root.ToJsonString(
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }
            );
        }

        public static bool ContainsInsecureTls(string source)
        {
            JsonObject root = ParseRoot(source);
            return ContainsInsecureTls(root);
        }

        private static void ValidateTypedObjects(
            JsonArray items,
            string itemName,
            int maximumCount)
        {
            if (items.Count > maximumCount)
            {
                throw new InvalidDataException(
                    $"В конфигурации слишком много {itemName}s " +
                    $"(максимум {maximumCount})."
                );
            }

            foreach (JsonNode? node in items)
            {
                if (node is not JsonObject item ||
                    item["type"] is not JsonValue typeValue ||
                    !typeValue.TryGetValue(out string? type) ||
                    string.IsNullOrWhiteSpace(type))
                {
                    throw new InvalidDataException(
                        $"Каждый {itemName} должен быть JSON-объектом " +
                        "с непустым полем type."
                    );
                }
            }
        }

        private static void ValidateEndpoints(JsonArray endpoints)
        {
            ValidateTypedObjects(
                endpoints,
                "endpoint",
                MaxEndpoints
            );

            foreach (JsonNode? node in endpoints)
            {
                string type = node!["type"]!.GetValue<string>();

                // В актуальном sing-box WireGuard перенесён из
                // outbounds в endpoints. Остальные endpoint-типы могут
                // создавать системные интерфейсы или фоновые службы и
                // поэтому требуют отдельной поддержки в приложении.
                if (!string.Equals(
                        type,
                        "wireguard",
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new NotSupportedException(
                        $"Endpoint типа «{type}» пока не поддерживается. " +
                        "Разрешён только WireGuard endpoint."
                    );
                }
            }
        }

        private static JsonObject ParseRoot(string source)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(source);

            int byteCount = Encoding.UTF8.GetByteCount(source);

            if (byteCount > MaxProfileBytes)
            {
                throw new InvalidDataException(
                    "JSON-профиль превышает допустимый размер 1 МБ."
                );
            }

            try
            {
                using JsonDocument _ = JsonDocument.Parse(
                    source,
                    new JsonDocumentOptions
                    {
                        MaxDepth = MaxJsonDepth,
                        AllowTrailingCommas = false,
                        CommentHandling =
                            JsonCommentHandling.Disallow
                    }
                );

                if (JsonNode.Parse(source) is not JsonObject root)
                {
                    throw new InvalidDataException(
                        "Корневой элемент конфигурации " +
                        "должен быть JSON-объектом."
                    );
                }

                return root;
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException(
                    "JSON-профиль содержит некорректный JSON.",
                    ex
                );
            }
        }

        private static JsonObject CreateControlledInbound(
            int localPort,
            string inboundTag)
        {
            return new JsonObject
            {
                ["type"] = "mixed",
                ["tag"] = inboundTag,
                ["listen"] = "127.0.0.1",
                ["listen_port"] = localPort,
                ["set_system_proxy"] = false
            };
        }

        private static JsonObject CreateTunInbound(
            string inboundTag,
            TunStackPreference tunStackPreference)
        {
            return new JsonObject
            {
                ["type"] = "tun",
                ["tag"] = inboundTag,
                ["interface_name"] = "GeniaProxy",
                ["address"] = new JsonArray("172.19.0.1/30"),
                ["mtu"] = 1500,
                ["auto_route"] = true,
                ["strict_route"] = true,
                ["stack"] = tunStackPreference switch
                {
                    TunStackPreference.System => "system",
                    TunStackPreference.GVisor => "gvisor",
                    _ => "mixed"
                }
            };
        }


        private static string GetCompatibleInboundTag(
            JsonObject root)
        {
            if (root["inbounds"] is JsonArray inbounds)
            {
                foreach (JsonNode? node in inbounds)
                {
                    if (node is not JsonObject inbound ||
                        inbound["type"] is not JsonValue typeValue ||
                        !typeValue.TryGetValue(out string? type) ||
                        !string.Equals(
                            type,
                            "mixed",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string? tag = null;

                    if (inbound["tag"] is JsonValue tagValue &&
                        tagValue.TryGetValue(out string? rawTag))
                    {
                        tag = rawTag?.Trim();
                    }

                    if (!string.IsNullOrWhiteSpace(tag) &&
                        tag.Length <= 128 &&
                        !tag.Any(char.IsControl))
                    {
                        return tag;
                    }
                }
            }

            return "mixed-in";
        }

        private static bool ContainsInsecureTls(
            JsonNode? node)
        {
            if (node is JsonObject jsonObject)
            {
                foreach ((string key, JsonNode? value) in jsonObject)
                {
                    if (string.Equals(
                            key,
                            "insecure",
                            StringComparison.OrdinalIgnoreCase) &&
                        IsTrue(value))
                    {
                        return true;
                    }

                    if (ContainsInsecureTls(value))
                    {
                        return true;
                    }
                }
            }
            else if (node is JsonArray jsonArray)
            {
                foreach (JsonNode? item in jsonArray)
                {
                    if (ContainsInsecureTls(item))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool IsTrue(JsonNode? node)
        {
            if (node is not JsonValue value)
            {
                return false;
            }

            if (value.TryGetValue(out bool booleanValue))
            {
                return booleanValue;
            }

            return value.TryGetValue(out string? stringValue) &&
                   (string.Equals(
                        stringValue,
                        "true",
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        stringValue,
                        "1",
                        StringComparison.OrdinalIgnoreCase));
        }

        private static void ValidateLocalPort(int localPort)
        {
            if (localPort is < 1 or > 65535)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(localPort),
                    "Локальный порт должен быть от 1 до 65535."
                );
            }
        }
    }
}

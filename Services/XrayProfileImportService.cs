using GeniaProxy.Models;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GeniaProxy.Services
{
    public sealed record XrayVlessEndpoint(string Address, int Port);

    public static class XrayProfileImportService
    {
        public const int MaxSourceBytes = 64 * 1024;

        public static string CreateVlessXhttpConfig(
            string source,
            int localPort)
        {
            ValidateLocalPort(localPort);
            Uri uri = ParseVlessUri(source);
            Dictionary<string, string> query = ParseQuery(uri.Query);

            string uuid = Decode(uri.UserInfo).Trim();

            if (string.IsNullOrWhiteSpace(uuid))
            {
                throw new FormatException(
                    "В ссылке VLESS отсутствует UUID."
                );
            }

            string transport = Get(query, "type") ?? string.Empty;
            bool usesXhttp =
                transport.Equals("xhttp", StringComparison.OrdinalIgnoreCase) ||
                transport.Equals("splithttp", StringComparison.OrdinalIgnoreCase);
            bool usesRaw =
                transport.Equals("raw", StringComparison.OrdinalIgnoreCase) ||
                transport.Equals("tcp", StringComparison.OrdinalIgnoreCase);

            if (!usesXhttp && !usesRaw)
            {
                throw new NotSupportedException(
                    "Поддерживаются VLESS + XHTTP и " +
                    "VLESS + REALITY + Vision + RAW/TCP."
                );
            }

            string security = Get(query, "security") ?? "none";
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
                    "Для VLESS требуется security=tls или security=reality."
                );
            }

            if (usesRaw && !usesReality)
            {
                throw new NotSupportedException(
                    "RAW/TCP в GeniaProxy поддерживается только как " +
                    "VLESS + REALITY + Vision."
                );
            }

            string? serverName =
                Get(query, "sni") ??
                Get(query, "serverName");

            if (usesTls && string.IsNullOrWhiteSpace(serverName))
            {
                serverName = uri.Host;
            }

            var user = new JsonObject
            {
                ["id"] = uuid,
                ["encryption"] = Get(query, "encryption") ?? "none"
            };

            string? flow = Get(query, "flow");

            if (usesRaw)
            {
                string encryption = Get(query, "encryption") ?? "none";
                if (!encryption.Equals("none", StringComparison.OrdinalIgnoreCase))
                {
                    throw new NotSupportedException(
                        "VLESS + REALITY + Vision + RAW/TCP требует encryption=none."
                    );
                }

                if (!string.Equals(
                        flow,
                        "xtls-rprx-vision",
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new NotSupportedException(
                        "VLESS + REALITY + RAW/TCP требует " +
                        "flow=xtls-rprx-vision."
                    );
                }

                string? headerType = Get(query, "headerType");
                if (!string.IsNullOrWhiteSpace(headerType) &&
                    !headerType.Equals("none", StringComparison.OrdinalIgnoreCase))
                {
                    throw new NotSupportedException(
                        "Для RAW/TCP поддерживается только headerType=none."
                    );
                }
            }

            if (!string.IsNullOrWhiteSpace(flow))
            {
                user["flow"] = flow;
            }

            var xhttp = new JsonObject
            {
                ["path"] = Get(query, "path") ?? "/"
            };

            AddIfNotEmpty(xhttp, "host", Get(query, "host"));

            string? xhttpMode = Get(query, "mode");

            if (usesReality && usesXhttp)
            {
                if (string.IsNullOrWhiteSpace(xhttpMode))
                {
                    // The REALITY branch is validated end-to-end only with
                    // XHTTP stream-one. Do not silently fall back to Xray's
                    // transport auto-selection when the share link omits mode.
                    xhttpMode = "stream-one";
                }
                else if (!xhttpMode.Equals(
                             "stream-one",
                             StringComparison.OrdinalIgnoreCase))
                {
                    throw new NotSupportedException(
                        "GeniaProxy 4.3.2 LTS поддерживает " +
                        "VLESS + XHTTP + REALITY только в режиме stream-one."
                    );
                }
            }

            if (usesXhttp)
            {
                AddIfNotEmpty(xhttp, "mode", xhttpMode);
                AddXhttpExtra(xhttp, Get(query, "extra"));
                ApplyXhttpXmuxDefaults(xhttp);
            }

            JsonObject securitySettings;
            string securitySettingsName;

            if (usesReality)
            {
                securitySettings = CreateRealityClientSettings(
                    query,
                    serverName
                );
                securitySettingsName = "realitySettings";
            }
            else
            {
                var tls = new JsonObject
                {
                    ["serverName"] = serverName!,
                    ["allowInsecure"] = GetBoolean(query, "allowInsecure") ||
                                        GetBoolean(query, "insecure")
                };

                AddIfNotEmpty(tls, "fingerprint", Get(query, "fp"));

                string? alpn = Get(query, "alpn");

                if (!string.IsNullOrWhiteSpace(alpn))
                {
                    tls["alpn"] = new JsonArray(
                        alpn.Split(
                                ',',
                                StringSplitOptions.RemoveEmptyEntries |
                                StringSplitOptions.TrimEntries
                            )
                            .Select(value => (JsonNode?)value)
                            .ToArray()
                    );
                }

                securitySettings = tls;
                securitySettingsName = "tlsSettings";
            }

            var root = new JsonObject
            {
                ["log"] = new JsonObject
                {
                    ["loglevel"] = "warning"
                },
                ["inbounds"] = new JsonArray(
                    CreateLocalInbound(localPort)
                ),
                ["outbounds"] = new JsonArray(
                    new JsonObject
                    {
                        ["tag"] = "proxy",
                        ["protocol"] = "vless",
                        ["settings"] = new JsonObject
                        {
                            ["vnext"] = new JsonArray(
                                new JsonObject
                                {
                                    ["address"] = uri.Host,
                                    ["port"] = uri.Port,
                                    ["users"] = new JsonArray(user)
                                }
                            )
                        },
                        ["streamSettings"] = usesXhttp
                            ? new JsonObject
                            {
                                ["network"] = "xhttp",
                                ["security"] = usesReality
                                    ? "reality"
                                    : "tls",
                                ["xhttpSettings"] = xhttp,
                                [securitySettingsName] = securitySettings
                            }
                            : new JsonObject
                            {
                                // RAW is the current Xray name for the historical
                                // TCP transport. tcp is accepted as a URI alias,
                                // but new runtime JSON is canonicalized to raw.
                                ["network"] = "raw",
                                ["security"] = "reality",
                                ["rawSettings"] = new JsonObject(),
                                [securitySettingsName] = securitySettings
                            }
                    },
                    new JsonObject
                    {
                        ["tag"] = "direct",
                        ["protocol"] = "freedom"
                    },
                    new JsonObject
                    {
                        ["tag"] = "block",
                        ["protocol"] = "blackhole"
                    }
                ),
                ["routing"] = new JsonObject
                {
                    ["domainStrategy"] = "AsIs",
                    ["rules"] = new JsonArray(
                        new JsonObject
                        {
                            ["type"] = "field",
                            ["ip"] = new JsonArray(
                                "10.0.0.0/8",
                                "100.64.0.0/10",
                                "127.0.0.0/8",
                                "169.254.0.0/16",
                                "172.16.0.0/12",
                                "192.168.0.0/16",
                                "224.0.0.0/4"
                            ),
                            ["outboundTag"] = "direct"
                        }
                    )
                }
            };

            if (usesRaw &&
                root["outbounds"] is JsonArray generatedOutbounds &&
                generatedOutbounds.Count > 0 &&
                generatedOutbounds[0] is JsonObject generatedProxy)
            {
                generatedProxy["mux"] = CreateRealityVisionRawXudpMux();
            }

            return root.ToJsonString(IndentedOptions);
        }

        public static string NormalizeConfig(
            string source,
            int localPort,
            ConnectionMode mode,
            int? systemProxyPort = null,
            string? pinnedVlessAddress = null)
        {
            ValidateLocalPort(localPort);
            JsonObject root = ParseRoot(source);

            if (ContainsPropertyRecursive(
                    root,
                    "pinnedPeerCertSha256"))
            {
                throw new NotSupportedException(
                    "Этот Xray-профиль использует pinnedPeerCertSha256. " +
                    "GeniaProxy 4.2.1 HF4 блокирует этот параметр " +
                    "fail-closed для bundled Xray 26.3.27. " +
                    "Удалите pinning или используйте версию GeniaProxy " +
                    "с явно поддерживаемым исправленным Xray."
                );
            }

            if (root["outbounds"] is not JsonArray outbounds ||
                outbounds.Count == 0)
            {
                throw new InvalidDataException(
                    "В Xray-профиле отсутствует массив outbounds."
                );
            }

            foreach (JsonNode? node in outbounds)
            {
                if (node is not JsonObject outbound ||
                    string.IsNullOrWhiteSpace(
                        TryGetString(outbound["protocol"])))
                {
                    throw new InvalidDataException(
                        "Каждый Xray outbound должен иметь поле protocol."
                    );
                }

                ApplyXhttpXmuxDefaultsToOutbound(outbound);
                ValidateRealityClientOutbound(outbound);
                ApplyRealityVisionRawXudpDefaultsToOutbound(outbound);
            }

            if (!string.IsNullOrWhiteSpace(pinnedVlessAddress))
            {
                PinPrimaryVlessEndpoint(root, pinnedVlessAddress);
            }

            root["inbounds"] = mode switch
            {
                ConnectionMode.Tun => new JsonArray(
                    CreateTunInbound()
                ),
                ConnectionMode.SystemProxy => new JsonArray(
                    CreateLocalInbound(localPort),
                    CreateHttpInbound(
                        systemProxyPort ??
                        GetSystemProxyPort(localPort)
                    )
                ),
                _ => new JsonArray(
                    CreateLocalInbound(localPort)
                )
            };

            if (mode == ConnectionMode.Tun)
            {
                ApplyTunRoutingSafety(root);
            }

            root.Remove("api");
            root.Remove("stats");
            root.Remove("metrics");
            root.Remove("reverse");
            root["log"] = new JsonObject
            {
                ["loglevel"] = "warning"
            };

            return root.ToJsonString(IndentedOptions);
        }

        public static bool ContainsInsecureTls(string source)
        {
            return ContainsTrueFlag(ParseRoot(source));
        }

        public static bool ContainsPinnedPeerCertSha256(string source)
        {
            return ContainsPropertyRecursive(
                ParseRoot(source),
                "pinnedPeerCertSha256"
            );
        }

        public static bool RequestsInsecureTls(string source)
        {
            Uri uri = ParseVlessUri(source);
            Dictionary<string, string> query = ParseQuery(uri.Query);

            return GetBoolean(query, "allowInsecure") ||
                   GetBoolean(query, "insecure");
        }

        private static JsonObject CreateLocalInbound(int localPort)
        {
            return new JsonObject
            {
                ["tag"] = "local-in",
                ["listen"] = "127.0.0.1",
                ["port"] = localPort,
                ["protocol"] = "socks",
                ["settings"] = new JsonObject
                {
                    ["auth"] = "noauth",
                    ["udp"] = true,
                    ["ip"] = "127.0.0.1"
                }
            };
        }

        private static JsonObject CreateHttpInbound(int port)
        {
            ValidateLocalPort(port);

            return new JsonObject
            {
                ["tag"] = "system-http-in",
                ["listen"] = "127.0.0.1",
                ["port"] = port,
                ["protocol"] = "http",
                ["settings"] = new JsonObject
                {
                    ["allowTransparent"] = false
                }
            };
        }

        public static int GetSystemProxyPort(int localPort)
        {
            ValidateLocalPort(localPort);
            return localPort == 65535
                ? localPort - 1
                : localPort + 1;
        }

        private static void ApplyTunRoutingSafety(JsonObject root)
        {
            if (root["outbounds"] is not JsonArray outbounds)
            {
                throw new InvalidDataException(
                    "В Xray-профиле отсутствует массив outbounds."
                );
            }

            bool hasBlockOutbound = outbounds
                .OfType<JsonObject>()
                .Any(outbound => string.Equals(
                    TryGetString(outbound["tag"]),
                    "block",
                    StringComparison.Ordinal
                ));

            if (!hasBlockOutbound)
            {
                outbounds.Add(new JsonObject
                {
                    ["tag"] = "block",
                    ["protocol"] = "blackhole"
                });
            }

            JsonObject routing = root["routing"] as JsonObject
                ?? new JsonObject();
            root["routing"] = routing;

            JsonArray rules = routing["rules"] as JsonArray
                ?? new JsonArray();
            routing["rules"] = rules;

            // Профиль, созданный GeniaProxy, содержит общий direct rule для
            // private/link-local диапазонов. В TUN-режиме реально доступные
            // локальные сети Windows и так обходят /1 благодаря более
            // специфичным системным маршрутам. Если оставить этот rule,
            // недоступный private/link-local адрес может выйти через freedom
            // обратно в TUN и образовать routing loop.
            for (int index = rules.Count - 1; index >= 0; index--)
            {
                if (rules[index] is JsonObject rule &&
                    IsGeneratedPrivateDirectRule(rule))
                {
                    rules.RemoveAt(index);
                }
            }

            // APIPA на Wintun создаёт connected 169.254/16. Broadcast NetBIOS
            // (например, 169.254.255.255:137), multicast и limited broadcast
            // нельзя выпускать через freedom: такой пакет вернётся в tun-in.
            rules.Insert(0, new JsonObject
            {
                ["type"] = "field",
                ["inboundTag"] = new JsonArray("tun-in"),
                ["ip"] = new JsonArray(
                    "169.254.0.0/16",
                    "224.0.0.0/4",
                    "255.255.255.255/32"
                ),
                ["outboundTag"] = "block"
            });
        }

        private static bool IsGeneratedPrivateDirectRule(JsonObject rule)
        {
            if (!string.Equals(
                    TryGetString(rule["type"]),
                    "field",
                    StringComparison.Ordinal) ||
                !string.Equals(
                    TryGetString(rule["outboundTag"]),
                    "direct",
                    StringComparison.Ordinal) ||
                rule["ip"] is not JsonArray ipArray ||
                rule.ContainsKey("inboundTag") ||
                rule.ContainsKey("domain") ||
                rule.ContainsKey("network") ||
                rule.ContainsKey("port"))
            {
                return false;
            }

            string[] expected =
            [
                "10.0.0.0/8",
                "100.64.0.0/10",
                "127.0.0.0/8",
                "169.254.0.0/16",
                "172.16.0.0/12",
                "192.168.0.0/16",
                "224.0.0.0/4"
            ];

            string[] actual = ipArray
                .Select(TryGetString)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();

            return actual.SequenceEqual(
                expected.OrderBy(value => value, StringComparer.Ordinal),
                StringComparer.Ordinal
            );
        }

        private static JsonObject CreateTunInbound()
        {
            return new JsonObject
            {
                ["tag"] = "tun-in",
                ["protocol"] = "tun",
                ["settings"] = new JsonObject
                {
                    ["name"] = WindowsTunNetworkService.XrayTunInterfaceName,
                    ["desc"] = "GeniaProxy",
                    ["mtu"] = 1500
                }
            };
        }

        public static XrayVlessEndpoint GetPrimaryVlessEndpoint(
            string source)
        {
            JsonObject root = ParseRoot(source);

            if (root["outbounds"] is not JsonArray outbounds)
            {
                throw new InvalidDataException(
                    "В Xray-профиле отсутствует массив outbounds."
                );
            }

            foreach (JsonNode? node in outbounds)
            {
                if (node is not JsonObject outbound ||
                    !string.Equals(
                        TryGetString(outbound["protocol"]),
                        "vless",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (outbound["settings"] is not JsonObject settings ||
                    settings["vnext"] is not JsonArray vnext ||
                    vnext.Count == 0 ||
                    vnext[0] is not JsonObject server)
                {
                    throw new InvalidDataException(
                        "VLESS outbound не содержит settings.vnext[0]."
                    );
                }

                string? address = TryGetString(server["address"]);
                int? port = server["port"]?.GetValue<int>();

                if (string.IsNullOrWhiteSpace(address) ||
                    port is null or < 1 or > 65535)
                {
                    throw new InvalidDataException(
                        "VLESS endpoint должен содержать корректные address и port."
                    );
                }

                return new XrayVlessEndpoint(address, port.Value);
            }

            throw new NotSupportedException(
                "Xray TUN 4.2.x поддерживает профиль с VLESS " +
                "settings.vnext endpoint."
            );
        }

        private static void PinPrimaryVlessEndpoint(
            JsonObject root,
            string pinnedVlessAddress)
        {
            if (!System.Net.IPAddress.TryParse(
                    pinnedVlessAddress,
                    out System.Net.IPAddress? parsed) ||
                parsed.AddressFamily !=
                    System.Net.Sockets.AddressFamily.InterNetwork)
            {
                throw new ArgumentException(
                    "Pinned VLESS endpoint должен быть IPv4-адресом.",
                    nameof(pinnedVlessAddress)
                );
            }

            if (root["outbounds"] is not JsonArray outbounds)
            {
                throw new InvalidDataException(
                    "В Xray-профиле отсутствует массив outbounds."
                );
            }

            foreach (JsonNode? node in outbounds)
            {
                if (node is not JsonObject outbound ||
                    !string.Equals(
                        TryGetString(outbound["protocol"]),
                        "vless",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (outbound["settings"] is not JsonObject settings ||
                    settings["vnext"] is not JsonArray vnext ||
                    vnext.Count == 0 ||
                    vnext[0] is not JsonObject server)
                {
                    throw new InvalidDataException(
                        "VLESS outbound не содержит settings.vnext[0]."
                    );
                }

                server["address"] = parsed.ToString();
                return;
            }

            throw new NotSupportedException(
                "Не найден VLESS outbound для pinning proxy endpoint."
            );
        }

        private static Uri ParseVlessUri(string source)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(source);
            source = source.Trim();

            if (Encoding.UTF8.GetByteCount(source) > MaxSourceBytes)
            {
                throw new FormatException("Ссылка VLESS слишком длинная.");
            }

            if (!Uri.TryCreate(source, UriKind.Absolute, out Uri? uri) ||
                !uri.Scheme.Equals("vless", StringComparison.OrdinalIgnoreCase))
            {
                throw new NotSupportedException(
                    "Ожидалась ссылка vless://."
                );
            }

            if (string.IsNullOrWhiteSpace(uri.Host) ||
                uri.Port is < 1 or > 65535)
            {
                throw new FormatException(
                    "В ссылке VLESS отсутствует адрес или порт сервера."
                );
            }

            return uri;
        }

        private static JsonObject ParseRoot(string source)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(source);

            if (Encoding.UTF8.GetByteCount(source) >
                JsonProfileImportService.MaxProfileBytes)
            {
                throw new InvalidDataException(
                    "Xray-профиль превышает допустимый размер 1 МБ."
                );
            }

            try
            {
                return JsonNode.Parse(source)?.AsObject()
                    ?? throw new InvalidDataException(
                        "Корневой элемент Xray-профиля должен быть объектом."
                    );
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException(
                    "Xray-профиль содержит некорректный JSON.",
                    ex
                );
            }
        }

        private static Dictionary<string, string> ParseQuery(string query)
        {
            var values = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase
            );

            foreach (string part in query.TrimStart('?').Split(
                         '&',
                         StringSplitOptions.RemoveEmptyEntries))
            {
                string[] pair = part.Split('=', 2);
                string key = Decode(pair[0]);
                string value = pair.Length == 2
                    ? Decode(pair[1])
                    : string.Empty;

                if (!string.IsNullOrWhiteSpace(key))
                {
                    values[key] = value;
                }
            }

            return values;
        }

        private static string? Get(
            Dictionary<string, string> values,
            string key)
        {
            return values.TryGetValue(key, out string? value) &&
                   !string.IsNullOrWhiteSpace(value)
                ? value.Trim()
                : null;
        }

        private static bool GetBoolean(
            Dictionary<string, string> values,
            string key)
        {
            string? value = Get(values, key);

            return value is not null &&
                   (value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                    value.Equals("true", StringComparison.OrdinalIgnoreCase));
        }

        private static string Decode(string value)
        {
            try
            {
                return Uri.UnescapeDataString(value);
            }
            catch (UriFormatException ex)
            {
                throw new FormatException(
                    "Ссылка содержит некорректное кодирование.",
                    ex
                );
            }
        }

        private static void AddIfNotEmpty(
            JsonObject target,
            string key,
            string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                target[key] = value;
            }
        }

        private static JsonObject CreateRealityClientSettings(
            Dictionary<string, string> query,
            string? serverName)
        {
            if (string.IsNullOrWhiteSpace(serverName))
            {
                throw new FormatException(
                    "Для VLESS + REALITY требуется параметр sni/serverName."
                );
            }

            string? publicKey =
                Get(query, "pbk") ??
                Get(query, "publicKey") ??
                Get(query, "password");

            if (string.IsNullOrWhiteSpace(publicKey))
            {
                throw new FormatException(
                    "Для VLESS + REALITY требуется публичный ключ " +
                    "REALITY (pbk/publicKey/password)."
                );
            }

            string? fingerprint = Get(query, "fp");

            if (string.IsNullOrWhiteSpace(fingerprint))
            {
                throw new FormatException(
                    "Для VLESS + REALITY требуется TLS fingerprint (fp)."
                );
            }

            ValidateRealityFingerprint(fingerprint);

            string shortId =
                Get(query, "sid") ??
                Get(query, "shortId") ??
                string.Empty;

            ValidateRealityShortId(shortId);

            var reality = new JsonObject
            {
                ["serverName"] = serverName,
                ["fingerprint"] = fingerprint,
                // Xray 26.3.27 exposes the REALITY client credential as
                // Password (PublicKey). Use the current config spelling for
                // newly imported links; JSON imports keep their original
                // publicKey/password spelling unchanged.
                ["password"] = publicKey,
                ["shortId"] = shortId
            };

            AddIfNotEmpty(
                reality,
                "spiderX",
                Get(query, "spx") ?? Get(query, "spiderX")
            );

            return reality;
        }

        private static void ValidateRealityClientOutbound(
            JsonObject outbound)
        {
            string protocol =
                TryGetString(outbound["protocol"]) ?? string.Empty;

            if (!protocol.Equals(
                    "vless",
                    StringComparison.OrdinalIgnoreCase) ||
                outbound["streamSettings"] is not JsonObject stream)
            {
                return;
            }

            string network =
                TryGetString(stream["network"]) ?? string.Empty;
            bool usesXhttp =
                network.Equals("xhttp", StringComparison.OrdinalIgnoreCase) ||
                network.Equals("splithttp", StringComparison.OrdinalIgnoreCase);
            bool usesRaw =
                network.Equals("raw", StringComparison.OrdinalIgnoreCase) ||
                network.Equals("tcp", StringComparison.OrdinalIgnoreCase);

            if (!usesXhttp && !usesRaw)
            {
                return;
            }

            string security =
                TryGetString(stream["security"]) ?? "none";

            if (!security.Equals(
                    "reality",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (usesRaw)
                {
                    throw new InvalidDataException(
                        "VLESS + RAW/TCP поддерживается только с REALITY + Vision."
                    );
                }
                return;
            }

            if (usesXhttp)
            {
                if (stream["xhttpSettings"] is not JsonObject realityXhttp)
                {
                    throw new InvalidDataException(
                        "VLESS + XHTTP + REALITY не содержит xhttpSettings."
                    );
                }

                string? runtimeMode = TryGetString(realityXhttp["mode"]);

                if (string.IsNullOrWhiteSpace(runtimeMode))
                {
                    realityXhttp["mode"] = "stream-one";
                }
                else if (!runtimeMode.Equals(
                             "stream-one",
                             StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "GeniaProxy 4.3.2 LTS поддерживает " +
                        "VLESS + XHTTP + REALITY только в режиме stream-one."
                    );
                }
            }
            else
            {
                ValidateRealityVisionUsers(outbound);

                JsonObject? rawSettings =
                    stream["rawSettings"] as JsonObject ??
                    stream["tcpSettings"] as JsonObject;

                if (rawSettings is not null &&
                    rawSettings["header"] is JsonObject header &&
                    !string.Equals(
                        TryGetString(header["type"]),
                        "none",
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "Для REALITY + Vision + RAW/TCP поддерживается только RAW без header obfuscation."
                    );
                }
            }

            if (stream["realitySettings"] is not JsonObject reality)
            {
                throw new InvalidDataException(
                    "VLESS + REALITY не содержит realitySettings."
                );
            }

            if (string.IsNullOrWhiteSpace(
                    TryGetString(reality["serverName"])))
            {
                throw new InvalidDataException(
                    "REALITY client settings не содержат serverName."
                );
            }

            string? fingerprint = TryGetString(reality["fingerprint"]);

            if (string.IsNullOrWhiteSpace(fingerprint))
            {
                throw new InvalidDataException(
                    "REALITY client settings не содержат fingerprint."
                );
            }

            try
            {
                ValidateRealityFingerprint(fingerprint);
            }
            catch (FormatException ex)
            {
                throw new InvalidDataException(ex.Message, ex);
            }

            string? publicKey = TryGetString(reality["publicKey"]);
            string? password = TryGetString(reality["password"]);

            if (string.IsNullOrWhiteSpace(publicKey) &&
                string.IsNullOrWhiteSpace(password))
            {
                throw new InvalidDataException(
                    "REALITY client settings не содержат publicKey/password."
                );
            }

            string shortId =
                TryGetString(reality["shortId"]) ?? string.Empty;

            try
            {
                ValidateRealityShortId(shortId);
            }
            catch (FormatException ex)
            {
                throw new InvalidDataException(ex.Message, ex);
            }
        }

        private static void ValidateRealityVisionUsers(JsonObject outbound)
        {
            if (outbound["settings"] is not JsonObject settings ||
                settings["vnext"] is not JsonArray vnext ||
                vnext.Count != 1 ||
                vnext[0] is not JsonObject server ||
                server["users"] is not JsonArray users ||
                users.Count == 0)
            {
                throw new InvalidDataException(
                    "VLESS + REALITY + Vision + RAW/TCP требует settings.vnext[0].users."
                );
            }

            foreach (JsonNode? node in users)
            {
                if (node is not JsonObject user ||
                    !string.Equals(
                        TryGetString(user["flow"]),
                        "xtls-rprx-vision",
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "VLESS + REALITY + RAW/TCP требует flow=xtls-rprx-vision для каждого пользователя."
                    );
                }
            }
        }

        private static void ValidateRealityFingerprint(string fingerprint)
        {
            // Keep the value from the share link verbatim; never substitute a
            // browser fingerprint. Validate it as a compact ASCII token so
            // malformed/injected values fail closed while known uTLS names such
            // as chrome, firefox, safari, edge and versioned underscore forms
            // remain representable.
            if (fingerprint.Length > 64 ||
                fingerprint.Any(character =>
                    !((character >= 'A' && character <= 'Z') ||
                      (character >= 'a' && character <= 'z') ||
                      (character >= '0' && character <= '9') ||
                      character == '_' ||
                      character == '-' ||
                      character == '.')))
            {
                throw new FormatException(
                    "REALITY fingerprint должен быть ASCII-токеном до 64 " +
                    "символов без пробелов."
                );
            }
        }

        private static void ValidateRealityShortId(string shortId)
        {
            if (shortId.Length > 16 ||
                shortId.Length % 2 != 0 ||
                shortId.Any(character => !Uri.IsHexDigit(character)))
            {
                throw new FormatException(
                    "REALITY shortId должен содержать до 16 шестнадцатеричных " +
                    "символов и иметь чётную длину."
                );
            }
        }

        private static JsonObject CreateRealityVisionRawXudpMux()
        {
            // Vision intentionally rejects native VLESS UDP requests. XUDP
            // carries UDP inside a Mux connection while concurrency=-1 keeps
            // ordinary TCP traffic on the native Vision/RAW path.
            return new JsonObject
            {
                ["enabled"] = true,
                ["concurrency"] = -1,
                ["xudpConcurrency"] = 8,
                ["xudpProxyUDP443"] = "reject"
            };
        }

        private static void ApplyRealityVisionRawXudpDefaultsToOutbound(
            JsonObject outbound)
        {
            string protocol =
                TryGetString(outbound["protocol"]) ?? string.Empty;

            if (!protocol.Equals(
                    "vless",
                    StringComparison.OrdinalIgnoreCase) ||
                outbound["streamSettings"] is not JsonObject stream)
            {
                return;
            }

            string network =
                TryGetString(stream["network"]) ?? string.Empty;
            string security =
                TryGetString(stream["security"]) ?? string.Empty;

            bool usesRaw =
                network.Equals("raw", StringComparison.OrdinalIgnoreCase) ||
                network.Equals("tcp", StringComparison.OrdinalIgnoreCase);

            if (!usesRaw ||
                !security.Equals(
                    "reality",
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            bool usesVision = false;

            if (outbound["settings"] is JsonObject settings &&
                settings["vnext"] is JsonArray vnext &&
                vnext.Count > 0 &&
                vnext[0] is JsonObject server &&
                server["users"] is JsonArray users &&
                users.Count > 0)
            {
                usesVision = users
                    .OfType<JsonObject>()
                    .All(user => string.Equals(
                        TryGetString(user["flow"]),
                        "xtls-rprx-vision",
                        StringComparison.OrdinalIgnoreCase
                    ));
            }

            if (!usesVision)
            {
                return;
            }

            JsonObject mux;

            if (outbound["mux"] is null)
            {
                mux = new JsonObject();
                outbound["mux"] = mux;
            }
            else if (outbound["mux"] is JsonObject existingMux)
            {
                mux = existingMux;
            }
            else
            {
                throw new InvalidDataException(
                    "VLESS + REALITY + Vision + RAW/TCP требует объект mux " +
                    "для XUDP."
                );
            }

            if (!mux.ContainsKey("enabled"))
            {
                mux["enabled"] = true;
            }

            if (!mux.ContainsKey("concurrency"))
            {
                mux["concurrency"] = -1;
            }

            if (!mux.ContainsKey("xudpConcurrency"))
            {
                mux["xudpConcurrency"] = 8;
            }

            if (!mux.ContainsKey("xudpProxyUDP443"))
            {
                mux["xudpProxyUDP443"] = "reject";
            }

            if (mux["enabled"]?.GetValue<bool>() != true)
            {
                throw new NotSupportedException(
                    "VLESS + REALITY + Vision + RAW/TCP требует mux.enabled=true " +
                    "для XUDP."
                );
            }

            int xudpConcurrency =
                mux["xudpConcurrency"]?.GetValue<int>() ?? 0;

            if (xudpConcurrency < 1 || xudpConcurrency > 1024)
            {
                throw new NotSupportedException(
                    "VLESS + REALITY + Vision + RAW/TCP требует " +
                    "xudpConcurrency от 1 до 1024."
                );
            }

            string xudpProxyUdp443 =
                TryGetString(mux["xudpProxyUDP443"]) ?? "reject";

            if (!xudpProxyUdp443.Equals(
                    "reject",
                    StringComparison.OrdinalIgnoreCase) &&
                !xudpProxyUdp443.Equals(
                    "allow",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new NotSupportedException(
                    "Для REALITY + Vision + RAW/TCP xudpProxyUDP443 " +
                    "поддерживает только reject или allow."
                );
            }
        }

        private static void ApplyXhttpXmuxDefaultsToOutbound(
            JsonObject outbound)
        {
            string protocol =
                TryGetString(outbound["protocol"]) ?? string.Empty;

            if (!protocol.Equals(
                    "vless",
                    StringComparison.OrdinalIgnoreCase) ||
                outbound["streamSettings"] is not JsonObject stream)
            {
                return;
            }

            string network =
                TryGetString(stream["network"]) ?? string.Empty;

            if (!network.Equals(
                    "xhttp",
                    StringComparison.OrdinalIgnoreCase) &&
                !network.Equals(
                    "splithttp",
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (stream["xhttpSettings"] is JsonObject xhttp)
            {
                ApplyXhttpXmuxDefaults(xhttp);
            }
        }

        private static void ApplyXhttpXmuxDefaults(
            JsonObject xhttp)
        {
            JsonObject extra;

            if (xhttp["extra"] is null)
            {
                extra = new JsonObject();
                xhttp["extra"] = extra;
            }
            else if (xhttp["extra"] is JsonObject existingExtra)
            {
                extra = existingExtra;
            }
            else
            {
                return;
            }

            JsonObject xmux;

            if (extra["xmux"] is null)
            {
                xmux = new JsonObject();
                extra["xmux"] = xmux;
            }
            else if (extra["xmux"] is JsonObject existingXmux)
            {
                xmux = existingXmux;
            }
            else
            {
                return;
            }

            // maxConnections и maxConcurrency взаимоисключающие.
            // Если профиль явно задал любой из них, сохраняем выбор
            // пользователя и не добавляем второй параметр.
            if (!xmux.ContainsKey("maxConcurrency") &&
                !xmux.ContainsKey("maxConnections"))
            {
                xmux["maxConcurrency"] = "16-32";
            }

            if (!xmux.ContainsKey("hMaxRequestTimes"))
            {
                xmux["hMaxRequestTimes"] = "600-900";
            }

            if (!xmux.ContainsKey("hMaxReusableSecs"))
            {
                xmux["hMaxReusableSecs"] = "1800-3000";
            }
        }

        private static void AddXhttpExtra(
            JsonObject xhttp,
            string? extra)
        {
            if (string.IsNullOrWhiteSpace(extra))
            {
                return;
            }

            try
            {
                JsonNode? value = JsonNode.Parse(extra);

                if (value is not JsonObject)
                {
                    throw new FormatException(
                        "Параметр XHTTP extra должен быть JSON-объектом."
                    );
                }

                xhttp["extra"] = value;
            }
            catch (JsonException ex)
            {
                throw new FormatException(
                    "Параметр XHTTP extra содержит некорректный JSON.",
                    ex
                );
            }
        }

        private static string? TryGetString(JsonNode? node)
        {
            return node is JsonValue value &&
                   value.TryGetValue(out string? text)
                ? text
                : null;
        }

        private static bool ContainsPropertyRecursive(
            JsonNode? node,
            string propertyName)
        {
            if (node is JsonObject jsonObject)
            {
                foreach ((string key, JsonNode? value) in jsonObject)
                {
                    if (key.Equals(
                            propertyName,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    if (ContainsPropertyRecursive(
                            value,
                            propertyName))
                    {
                        return true;
                    }
                }
            }
            else if (node is JsonArray array)
            {
                foreach (JsonNode? value in array)
                {
                    if (ContainsPropertyRecursive(
                            value,
                            propertyName))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool ContainsTrueFlag(JsonNode? node)
        {
            if (node is JsonObject jsonObject)
            {
                foreach ((string key, JsonNode? value) in jsonObject)
                {
                    if ((key.Equals("allowInsecure", StringComparison.OrdinalIgnoreCase) ||
                         key.Equals("insecure", StringComparison.OrdinalIgnoreCase)) &&
                        value is JsonValue flag &&
                        flag.TryGetValue(out bool enabled) && enabled)
                    {
                        return true;
                    }

                    if (ContainsTrueFlag(value))
                    {
                        return true;
                    }
                }
            }
            else if (node is JsonArray array)
            {
                return array.Any(ContainsTrueFlag);
            }

            return false;
        }

        private static void ValidateLocalPort(int localPort)
        {
            if (localPort is < 1024 or > 65535)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(localPort),
                    "Локальный порт должен быть от 1024 до 65535."
                );
            }
        }

        private static readonly JsonSerializerOptions IndentedOptions =
            new()
            {
                WriteIndented = true
            };
    }
}

using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using GeniaProxy.Models;
using GeniaProxy.Services;

namespace GeniaProxy.Tests
{
    internal static class Program
    {
        private static int Main()
        {
            (string Name, Action Test)[] tests =
            [
                ("Импорт Hysteria2", CreateHysteria2Config),
                ("Импорт VLESS XHTTP TLS", CreateVlessXhttpConfig),
                ("Импорт VLESS XHTTP REALITY", CreateVlessXhttpRealityConfig),
                ("Импорт VLESS REALITY Vision RAW", CreateVlessRealityVisionRawConfig),
                ("Валидация REALITY shortId", ValidateRealityShortId),
                ("Автовыбор ядра", DetectProfileCore),
                ("Конфигурации TUN", CreateTunConfigs),
                ("Символ плюс в параметре", PreserveLiteralPlus),
                ("Отклонение ссылки без порта", RejectMissingPort),
                ("Отклонение другого протокола", RejectUnsupportedScheme),
                ("Проверка имён профилей", ValidateProfileNames),
                ("Метаданные Browser Direct Bridge Stable", ValidateBrowserIntegrationMetadata),
                ("Атомарная перезапись файла", ReplaceFileAtomically),
                ("Импорт JSON-профиля", NormalizeJsonProfile),
                ("Защита JSON-профиля", HardenJsonProfile),
                ("WireGuard endpoint", PreserveWireGuardEndpoint),
                ("Отклонение опасного endpoint", RejectUnsafeEndpoint),
                ("Координатор операций", CoordinateOperations),
                ("Анализ профиля", InspectProfile),
                ("Реальный протокол Hysteria2", IdentifyHysteria2Protocol),
                ("Очистка управляющих символов", SanitizeTerminalOutput),
                ("Определение insecure TLS", DetectInsecureTls),
                ("Архив профилей", BackupProfilesRoundTrip),
                ("Переименование и удаление профиля", ManageProfileFiles),
                ("Локальный QR профиля", CreateProfileQrCode),
                ("Endpoint профиля", InspectProfileEndpoint),
                ("Блокировка Xray pinnedPeerCertSha256", RejectPinnedPeerCertSha256),
                ("Валидация TUN snapshot", ValidateTunSnapshot),
                ("Отклонение инъекции в TUN snapshot", RejectMaliciousTunSnapshot),
                ("Отклонение чужой TUN topology", RejectUnexpectedTunSnapshotTopology),
                ("Разбор проверки канала", ParseConnectionTrace),
                ("Отчёт privacy probe", FormatPrivacyReport)
            ];

            int failed = 0;

            foreach ((string name, Action test) in tests)
            {
                try
                {
                    test();
                    Console.WriteLine($"[OK] {name}");
                }
                catch (Exception ex)
                {
                    failed++;
                    Console.Error.WriteLine(
                        $"[FAIL] {name}: {ex.Message}"
                    );
                }
            }

            Console.WriteLine();
            Console.WriteLine(
                $"Проверок: {tests.Length}, ошибок: {failed}."
            );

            return failed == 0 ? 0 : 1;
        }

        private static void CreateHysteria2Config()
        {
            string json = Hysteria2ImportService
                .CreateSingBoxConfig(
                    "hy2://secret@example.com:443" +
                    "?sni=edge.example.com&insecure=1",
                    2080
                );

            JsonObject root = JsonNode.Parse(json)?.AsObject()
                ?? throw new Exception(
                    "Не создан корневой JSON-объект."
                );

            JsonObject inbound =
                root["inbounds"]?.AsArray()[0]?.AsObject()
                ?? throw new Exception(
                    "Не создан mixed inbound."
                );

            JsonObject outbound =
                root["outbounds"]?.AsArray()[0]?.AsObject()
                ?? throw new Exception(
                    "Не создан Hysteria2 outbound."
                );

            AssertEqual(
                2080,
                inbound["listen_port"]?.GetValue<int>()
            );
            AssertEqual(
                "127.0.0.1",
                inbound["listen"]?.GetValue<string>()
            );
            AssertEqual(
                "secret",
                outbound["password"]?.GetValue<string>()
            );
            AssertEqual(
                "edge.example.com",
                outbound["tls"]?["server_name"]
                    ?.GetValue<string>()
            );
        }

        private static void PreserveLiteralPlus()
        {
            string json = Hysteria2ImportService
                .CreateSingBoxConfig(
                    "hysteria2://secret@example.com:443" +
                    "?obfs=salamander&obfs-password=a+b",
                    2080
                );

            JsonObject root =
                JsonNode.Parse(json)!.AsObject();

            string? password = root["outbounds"]?[0]?
                ["obfs"]?["password"]?.GetValue<string>();

            AssertEqual("a+b", password);
        }

        private static void RejectMissingPort()
        {
            AssertThrows<FormatException>(() =>
                Hysteria2ImportService.CreateSingBoxConfig(
                    "hy2://secret@example.com",
                    2080
                )
            );
        }

        private static void RejectUnsupportedScheme()
        {
            AssertThrows<NotSupportedException>(() =>
                Hysteria2ImportService.CreateSingBoxConfig(
                    "https://example.com:443",
                    2080
                )
            );
        }

        private static void ValidateProfileNames()
        {
            AssertEqual(
                "Office",
                ProfileNameValidator.Normalize(
                    " Office.json "
                )
            );

            AssertNotNull(
                ProfileNameValidator.GetValidationError("CON")
            );
            AssertNotNull(
                ProfileNameValidator.GetValidationError("Office.")
            );
            AssertEqual(
                null,
                ProfileNameValidator.GetValidationError("Office")
            );
        }

        private static void ValidateBrowserIntegrationMetadata()
        {
            AssertEqual("5.6.0", BrowserIntegrationService.SwitcherVersion);
            AssertEqual("5.6.0.6", BrowserIntegrationService.SwitcherManifestVersion);
            AssertEqual("direct-1-exp2", BrowserIntegrationService.BridgeVersion);
            AssertEqual(47831, BrowserDirectBridgeService.Port);
            AssertEqual(1, BrowserDirectBridgeService.ProtocolVersion);
        }

        private static void ReplaceFileAtomically()
        {
            string directory = CreateTestDirectory();
            string path = Path.Combine(
                directory,
                "profile.json"
            );

            try
            {
                var encoding = new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false
                );

                AtomicFileWriter.WriteAllText(
                    path,
                    "first",
                    encoding
                );
                AtomicFileWriter.WriteAllText(
                    path,
                    "second",
                    encoding
                );

                AssertEqual(
                    "second",
                    File.ReadAllText(path, encoding)
                );
            }
            finally
            {
                DeleteTestDirectory(directory);
            }
        }

        private static void NormalizeJsonProfile()
        {
            const string source = """
                {
                  "log": { "level": "info" },
                  "outbounds": [
                    { "type": "direct", "tag": "direct" }
                  ]
                }
                """;

            string json = JsonProfileImportService
                .NormalizeConfig(source, 3090);

            JsonObject root =
                JsonNode.Parse(json)!.AsObject();

            JsonObject inbound = root["inbounds"]?[0]
                ?.AsObject()
                ?? throw new Exception(
                    "Не создан mixed inbound."
                );

            AssertEqual(
                "mixed",
                inbound["type"]?.GetValue<string>()
            );
            AssertEqual(
                "127.0.0.1",
                inbound["listen"]?.GetValue<string>()
            );
            AssertEqual(
                3090,
                inbound["listen_port"]?.GetValue<int>()
            );
        }


        private static void HardenJsonProfile()
        {
            const string source = """
                {
                  "log": { "level": "debug" },
                  "experimental": {
                    "clash_api": { "external_controller": "0.0.0.0:9090" }
                  },
                  "inbounds": [
                    { "type": "mixed", "tag": "browser-in", "listen": "0.0.0.0", "listen_port": 9999 },
                    { "type": "direct", "listen": "0.0.0.0", "listen_port": 8888 }
                  ],
                  "outbounds": [
                    { "type": "direct", "tag": "direct" }
                  ]
                }
                """;

            JsonObject root = JsonNode.Parse(
                JsonProfileImportService.NormalizeConfig(
                    source,
                    2080
                )
            )!.AsObject();

            JsonArray inbounds = root["inbounds"]?.AsArray()
                ?? throw new Exception("Нет массива inbounds.");

            AssertEqual(1, inbounds.Count);
            AssertEqual(
                "127.0.0.1",
                inbounds[0]?["listen"]?.GetValue<string>()
            );
            AssertEqual(
                "browser-in",
                inbounds[0]?["tag"]?.GetValue<string>()
            );
            AssertEqual(
                false,
                root.ContainsKey("experimental")
            );
            AssertEqual(
                "warn",
                root["log"]?["level"]?.GetValue<string>()
            );
        }

        private static void SanitizeTerminalOutput()
        {
            string source =
                "normal\x07" +
                "\x1B[31mred\x1B[0m" +
                "\x1B]0;title\x07" +
                "\nnext";

            string cleaned =
                TerminalOutputSanitizer.Sanitize(source);

            AssertEqual(
                "normalred\nnext",
                cleaned
            );
        }

        private static void PreserveWireGuardEndpoint()
        {
            const string source = """
                {
                  "endpoints": [
                    {
                      "type": "wireguard",
                      "tag": "wg-out",
                      "address": ["10.0.0.2/32"],
                      "private_key": "secret",
                      "peers": []
                    }
                  ],
                  "route": { "final": "wg-out" }
                }
                """;

            JsonObject root = JsonNode.Parse(
                JsonProfileImportService.NormalizeConfig(
                    source,
                    2080
                )
            )!.AsObject();

            AssertEqual(
                "wireguard",
                root["endpoints"]?[0]?["type"]
                    ?.GetValue<string>()
            );
        }

        private static void RejectUnsafeEndpoint()
        {
            const string source = """
                {
                  "endpoints": [
                    { "type": "tailscale", "tag": "ts-out" }
                  ]
                }
                """;

            AssertThrows<NotSupportedException>(() =>
                JsonProfileImportService.NormalizeConfig(
                    source,
                    2080
                )
            );
        }

        private static void DetectInsecureTls()
        {
            AssertEqual(
                true,
                Hysteria2ImportService.RequestsInsecureTls(
                    "hy2://secret@example.com:443?insecure=1"
                )
            );

            AssertEqual(
                true,
                JsonProfileImportService.ContainsInsecureTls(
                    "{\"outbounds\":[{\"type\":\"direct\",\"tls\":{\"insecure\":true}}]}"
                )
            );
        }

        private static void CoordinateOperations()
        {
            using var coordinator = new OperationCoordinator();

            CancellationToken token = coordinator.Begin(
                ApplicationOperation.CheckProfile
            );

            AssertEqual(true, coordinator.IsBusy);
            AssertEqual(
                ApplicationOperation.CheckProfile,
                coordinator.CurrentOperation
            );

            AssertThrows<InvalidOperationException>(() =>
                coordinator.Begin(
                    ApplicationOperation.StartConnection
                )
            );

            coordinator.Cancel();
            AssertEqual(true, token.IsCancellationRequested);

            coordinator.End();
            AssertEqual(false, coordinator.IsBusy);
        }

        private static void InspectProfile()
        {
            const string source = """
                {
                  "experimental": {
                    "clash_api": {
                      "external_controller": "0.0.0.0:9090"
                    }
                  },
                  "inbounds": [
                    {
                      "type": "mixed",
                      "listen": "0.0.0.0",
                      "listen_port": 9999
                    }
                  ],
                  "outbounds": [
                    {
                      "type": "hysteria2",
                      "tag": "proxy",
                      "tls": { "insecure": true }
                    }
                  ]
                }
                """;

            ProfileInspection inspection =
                ProfileInspectionService.Inspect(source);

            AssertEqual(1, inspection.OutboundCount);
            AssertEqual(0, inspection.EndpointCount);
            AssertEqual(true, inspection.ContainsInsecureTls);
            AssertEqual(
                true,
                inspection.Protocols.Contains("hysteria2")
            );
            AssertEqual(
                true,
                inspection.Changes.Any(change =>
                    change.Contains(
                        "experimental",
                        StringComparison.Ordinal
                    ))
            );
        }

        private static void IdentifyHysteria2Protocol()
        {
            string source = Hysteria2ImportService
                .CreateSingBoxConfig(
                    "hysteria2://secret@example.com:443",
                    2080
                );

            ProfileInspection inspection =
                ProfileInspectionService.Inspect(source);

            AssertEqual(
                true,
                inspection.Protocols.Contains("hysteria2")
            );

            AssertEqual(
                false,
                inspection.Protocols.Contains("wireguard")
            );
        }

        private static void BackupProfilesRoundTrip()
        {
            string directory = CreateTestDirectory();
            string profiles = Path.Combine(
                directory,
                "profiles"
            );
            string settings = Path.Combine(
                directory,
                "settings.json"
            );
            string archive = Path.Combine(
                directory,
                "backup.zip"
            );

            try
            {
                Directory.CreateDirectory(profiles);
                File.WriteAllText(
                    Path.Combine(profiles, "Office.json"),
                    "{\"outbounds\":[{\"type\":\"direct\"}]}"
                );
                File.WriteAllText(
                    settings,
                    "{\"localPort\":2080}"
                );

                var service = new ProfileBackupService(
                    profiles,
                    settings
                );

                BackupExportResult exported =
                    service.Export(archive);

                File.Delete(
                    Path.Combine(profiles, "Office.json")
                );

                BackupRestoreResult restored =
                    service.Restore(
                        archive,
                        BackupRestoreMode.Replace
                    );

                AssertEqual(1, exported.ProfilesExported);
                AssertEqual(1, restored.ProfilesRestored);
                AssertEqual(
                    true,
                    File.Exists(
                        Path.Combine(
                            profiles,
                            "Office.json"
                        )
                    )
                );
            }
            finally
            {
                DeleteTestDirectory(directory);
            }
        }

        private static void CreateVlessXhttpConfig()
        {
            const string uuid =
                "11111111-2222-3333-4444-555555555555";

            string json = XrayProfileImportService
                .CreateVlessXhttpConfig(
                    $"vless://{uuid}@edge.example.com:443" +
                    "?encryption=none&security=tls" +
                    "&sni=cdn.example.com&type=xhttp" +
                    "&path=%2Fsecret&mode=stream-one" +
                    "&extra=%7B%22noGRPCHeader%22%3Atrue%7D#Office",
                    2080
                );

            JsonObject root = JsonNode.Parse(json)!.AsObject();
            JsonObject inbound = root["inbounds"]?[0]!.AsObject()
                ?? throw new Exception("Не создан Xray inbound.");
            JsonObject outbound = root["outbounds"]?[0]!.AsObject()
                ?? throw new Exception("Не создан VLESS outbound.");

            AssertEqual(
                "socks",
                inbound["protocol"]?.GetValue<string>()
            );
            AssertEqual(2080, inbound["port"]?.GetValue<int>());
            AssertEqual(
                "vless",
                outbound["protocol"]?.GetValue<string>()
            );
            AssertEqual(
                "xhttp",
                outbound["streamSettings"]?["network"]
                    ?.GetValue<string>()
            );
            AssertEqual(
                "tls",
                outbound["streamSettings"]?["security"]
                    ?.GetValue<string>()
            );
            AssertEqual(
                "cdn.example.com",
                outbound["streamSettings"]?["tlsSettings"]?
                    ["serverName"]?.GetValue<string>()
            );
            AssertEqual(
                true,
                outbound["streamSettings"]?["xhttpSettings"]?
                    ["extra"]?["noGRPCHeader"]?.GetValue<bool>()
            );
            AssertEqual(
                "16-32",
                outbound["streamSettings"]?["xhttpSettings"]?
                    ["extra"]?["xmux"]?["maxConcurrency"]
                    ?.GetValue<string>()
            );
            AssertEqual(
                "600-900",
                outbound["streamSettings"]?["xhttpSettings"]?
                    ["extra"]?["xmux"]?["hMaxRequestTimes"]
                    ?.GetValue<string>()
            );
            AssertEqual(
                "1800-3000",
                outbound["streamSettings"]?["xhttpSettings"]?
                    ["extra"]?["xmux"]?["hMaxReusableSecs"]
                    ?.GetValue<string>()
            );

            string explicitXmux = Uri.EscapeDataString(
                "{\"xmux\":{\"maxConnections\":\"3\"," +
                "\"hMaxRequestTimes\":\"100-200\"}}"
            );

            JsonObject explicitRoot = JsonNode.Parse(
                XrayProfileImportService.CreateVlessXhttpConfig(
                    $"vless://{uuid}@edge.example.com:443" +
                    "?encryption=none&security=tls&type=xhttp" +
                    $"&extra={explicitXmux}",
                    2080
                )
            )!.AsObject();

            JsonObject explicitXhttp = explicitRoot["outbounds"]?[0]?
                ["streamSettings"]?["xhttpSettings"]?.AsObject()
                ?? throw new Exception(
                    "Не созданы XHTTP-настройки с явным XMUX."
                );

            AssertEqual(
                "3",
                explicitXhttp["extra"]?["xmux"]?
                    ["maxConnections"]?.GetValue<string>()
            );
            AssertEqual<JsonNode?>(
                null,
                explicitXhttp["extra"]?["xmux"]?
                    ["maxConcurrency"]
            );
            AssertEqual(
                "100-200",
                explicitXhttp["extra"]?["xmux"]?
                    ["hMaxRequestTimes"]?.GetValue<string>()
            );
            AssertEqual(
                "1800-3000",
                explicitXhttp["extra"]?["xmux"]?
                    ["hMaxReusableSecs"]?.GetValue<string>()
            );
        }

        private static void CreateVlessXhttpRealityConfig()
        {
            const string uuid =
                "11111111-2222-3333-4444-555555555555";
            const string publicKey =
                "mS7f7KkP4Xfx2tFh_o9mD6f2jJv6Y3hZ6vB0Xc4pQwE";
            const string shortId = "0123456789abcdef";

            string json = XrayProfileImportService
                .CreateVlessXhttpConfig(
                    $"vless://{uuid}@edge.example.com:8443" +
                    "?encryption=none&security=reality" +
                    "&sni=www.cloudflare.com&fp=chrome" +
                    $"&pbk={publicKey}&sid={shortId}" +
                    "&spx=%2Fgeniaproxy&type=xhttp" +
                    "&path=%2Freality&mode=stream-one#Reality",
                    2081
                );

            JsonObject root = JsonNode.Parse(json)!.AsObject();
            JsonObject outbound = root["outbounds"]?[0]!.AsObject()
                ?? throw new Exception("Не создан REALITY outbound.");
            JsonObject stream = outbound["streamSettings"]?.AsObject()
                ?? throw new Exception("Не созданы streamSettings.");
            JsonObject reality = stream["realitySettings"]?.AsObject()
                ?? throw new Exception("Не созданы realitySettings.");

            AssertEqual("xhttp", stream["network"]?.GetValue<string>());
            AssertEqual("reality", stream["security"]?.GetValue<string>());
            AssertEqual(
                "www.cloudflare.com",
                reality["serverName"]?.GetValue<string>()
            );
            AssertEqual(
                "chrome",
                reality["fingerprint"]?.GetValue<string>()
            );
            AssertEqual(
                publicKey,
                reality["password"]?.GetValue<string>()
            );
            AssertEqual<JsonNode?>(null, reality["publicKey"]);
            AssertEqual(
                shortId,
                reality["shortId"]?.GetValue<string>()
            );
            AssertEqual(
                "/geniaproxy",
                reality["spiderX"]?.GetValue<string>()
            );
            AssertEqual<JsonNode?>(null, stream["tlsSettings"]);
            AssertEqual(
                "16-32",
                stream["xhttpSettings"]?["extra"]?["xmux"]?
                    ["maxConcurrency"]?.GetValue<string>()
            );
            AssertEqual(
                "600-900",
                stream["xhttpSettings"]?["extra"]?["xmux"]?
                    ["hMaxRequestTimes"]?.GetValue<string>()
            );
            AssertEqual(
                "1800-3000",
                stream["xhttpSettings"]?["extra"]?["xmux"]?
                    ["hMaxReusableSecs"]?.GetValue<string>()
            );

            string passwordAliasJson =
                "{\"outbounds\":[{\"protocol\":\"vless\"," +
                "\"settings\":{\"vnext\":[{\"address\":\"edge.example.com\"," +
                "\"port\":8443,\"users\":[{\"id\":\"" + uuid +
                "\",\"encryption\":\"none\"}]}]}," +
                "\"streamSettings\":{\"network\":\"xhttp\"," +
                "\"security\":\"reality\"," +
                "\"xhttpSettings\":{\"path\":\"/\",\"mode\":\"stream-one\"}," +
                "\"realitySettings\":{\"serverName\":\"www.cloudflare.com\"," +
                "\"fingerprint\":\"chrome\",\"password\":\"" + publicKey +
                "\",\"shortId\":\"" + shortId + "\"}}}]}";

            JsonObject normalized = JsonNode.Parse(
                XrayProfileImportService.NormalizeConfig(
                    passwordAliasJson,
                    2081,
                    ConnectionMode.LocalProxy
                )
            )!.AsObject();

            AssertEqual(
                publicKey,
                normalized["outbounds"]?[0]?["streamSettings"]?
                    ["realitySettings"]?["password"]?.GetValue<string>()
            );
            AssertEqual<JsonNode?>(
                null,
                normalized["outbounds"]?[0]?["streamSettings"]?
                    ["realitySettings"]?["publicKey"]
            );

            string noModeJson = passwordAliasJson.Replace(
                "\"xhttpSettings\":{\"path\":\"/\",\"mode\":\"stream-one\"}",
                "\"xhttpSettings\":{\"path\":\"/\"}"
            );

            JsonObject normalizedNoMode = JsonNode.Parse(
                XrayProfileImportService.NormalizeConfig(
                    noModeJson,
                    2081,
                    ConnectionMode.LocalProxy
                )
            )!.AsObject();

            AssertEqual(
                "stream-one",
                normalizedNoMode["outbounds"]?[0]?["streamSettings"]?
                    ["xhttpSettings"]?["mode"]?.GetValue<string>()
            );

            string publicKeyJson = passwordAliasJson.Replace(
                "\"password\":\"" + publicKey + "\"",
                "\"publicKey\":\"" + publicKey + "\""
            );

            JsonObject normalizedPublicKey = JsonNode.Parse(
                XrayProfileImportService.NormalizeConfig(
                    publicKeyJson,
                    2081,
                    ConnectionMode.LocalProxy
                )
            )!.AsObject();

            AssertEqual(
                publicKey,
                normalizedPublicKey["outbounds"]?[0]?["streamSettings"]?
                    ["realitySettings"]?["publicKey"]?.GetValue<string>()
            );
            AssertEqual<JsonNode?>(
                null,
                normalizedPublicKey["outbounds"]?[0]?["streamSettings"]?
                    ["realitySettings"]?["password"]
            );

            string defaultModeJson = XrayProfileImportService
                .CreateVlessXhttpConfig(
                    $"vless://{uuid}@edge.example.com:8443" +
                    "?encryption=none&security=reality" +
                    "&sni=www.cloudflare.com&fp=chrome" +
                    $"&pbk={publicKey}&sid={shortId}" +
                    "&type=xhttp&path=%2Freality#RealityDefaultMode",
                    2081
                );

            JsonObject defaultModeRoot = JsonNode.Parse(
                defaultModeJson
            )!.AsObject();

            AssertEqual(
                "stream-one",
                defaultModeRoot["outbounds"]?[0]?["streamSettings"]?
                    ["xhttpSettings"]?["mode"]?.GetValue<string>()
            );
        }

        private static void CreateVlessRealityVisionRawConfig()
        {
            const string uuid =
                "11111111-2222-3333-4444-555555555555";
            const string publicKey =
                "mS7f7KkP4Xfx2tFh_o9mD6f2jJv6Y3hZ6vB0Xc4pQwE";
            const string shortId = "0123456789abcdef";

            string json = XrayProfileImportService.CreateVlessXhttpConfig(
                $"vless://{uuid}@edge.example.com:2053" +
                "?encryption=none&security=reality&type=tcp" +
                "&flow=xtls-rprx-vision&headerType=none" +
                "&sni=www.cloudflare.com&fp=firefox" +
                $"&pbk={publicKey}&sid={shortId}#RealityVisionRaw",
                2081
            );

            JsonObject root = JsonNode.Parse(json)!.AsObject();
            JsonObject outbound = root["outbounds"]![0]!.AsObject();
            AssertEqual(
                "raw",
                outbound["streamSettings"]?["network"]?.GetValue<string>()
            );
            AssertEqual(
                "reality",
                outbound["streamSettings"]?["security"]?.GetValue<string>()
            );
            AssertEqual(
                "xtls-rprx-vision",
                outbound["settings"]?["vnext"]?[0]?["users"]?[0]?["flow"]?
                    .GetValue<string>()
            );
            AssertEqual(
                publicKey,
                outbound["streamSettings"]?["realitySettings"]?["password"]?
                    .GetValue<string>()
            );
            AssertEqual(
                "firefox",
                outbound["streamSettings"]?["realitySettings"]?["fingerprint"]?
                    .GetValue<string>()
            );
            AssertEqual(
                true,
                outbound["mux"]?["enabled"]?.GetValue<bool>()
            );
            AssertEqual(
                -1,
                outbound["mux"]?["concurrency"]?.GetValue<int>()
            );
            AssertEqual(
                8,
                outbound["mux"]?["xudpConcurrency"]?.GetValue<int>()
            );
            AssertEqual(
                "reject",
                outbound["mux"]?["xudpProxyUDP443"]?.GetValue<string>()
            );

            string normalized = XrayProfileImportService.NormalizeConfig(
                json,
                2081,
                ConnectionMode.LocalProxy
            );
            JsonObject normalizedRoot = JsonNode.Parse(normalized)!.AsObject();
            AssertEqual(
                "raw",
                normalizedRoot["outbounds"]?[0]?["streamSettings"]?["network"]?
                    .GetValue<string>()
            );
            AssertEqual(
                true,
                normalizedRoot["outbounds"]?[0]?["mux"]?["enabled"]?
                    .GetValue<bool>()
            );
            AssertEqual(
                8,
                normalizedRoot["outbounds"]?[0]?["mux"]?["xudpConcurrency"]?
                    .GetValue<int>()
            );

            AssertEqual(
                "firefox",
                normalizedRoot["outbounds"]?[0]?["streamSettings"]?
                    ["realitySettings"]?["fingerprint"]?.GetValue<string>()
            );

            AssertThrows<NotSupportedException>(() =>
                XrayProfileImportService.CreateVlessXhttpConfig(
                    $"vless://{uuid}@edge.example.com:2053" +
                    "?encryption=none&security=reality&type=tcp" +
                    "&sni=www.cloudflare.com&fp=firefox" +
                    $"&pbk={publicKey}&sid={shortId}",
                    2081
                )
            );

            AssertThrows<NotSupportedException>(() =>
                XrayProfileImportService.CreateVlessXhttpConfig(
                    $"vless://{uuid}@edge.example.com:2053" +
                    "?encryption=none&security=reality&type=tcp" +
                    "&flow=xtls-rprx-vision&headerType=http" +
                    "&sni=www.cloudflare.com&fp=firefox" +
                    $"&pbk={publicKey}&sid={shortId}",
                    2081
                )
            );

            ProfileQrData qr = ProfileQrService.Create("RAW", json);
            AssertEqual("VLESS · REALITY · Vision · RAW/TCP", qr.DisplayFormat);
            AssertEqual(true, qr.Payload.Contains("type=tcp"));
            AssertEqual(true, qr.Payload.Contains("flow=xtls-rprx-vision"));
            AssertEqual(true, qr.Payload.Contains("fp=firefox"));

            AssertThrows<FormatException>(() =>
                XrayProfileImportService.CreateVlessXhttpConfig(
                    $"vless://{uuid}@edge.example.com:2053" +
                    "?encryption=none&security=reality&type=raw" +
                    "&flow=xtls-rprx-vision" +
                    "&sni=www.cloudflare.com&fp=fire%20fox" +
                    $"&pbk={publicKey}&sid={shortId}",
                    2081
                )
            );
        }

        private static void ValidateRealityShortId()
        {
            const string uuid =
                "11111111-2222-3333-4444-555555555555";
            const string publicKey =
                "mS7f7KkP4Xfx2tFh_o9mD6f2jJv6Y3hZ6vB0Xc4pQwE";

            AssertThrows<FormatException>(() =>
                XrayProfileImportService.CreateVlessXhttpConfig(
                    $"vless://{uuid}@edge.example.com:8443" +
                    "?security=reality&type=xhttp" +
                    "&sni=www.cloudflare.com&fp=chrome" +
                    $"&pbk={publicKey}&sid=abc",
                    2081
                )
            );

            AssertThrows<FormatException>(() =>
                XrayProfileImportService.CreateVlessXhttpConfig(
                    $"vless://{uuid}@edge.example.com:8443" +
                    "?security=reality&type=xhttp" +
                    "&sni=www.cloudflare.com&fp=chrome" +
                    $"&pbk={publicKey}&sid=zz",
                    2081
                )
            );
        }

        private static void DetectProfileCore()
        {
            const string singBox =
                "{\"outbounds\":[{\"type\":\"direct\"}]}";
            const string xray =
                "{\"outbounds\":[{\"protocol\":\"freedom\"}]}";

            ProfileDescriptor singBoxProfile =
                ProfileFormatService.Inspect(singBox);
            ProfileDescriptor xrayProfile =
                ProfileFormatService.Inspect(xray);

            AssertEqual(ProxyCoreKind.SingBox, singBoxProfile.Core);
            AssertEqual(ProxyCoreKind.Xray, xrayProfile.Core);
            AssertEqual(
                ProxyCoreKind.Xray,
                ProfileFormatService.ResolveCore(
                    xrayProfile,
                    CorePreference.Automatic
                )
            );
            AssertThrows<NotSupportedException>(() =>
                ProfileFormatService.ResolveCore(
                    xrayProfile,
                    CorePreference.SingBox
                )
            );
        }

        private static void CreateTunConfigs()
        {
            const string singBox =
                "{\"outbounds\":[{\"type\":\"direct\"}]}";

            JsonObject singBoxRoot = JsonNode.Parse(
                JsonProfileImportService.NormalizeConfig(
                    singBox,
                    2080,
                    ConnectionMode.Tun
                )
            )!.AsObject();

            AssertEqual(
                "tun",
                singBoxRoot["inbounds"]?[0]?["type"]
                    ?.GetValue<string>()
            );
            AssertEqual(
                true,
                singBoxRoot["inbounds"]?[0]?["auto_route"]
                    ?.GetValue<bool>()
            );
            AssertEqual(
                true,
                singBoxRoot["inbounds"]?[0]?["strict_route"]
                    ?.GetValue<bool>()
            );

            JsonObject gVisorRoot = JsonNode.Parse(
                JsonProfileImportService.NormalizeConfig(
                    singBox,
                    2080,
                    ConnectionMode.Tun,
                    TunStackPreference.GVisor
                )
            )!.AsObject();

            AssertEqual(
                "gvisor",
                gVisorRoot["inbounds"]?[0]?["stack"]
                    ?.GetValue<string>()
            );

            string xraySource = XrayProfileImportService
                .CreateVlessXhttpConfig(
                    "vless://11111111-2222-3333-4444-555555555555" +
                    "@edge.example.com:443?security=tls&type=xhttp" +
                    "&sni=tls.example.com&host=xhttp.example.com",
                    2080
                );

            XrayVlessEndpoint endpoint = XrayProfileImportService
                .GetPrimaryVlessEndpoint(xraySource);

            AssertEqual("edge.example.com", endpoint.Address);
            AssertEqual(443, endpoint.Port);

            JsonObject xrayRoot = JsonNode.Parse(
                XrayProfileImportService.NormalizeConfig(
                    xraySource,
                    2080,
                    ConnectionMode.Tun,
                    pinnedVlessAddress: "203.0.113.10"
                )
            )!.AsObject();

            AssertEqual(
                "tun",
                xrayRoot["inbounds"]?[0]?["protocol"]
                    ?.GetValue<string>()
            );

            JsonObject tunSettings = xrayRoot["inbounds"]?[0]?
                ["settings"]?.AsObject()
                ?? throw new Exception("Не созданы Xray TUN settings.");

            AssertEqual(
                WindowsTunNetworkService.XrayTunInterfaceName,
                tunSettings["name"]?.GetValue<string>()
            );
            AssertEqual(false, tunSettings.ContainsKey("gateway"));
            AssertEqual(false, tunSettings.ContainsKey("dns"));
            AssertEqual(
                false,
                tunSettings.ContainsKey("autoSystemRoutingTable")
            );
            AssertEqual(
                false,
                tunSettings.ContainsKey("autoOutboundsInterface")
            );

            JsonArray xrayRules = xrayRoot["routing"]?["rules"]?.AsArray()
                ?? throw new Exception("Не созданы Xray TUN routing rules.");
            JsonObject tunSafetyRule = xrayRules[0]?.AsObject()
                ?? throw new Exception("Не создан Xray TUN safety rule.");

            AssertEqual(
                "block",
                tunSafetyRule["outboundTag"]?.GetValue<string>()
            );
            AssertEqual(
                "tun-in",
                tunSafetyRule["inboundTag"]?[0]?.GetValue<string>()
            );

            JsonArray safetyIps = tunSafetyRule["ip"]?.AsArray()
                ?? throw new Exception("Нет IP в Xray TUN safety rule.");
            AssertEqual(
                true,
                safetyIps.Any(node => node?.GetValue<string>() ==
                    "169.254.0.0/16")
            );
            AssertEqual(
                true,
                safetyIps.Any(node => node?.GetValue<string>() ==
                    "224.0.0.0/4")
            );
            AssertEqual(
                true,
                safetyIps.Any(node => node?.GetValue<string>() ==
                    "255.255.255.255/32")
            );
            AssertEqual(
                false,
                xrayRules
                    .OfType<JsonObject>()
                    .Where(rule => rule["outboundTag"]?.GetValue<string>() ==
                        "direct")
                    .SelectMany(rule => rule["ip"]?.AsArray() ?? [])
                    .Any(node => node?.GetValue<string>() ==
                        "169.254.0.0/16")
            );

            AssertEqual(
                "203.0.113.10",
                xrayRoot["outbounds"]?[0]?["settings"]?["vnext"]?[0]?
                    ["address"]?.GetValue<string>()
            );
            AssertEqual(
                "tls.example.com",
                xrayRoot["outbounds"]?[0]?["streamSettings"]?
                    ["tlsSettings"]?["serverName"]?.GetValue<string>()
            );
            AssertEqual(
                "xhttp.example.com",
                xrayRoot["outbounds"]?[0]?["streamSettings"]?
                    ["xhttpSettings"]?["host"]?.GetValue<string>()
            );

            var preparation = new XrayTunPreparation(
                "edge.example.com",
                443,
                IPAddress.Parse("203.0.113.10"),
                7,
                "physical-id",
                "Кабель",
                IPAddress.Parse("192.168.0.20"),
                IPAddress.Parse("192.168.0.1"),
                [IPAddress.Parse("192.168.0.1")],
                true
            );

            var adapter = new TunAdapterInfo(
                42,
                "tun-id",
                WindowsTunNetworkService.XrayTunInterfaceName,
                IPAddress.Parse("169.254.167.8")
            );

            IReadOnlyList<TunRoutePlanItem> routePlan =
                WindowsTunNetworkService.BuildXrayRoutePlan(
                    preparation,
                    adapter
                );

            AssertEqual(3, routePlan.Count);
            AssertEqual("203.0.113.10/32", routePlan[0].DestinationPrefix);
            AssertEqual(7, routePlan[0].InterfaceIndex);
            AssertEqual("0.0.0.0/1", routePlan[1].DestinationPrefix);
            AssertEqual(42, routePlan[1].InterfaceIndex);
            AssertEqual("169.254.167.8", routePlan[1].NextHop);
            AssertEqual("128.0.0.0/1", routePlan[2].DestinationPrefix);
            AssertEqual("169.254.167.8", routePlan[2].NextHop);

            JsonObject systemRoot = JsonNode.Parse(
                XrayProfileImportService.NormalizeConfig(
                    xraySource,
                    2080,
                    ConnectionMode.SystemProxy
                )
            )!.AsObject();

            AssertEqual(2, systemRoot["inbounds"]?.AsArray().Count);
            AssertEqual(
                "http",
                systemRoot["inbounds"]?[1]?["protocol"]
                    ?.GetValue<string>()
            );
            AssertEqual(
                2081,
                systemRoot["inbounds"]?[1]?["port"]
                    ?.GetValue<int>()
            );
        }

        private static void ManageProfileFiles()
        {
            string directory = CreateTestDirectory();

            try
            {
                Directory.CreateDirectory(directory);
                string originalPath = Path.Combine(
                    directory,
                    "Original.json"
                );
                File.WriteAllText(
                    originalPath,
                    "{\"outbounds\":[{\"type\":\"direct\"}]}"
                );

                var service = new ProfileFileService(directory);
                service.Rename("Original", "Renamed");

                string renamedPath = Path.Combine(
                    directory,
                    "Renamed.json"
                );

                AssertEqual(false, File.Exists(originalPath));
                AssertEqual(true, File.Exists(renamedPath));
                AssertThrows<InvalidDataException>(() =>
                    service.Rename("Renamed", "../unsafe")
                );

                service.Delete("Renamed");
                AssertEqual(false, File.Exists(renamedPath));
            }
            finally
            {
                DeleteTestDirectory(directory);
            }
        }

        private static void CreateProfileQrCode()
        {
            ProfileQrData qr = ProfileQrService.Create(
                "Mobile",
                "{\"outbounds\":[{\"type\":\"hysteria2\"," +
                "\"server\":\"example.com\",\"server_port\":443," +
                "\"password\":\"secret\"}]}"
            );

            AssertEqual("Mobile", qr.ProfileName);
            AssertEqual(true, qr.Payload.StartsWith(
                "hysteria2://",
                StringComparison.OrdinalIgnoreCase
            ));
            AssertEqual("Hysteria2", qr.DisplayFormat);
            AssertEqual(true, qr.PayloadBytes > 0);
            AssertEqual(true, qr.PayloadBytes < 256);
            AssertEqual(true, qr.PngBytes.Length > 8);
            AssertEqual((byte)0x89, qr.PngBytes[0]);
            AssertEqual((byte)0x50, qr.PngBytes[1]);

            string hysteriaRoundTrip =
                Hysteria2ImportService.CreateSingBoxConfig(qr.Payload, 2080);
            JsonObject hysteriaRoot = JsonNode.Parse(hysteriaRoundTrip)!.AsObject();
            AssertEqual(
                "hysteria2",
                hysteriaRoot["outbounds"]?[0]?["type"]?.GetValue<string>()
            );
            AssertEqual(
                "secret",
                hysteriaRoot["outbounds"]?[0]?["password"]?.GetValue<string>()
            );

            const string xrayJson =
                "{\"outbounds\":[" +
                "{\"tag\":\"proxy\",\"protocol\":\"vless\"," +
                "\"settings\":{\"vnext\":[{\"address\":\"edge.example.com\"," +
                "\"port\":443,\"users\":[{\"id\":\"11111111-2222-3333-4444-555555555555\"," +
                "\"encryption\":\"none\"}]}]}," +
                "\"streamSettings\":{\"network\":\"xhttp\",\"security\":\"tls\"," +
                "\"xhttpSettings\":{\"path\":\"/x\",\"mode\":\"stream-one\"}," +
                "\"tlsSettings\":{\"serverName\":\"edge.example.com\",\"fingerprint\":\"chrome\"}}}," +
                "{\"tag\":\"direct\",\"protocol\":\"freedom\"}," +
                "{\"tag\":\"block\",\"protocol\":\"blackhole\"}]}";

            ProfileQrData xrayQr = ProfileQrService.Create("XHTTP", xrayJson);
            AssertEqual(true, xrayQr.Payload.StartsWith(
                "vless://",
                StringComparison.OrdinalIgnoreCase
            ));
            AssertEqual("VLESS · XHTTP · TLS", xrayQr.DisplayFormat);
            AssertEqual(true, xrayQr.Payload.Contains("type=xhttp"));
            AssertEqual(true, xrayQr.Payload.Contains("mode=stream-one"));

            string xrayRoundTrip =
                XrayProfileImportService.CreateVlessXhttpConfig(xrayQr.Payload, 2080);
            JsonObject xrayRoot = JsonNode.Parse(xrayRoundTrip)!.AsObject();
            AssertEqual(
                "vless",
                xrayRoot["outbounds"]?[0]?["protocol"]?.GetValue<string>()
            );

            const string realityXrayJson =
                "{\"outbounds\":[" +
                "{\"tag\":\"proxy\",\"protocol\":\"vless\"," +
                "\"settings\":{\"vnext\":[{\"address\":\"edge.example.com\"," +
                "\"port\":8443,\"users\":[{\"id\":\"11111111-2222-3333-4444-555555555555\"," +
                "\"encryption\":\"none\"}]}]}," +
                "\"streamSettings\":{\"network\":\"xhttp\",\"security\":\"reality\"," +
                "\"xhttpSettings\":{\"path\":\"/x\",\"mode\":\"stream-one\"}," +
                "\"realitySettings\":{\"serverName\":\"www.cloudflare.com\"," +
                "\"fingerprint\":\"chrome\"," +
                "\"publicKey\":\"mS7f7KkP4Xfx2tFh_o9mD6f2jJv6Y3hZ6vB0Xc4pQwE\"," +
                "\"shortId\":\"0123456789abcdef\",\"spiderX\":\"/qr\"}}}," +
                "{\"tag\":\"direct\",\"protocol\":\"freedom\"}," +
                "{\"tag\":\"block\",\"protocol\":\"blackhole\"}]}";

            ProfileQrData realityQr = ProfileQrService.Create(
                "REALITY",
                realityXrayJson
            );
            AssertEqual(
                "VLESS · XHTTP · REALITY",
                realityQr.DisplayFormat
            );
            AssertEqual(true, realityQr.Payload.Contains("security=reality"));
            AssertEqual(true, realityQr.Payload.Contains("pbk="));
            AssertEqual(true, realityQr.Payload.Contains("sid=0123456789abcdef"));

            JsonObject realityRoundTrip = JsonNode.Parse(
                XrayProfileImportService.CreateVlessXhttpConfig(
                    realityQr.Payload,
                    2081
                )
            )!.AsObject();
            AssertEqual(
                "reality",
                realityRoundTrip["outbounds"]?[0]?["streamSettings"]?
                    ["security"]?.GetValue<string>()
            );

            string oversized =
                "{\"outbounds\":[{\"type\":\"hysteria2\"," +
                "\"server\":\"example.com\",\"server_port\":443," +
                "\"password\":\"" +
                new string('x', ProfileQrService.MaxPayloadBytes + 200) +
                "\"}]}";

            AssertThrows<InvalidDataException>(() =>
                ProfileQrService.Create("TooLarge", oversized)
            );
        }

        private static void InspectProfileEndpoint()
        {
            string hysteriaSource = Hysteria2ImportService
                .CreateSingBoxConfig(
                    "hysteria2://secret@198.51.100.10:443",
                    2080
                );

            ProfileEndpoint? hysteriaEndpoint =
                ProfileEndpointService.Inspect(hysteriaSource);

            AssertNotNull(hysteriaEndpoint);
            AssertEqual("198.51.100.10", hysteriaEndpoint!.Host);
            AssertEqual(443, hysteriaEndpoint.Port);

            ResolvedProfileEndpoint? literalResolved =
                ProfileEndpointService.ResolveAsync(hysteriaEndpoint)
                    .GetAwaiter()
                    .GetResult();

            AssertNotNull(literalResolved);
            AssertEqual("198.51.100.10", literalResolved!.Address);

            const string xraySource =
                "{\"outbounds\":[" +
                "{\"tag\":\"proxy\",\"protocol\":\"vless\"," +
                "\"settings\":{\"vnext\":[{\"address\":\"203.0.113.10\"," +
                "\"port\":443,\"users\":[]}]}} ," +
                "{\"protocol\":\"freedom\",\"tag\":\"direct\"}]}";

            ProfileEndpoint? xrayEndpoint =
                ProfileEndpointService.Inspect(xraySource);

            AssertNotNull(xrayEndpoint);
            AssertEqual("203.0.113.10", xrayEndpoint!.Host);
            AssertEqual(443, xrayEndpoint.Port);
        }

        private static void RejectPinnedPeerCertSha256()
        {
            const string source =
                "{\"outbounds\":[{" +
                "\"protocol\":\"vless\"," +
                "\"settings\":{\"vnext\":[{" +
                "\"address\":\"edge.example.com\"," +
                "\"port\":443,\"users\":[]}]}," +
                "\"streamSettings\":{\"security\":\"tls\"," +
                "\"tlsSettings\":{\"pinnedPeerCertSha256\":[\"deadbeef\"]}}}]}";

            AssertEqual(
                true,
                XrayProfileImportService.ContainsPinnedPeerCertSha256(
                    source
                )
            );

            AssertThrows<NotSupportedException>(() =>
                XrayProfileImportService.NormalizeConfig(
                    source,
                    2080,
                    ConnectionMode.LocalProxy
                )
            );
        }

        private static void ValidateTunSnapshot()
        {
            TunNetworkSnapshot snapshot = CreateValidTunSnapshot();

            WindowsTunNetworkService.ValidateSnapshotForRestore(
                snapshot
            );
        }

        private static void RejectMaliciousTunSnapshot()
        {
            TunNetworkSnapshot snapshot = CreateValidTunSnapshot();
            snapshot.AppliedRoutes[0].DestinationPrefix =
                "203.0.113.10/32'; Start-Process calc; #'";

            AssertThrows<InvalidDataException>(() =>
                WindowsTunNetworkService.ValidateSnapshotForRestore(
                    snapshot
                )
            );
        }

        private static void RejectUnexpectedTunSnapshotTopology()
        {
            TunNetworkSnapshot snapshot = CreateValidTunSnapshot();
            snapshot.AppliedRoutes[1].NextHop = "192.168.0.1";

            AssertThrows<InvalidDataException>(() =>
                WindowsTunNetworkService.ValidateSnapshotForRestore(
                    snapshot
                )
            );
        }

        private static TunNetworkSnapshot CreateValidTunSnapshot()
        {
            return new TunNetworkSnapshot
            {
                FormatVersion = 1,
                SessionId = Guid.NewGuid().ToString("N"),
                CreatedUtc = DateTime.UtcNow,
                PhysicalInterfaceIndex = 7,
                PhysicalInterfaceId = "physical-id",
                PhysicalInterfaceAlias = "Кабель",
                PhysicalIpv4 = "192.168.0.20",
                PhysicalGateway = "192.168.0.1",
                OriginalDnsServers = ["192.168.0.1"],
                OriginalDnsWasAutomatic = true,
                AppliedDnsServers = ["1.1.1.1", "1.0.0.1"],
                TunInterfaceIndex = 42,
                TunInterfaceId = "tun-id",
                TunInterfaceAlias =
                    WindowsTunNetworkService.XrayTunInterfaceName,
                TunIpv4 = "169.254.167.8",
                ProxyEndpointIps = ["203.0.113.10"],
                AppliedRoutes =
                [
                    new TunAppliedRoute
                    {
                        DestinationPrefix = "203.0.113.10/32",
                        InterfaceIndex = 7,
                        NextHop = "192.168.0.1"
                    },
                    new TunAppliedRoute
                    {
                        DestinationPrefix = "0.0.0.0/1",
                        InterfaceIndex = 42,
                        NextHop = "169.254.167.8"
                    },
                    new TunAppliedRoute
                    {
                        DestinationPrefix = "128.0.0.0/1",
                        InterfaceIndex = 42,
                        NextHop = "169.254.167.8"
                    }
                ]
            };
        }

        private static void ParseConnectionTrace()
        {
            const string trace =
                "fl=29f\n" +
                "ip=203.0.113.10\n" +
                "http=h2\n";

            AssertEqual(
                "203.0.113.10",
                ConnectionTestService.ParseTraceValue(trace, "ip")
            );
            AssertEqual(
                "h2",
                ConnectionTestService.ParseTraceValue(trace, "http")
            );
        }

        private static void FormatPrivacyReport()
        {
            var result = new ConnectionTestResult
            {
                InitialRequest = new ConnectionTestRequestResult(
                    true,
                    200,
                    100,
                    new Version(2, 0),
                    string.Empty
                ),
                ParallelRequests =
                [
                    new ConnectionTestRequestResult(
                        true,
                        200,
                        110,
                        new Version(2, 0),
                        string.Empty
                    )
                ],
                ExitIp = "203.0.113.10",
                CloudflareProtocol = "h2",
                Ipv6Privacy = new Ipv6PrivacyProbeResult(
                    true,
                    false,
                    false,
                    string.Empty,
                    "IPv6 endpoint недоступен."
                ),
                DnsInterfaceSummary = "GeniaProxy=[1.1.1.1]"
            };

            string report = result.FormatReport(
                "Test",
                ConnectionMode.Tun,
                2080
            );

            AssertEqual(true, result.IsSuccess);
            AssertEqual(
                true,
                report.Contains(
                    "IPv6 leak probe: PASS",
                    StringComparison.Ordinal
                )
            );
            AssertEqual(
                true,
                report.Contains(
                    "DNS-интерфейсы Windows",
                    StringComparison.Ordinal
                )
            );
        }

        private static string CreateTestDirectory()
        {
            return Path.Combine(
                Path.GetTempPath(),
                "GeniaProxy.Tests",
                Guid.NewGuid().ToString("N")
            );
        }

        private static void DeleteTestDirectory(
            string directory)
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        private static void AssertEqual<T>(
            T expected,
            T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(
                    expected,
                    actual))
            {
                throw new Exception(
                    $"Ожидалось «{expected}», " +
                    $"получено «{actual}»."
                );
            }
        }

        private static void AssertNotNull(object? value)
        {
            if (value is null)
            {
                throw new Exception(
                    "Ожидалось непустое значение."
                );
            }
        }

        private static void AssertThrows<TException>(
            Action action)
            where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }

            throw new Exception(
                $"Ожидалось исключение " +
                $"{typeof(TException).Name}."
            );
        }
    }
}

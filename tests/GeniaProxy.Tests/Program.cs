using System.IO.Pipes;
using System.Net;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json.Nodes;
using GeniaProxy.ControlPlane;
using GeniaProxy.Models;
using GeniaProxy.Services;

namespace GeniaProxy.Tests
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (TryRunProtocolLabProbe(args, out int probeExitCode))
            {
                return probeExitCode;
            }

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
                ("Отчёт privacy probe", FormatPrivacyReport),
                ("Control Plane lifecycle", ValidateControlPlaneLifecycle),
                ("Control Plane session isolation", ValidateControlPlaneSessionIsolation),
                ("Control Plane stale session guard", ValidateControlPlaneStaleSessionGuard),
                ("Control Plane verification refresh", ValidateControlPlaneVerificationRefresh),
                ("Control Plane refresh stale-session guard", ValidateControlPlaneRefreshStaleSessionGuard),
                ("Control Plane transition guard", ValidateControlPlaneTransitionGuard),
                ("Startup journal live-readable", ValidateStartupJournalLiveReadable),
                ("Single-instance activation ACK", ValidateSingleInstanceActivationAck),
                ("Single-instance UAC pipe security", ValidateSingleInstanceActivationPipeSecurity),
                ("Protocol Lab Alpha 1 boundary", ValidateProtocolLabBoundary),
                ("Protocol Lab Alpha 2 capability model", ValidateProtocolLabCapabilityModel),
                ("Protocol Lab AnyTLS selection gate", ValidateProtocolLabAnyTlsSelectionGate),
                ("Protocol Lab TUIC selection gate", ValidateProtocolLabTuicSelectionGate),
                ("Protocol Lab AnyTLS config", ValidateProtocolLabAnyTlsConfig),
                ("Protocol Lab AnyTLS validation", ValidateProtocolLabAnyTlsValidation),
                ("Protocol Lab AnyTLS trusted certificate", ValidateProtocolLabAnyTlsTrustedCertificate),
                ("Protocol Lab TUIC config", ValidateProtocolLabTuicConfig),
                ("Protocol Lab TUIC validation", ValidateProtocolLabTuicValidation),
                ("Protocol Lab Snell v6 config", ValidateProtocolLabSnellConfig),
                ("Protocol Lab Snell v6 validation", ValidateProtocolLabSnellValidation)
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

        private static bool TryRunProtocolLabProbe(
            string[] args,
            out int exitCode)
        {
            exitCode = 0;

            if (args.Length == 0)
            {
                return false;
            }

            try
            {
                string json;
                string outputPath;

                if (args.Length == 2 &&
                    args[0].Equals(
                        "--write-anytls-config",
                        StringComparison.Ordinal))
                {
                    outputPath = args[1];
                    json =
                        ProtocolLabAnyTlsConfigService
                            .CreateLocalProxyConfig(
                                "example.com",
                                443,
                                "protocol-lab-test",
                                "example.com",
                                2080
                            );
                }
                else if (args.Length == 5 &&
                         args[0].Equals(
                             "--write-anytls-runtime-config",
                             StringComparison.Ordinal))
                {
                    outputPath = args[1];

                    if (!int.TryParse(args[2], out int serverPort) ||
                        !int.TryParse(args[3], out int localPort))
                    {
                        throw new FormatException(
                            "Runtime probe ports must be integers."
                        );
                    }

                    json =
                        ProtocolLabAnyTlsConfigService
                            .CreateLocalProxyConfig(
                                "127.0.0.1",
                                serverPort,
                                "protocol-lab-loopback-secret",
                                "localhost",
                                localPort,
                                allowInsecureTls: false,
                                trustedCertificatePath: args[4]
                            );
                }
                else if (args.Length == 2 &&
                         args[0].Equals(
                             "--write-tuic-config",
                             StringComparison.Ordinal))
                {
                    outputPath = args[1];

                    json =
                        ProtocolLabTuicConfigService
                            .CreateLocalProxyConfig(
                                "example.com",
                                443,
                                "11111111-2222-3333-4444-555555555555",
                                "protocol-lab-test",
                                "example.com",
                                2080
                            );
                }
                else if (args.Length == 5 &&
                         args[0].Equals(
                             "--write-tuic-runtime-config",
                             StringComparison.Ordinal))
                {
                    outputPath = args[1];

                    if (!int.TryParse(args[2], out int serverPort) ||
                        !int.TryParse(args[3], out int localPort))
                    {
                        throw new FormatException(
                            "Runtime probe ports must be integers."
                        );
                    }

                    json =
                        ProtocolLabTuicConfigService
                            .CreateLocalProxyConfig(
                                "127.0.0.1",
                                serverPort,
                                "11111111-2222-3333-4444-555555555555",
                                "protocol-lab-loopback-secret",
                                "localhost",
                                localPort,
                                allowInsecureTls: false,
                                trustedCertificatePath: args[4]
                            );
                }
                else if (args.Length == 2 &&
                         args[0].Equals(
                             "--write-snell-config",
                             StringComparison.Ordinal))
                {
                    outputPath = args[1];

                    json =
                        ProtocolLabSnellConfigService
                            .CreateLocalProxyConfig(
                                "example.com",
                                443,
                                "protocol-lab-snell-psk",
                                2080
                            );
                }
                else if (args.Length == 4 &&
                         args[0].Equals(
                             "--write-snell-runtime-config",
                             StringComparison.Ordinal))
                {
                    outputPath = args[1];

                    if (!int.TryParse(args[2], out int serverPort) ||
                        !int.TryParse(args[3], out int localPort))
                    {
                        throw new FormatException(
                            "Runtime probe ports must be integers."
                        );
                    }

                    json =
                        ProtocolLabSnellConfigService
                            .CreateLocalProxyConfig(
                                "127.0.0.1",
                                serverPort,
                                "protocol-lab-snell-psk",
                                localPort
                            );
                }
                else
                {
                    Console.Error.WriteLine(
                        "Неизвестный режим тестового probe."
                    );
                    exitCode = 2;
                    return true;
                }

                File.WriteAllText(
                    outputPath,
                    json,
                    new UTF8Encoding(
                        encoderShouldEmitUTF8Identifier: false
                    )
                );

                Console.WriteLine(
                    "Protocol Lab config written: " +
                    Path.GetFullPath(outputPath)
                );

                return true;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                exitCode = 1;
                return true;
            }
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
            AssertEqual("5.6.0.7", BrowserIntegrationService.SwitcherManifestVersion);
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

        private static void ValidateControlPlaneLifecycle()
        {
            string directory = CreateTestDirectory();
            string journalPath = Path.Combine(
                directory,
                "session-journal.jsonl"
            );

            try
            {
                using var coordinator = new ControlPlaneCoordinator(
                    journalPath,
                    TimeSpan.FromMinutes(5)
                );

                coordinator.BeginSessionAsync(
                    "alpha-profile",
                    "tun"
                ).GetAwaiter().GetResult();

                AssertEqual(
                    ConnectionState.Connecting,
                    coordinator.Snapshot.State
                );

                coordinator.MarkNetworkReadyAsync(
                    "xray",
                    "tun",
                    tunMode: true
                ).GetAwaiter().GetResult();

                AssertEqual(
                    ConnectionState.TunWarmup,
                    coordinator.Snapshot.State
                );

                coordinator.MarkVerificationStartedAsync(
                    "manager-channel-test"
                ).GetAwaiter().GetResult();

                coordinator.MarkVerifiedAsync(
                    "203.0.113.10",
                    expectedExit: null,
                    source: "manager-channel-test"
                ).GetAwaiter().GetResult();

                ControlPlaneSnapshot verified = coordinator.Snapshot;
                AssertEqual(ConnectionState.Verified, verified.State);
                AssertEqual("203.0.113.10", verified.VerifiedExit);
                AssertEqual(true, verified.VerificationSucceeded);
                AssertEqual(true, verified.VerificationFresh);

                coordinator.BeginDisconnectAsync(
                    "test-disconnect"
                ).GetAwaiter().GetResult();
                coordinator.CompleteDisconnectAsync(
                    "test-disconnect-complete"
                ).GetAwaiter().GetResult();

                AssertEqual(
                    ConnectionState.Idle,
                    coordinator.Snapshot.State
                );

                string journal = File.ReadAllText(journalPath);
                AssertEqual(
                    true,
                    journal.Contains(
                        "sessionStarted",
                        StringComparison.OrdinalIgnoreCase
                    )
                );
                AssertEqual(
                    true,
                    journal.Contains(
                        "verificationSucceeded",
                        StringComparison.OrdinalIgnoreCase
                    )
                );
                AssertEqual(
                    true,
                    journal.Contains(
                        "sessionStopped",
                        StringComparison.OrdinalIgnoreCase
                    )
                );
            }
            finally
            {
                DeleteTestDirectory(directory);
            }
        }

        private static void ValidateControlPlaneSessionIsolation()
        {
            string directory = CreateTestDirectory();
            string journalPath = Path.Combine(
                directory,
                "session-journal.jsonl"
            );

            try
            {
                using var coordinator = new ControlPlaneCoordinator(
                    journalPath
                );

                coordinator.BeginSessionAsync(
                    "profile-one",
                    "local"
                ).GetAwaiter().GetResult();
                coordinator.MarkNetworkReadyAsync(
                    "sing-box",
                    "local",
                    tunMode: false
                ).GetAwaiter().GetResult();
                coordinator.MarkVerifiedAsync(
                    "198.51.100.20",
                    expectedExit: null,
                    source: "manager-channel-test"
                ).GetAwaiter().GetResult();

                Guid firstSession = coordinator.Snapshot.SessionId;

                coordinator.CompleteDisconnectAsync(
                    "first-session-complete"
                ).GetAwaiter().GetResult();

                coordinator.BeginSessionAsync(
                    "profile-two",
                    "local"
                ).GetAwaiter().GetResult();

                ControlPlaneSnapshot second = coordinator.Snapshot;
                AssertEqual(false, firstSession == second.SessionId);
                AssertEqual<string?>(null, second.VerifiedExit);
                AssertEqual<DateTimeOffset?>(null, second.VerifiedAtUtc);
                AssertEqual<bool?>(null, second.VerificationSucceeded);
                AssertEqual(false, second.VerificationFresh);
            }
            finally
            {
                DeleteTestDirectory(directory);
            }
        }

        private static void ValidateControlPlaneStaleSessionGuard()
        {
            string directory = CreateTestDirectory();
            string journalPath = Path.Combine(
                directory,
                "session-journal.jsonl"
            );

            try
            {
                using var coordinator = new ControlPlaneCoordinator(
                    journalPath
                );

                coordinator.BeginSessionAsync(
                    "old-session",
                    "tun"
                ).GetAwaiter().GetResult();
                coordinator.MarkNetworkReadyAsync(
                    "sing-box",
                    "tun",
                    tunMode: true
                ).GetAwaiter().GetResult();

                Guid staleSessionId = coordinator.Snapshot.SessionId;

                coordinator.BeginSessionAsync(
                    "new-session",
                    "tun"
                ).GetAwaiter().GetResult();
                coordinator.MarkNetworkReadyAsync(
                    "sing-box",
                    "tun",
                    tunMode: true
                ).GetAwaiter().GetResult();

                Guid currentSessionId = coordinator.Snapshot.SessionId;
                AssertEqual(false, staleSessionId == currentSessionId);

                bool staleStarted = coordinator
                    .MarkVerificationStartedAsync(
                        staleSessionId,
                        "manager-tun-auto"
                    ).GetAwaiter().GetResult();
                bool staleApplied = coordinator
                    .MarkVerifiedAsync(
                        staleSessionId,
                        "192.0.2.90",
                        expectedExit: null,
                        source: "manager-tun-auto"
                    ).GetAwaiter().GetResult();

                AssertEqual(false, staleStarted);
                AssertEqual(false, staleApplied);
                AssertEqual(
                    ConnectionState.TunWarmup,
                    coordinator.Snapshot.State
                );
                AssertEqual<string?>(
                    null,
                    coordinator.Snapshot.VerifiedExit
                );

                bool currentStarted = coordinator
                    .MarkVerificationStartedAsync(
                        currentSessionId,
                        "manager-tun-auto"
                    ).GetAwaiter().GetResult();
                bool currentApplied = coordinator
                    .MarkVerifiedAsync(
                        currentSessionId,
                        "198.51.100.77",
                        expectedExit: null,
                        source: "manager-tun-auto"
                    ).GetAwaiter().GetResult();

                AssertEqual(true, currentStarted);
                AssertEqual(true, currentApplied);
                AssertEqual(
                    ConnectionState.Verified,
                    coordinator.Snapshot.State
                );
                AssertEqual(
                    "198.51.100.77",
                    coordinator.Snapshot.VerifiedExit
                );
                AssertEqual(
                    "manager-tun-auto",
                    coordinator.Snapshot.VerificationSource
                );
            }
            finally
            {
                DeleteTestDirectory(directory);
            }
        }

        private static void ValidateControlPlaneVerificationRefresh()
        {
            string directory = CreateTestDirectory();
            string journalPath = Path.Combine(
                directory,
                "session-journal.jsonl"
            );

            try
            {
                using var coordinator = new ControlPlaneCoordinator(
                    journalPath,
                    TimeSpan.FromMinutes(5)
                );

                coordinator.BeginSessionAsync(
                    "refresh-profile",
                    "tun"
                ).GetAwaiter().GetResult();
                coordinator.MarkNetworkReadyAsync(
                    "sing-box",
                    "tun",
                    tunMode: true
                ).GetAwaiter().GetResult();

                Guid sessionId = coordinator.Snapshot.SessionId;
                bool automaticApplied = coordinator.MarkVerifiedAsync(
                    sessionId,
                    "198.51.100.77",
                    expectedExit: null,
                    source: "manager-tun-auto"
                ).GetAwaiter().GetResult();

                AssertEqual(true, automaticApplied);
                ControlPlaneSnapshot automatic = coordinator.Snapshot;
                AssertEqual(ConnectionState.Verified, automatic.State);
                DateTimeOffset verifiedStateSince = automatic.StateSinceUtc;
                DateTimeOffset firstVerifiedAt = automatic.VerifiedAtUtc!.Value;

                Thread.Sleep(10);

                bool started = coordinator.MarkVerificationStartedAsync(
                    sessionId,
                    "manager-channel-test"
                ).GetAwaiter().GetResult();
                bool refreshed = coordinator.MarkVerifiedAsync(
                    sessionId,
                    "198.51.100.88",
                    expectedExit: null,
                    source: "manager-channel-test"
                ).GetAwaiter().GetResult();

                ControlPlaneSnapshot snapshot = coordinator.Snapshot;
                AssertEqual(true, started);
                AssertEqual(true, refreshed);
                AssertEqual(sessionId, snapshot.SessionId);
                AssertEqual(ConnectionState.Verified, snapshot.State);
                AssertEqual(verifiedStateSince, snapshot.StateSinceUtc);
                AssertEqual("198.51.100.88", snapshot.VerifiedExit);
                AssertEqual("manager-channel-test", snapshot.VerificationSource);
                AssertEqual(true, snapshot.VerifiedAtUtc > firstVerifiedAt);
                AssertEqual(true, snapshot.VerificationFresh);

                string journal = File.ReadAllText(journalPath);
                AssertEqual(
                    true,
                    journal.Contains(
                        "verificationRefreshed",
                        StringComparison.OrdinalIgnoreCase
                    )
                );
            }
            finally
            {
                DeleteTestDirectory(directory);
            }
        }

        private static void ValidateControlPlaneRefreshStaleSessionGuard()
        {
            string directory = CreateTestDirectory();
            string journalPath = Path.Combine(
                directory,
                "session-journal.jsonl"
            );

            try
            {
                using var coordinator = new ControlPlaneCoordinator(journalPath);

                coordinator.BeginSessionAsync(
                    "old-refresh-session",
                    "tun"
                ).GetAwaiter().GetResult();
                coordinator.MarkNetworkReadyAsync(
                    "sing-box",
                    "tun",
                    tunMode: true
                ).GetAwaiter().GetResult();
                Guid staleSessionId = coordinator.Snapshot.SessionId;
                coordinator.MarkVerifiedAsync(
                    staleSessionId,
                    "192.0.2.10",
                    expectedExit: null,
                    source: "manager-tun-auto"
                ).GetAwaiter().GetResult();

                coordinator.BeginSessionAsync(
                    "new-refresh-session",
                    "tun"
                ).GetAwaiter().GetResult();
                coordinator.MarkNetworkReadyAsync(
                    "sing-box",
                    "tun",
                    tunMode: true
                ).GetAwaiter().GetResult();
                Guid currentSessionId = coordinator.Snapshot.SessionId;
                coordinator.MarkVerifiedAsync(
                    currentSessionId,
                    "198.51.100.40",
                    expectedExit: null,
                    source: "manager-tun-auto"
                ).GetAwaiter().GetResult();

                DateTimeOffset? currentVerifiedAt = coordinator.Snapshot.VerifiedAtUtc;
                bool staleRefresh = coordinator.MarkVerifiedAsync(
                    staleSessionId,
                    "203.0.113.99",
                    expectedExit: null,
                    source: "manager-channel-test"
                ).GetAwaiter().GetResult();

                AssertEqual(false, staleRefresh);
                AssertEqual(currentSessionId, coordinator.Snapshot.SessionId);
                AssertEqual("198.51.100.40", coordinator.Snapshot.VerifiedExit);
                AssertEqual(currentVerifiedAt, coordinator.Snapshot.VerifiedAtUtc);
                AssertEqual("manager-tun-auto", coordinator.Snapshot.VerificationSource);
            }
            finally
            {
                DeleteTestDirectory(directory);
            }
        }

        private static void ValidateControlPlaneTransitionGuard()
        {
            string directory = CreateTestDirectory();
            string journalPath = Path.Combine(
                directory,
                "session-journal.jsonl"
            );

            try
            {
                using var coordinator = new ControlPlaneCoordinator(
                    journalPath
                );

                coordinator.BeginSessionAsync(
                    "guard-profile",
                    "local"
                ).GetAwaiter().GetResult();

                AssertThrows<InvalidOperationException>(() =>
                    coordinator.MarkVerifiedAsync(
                        "192.0.2.44",
                        expectedExit: null,
                        source: "invalid-early-verification"
                    ).GetAwaiter().GetResult()
                );

                AssertEqual(
                    ConnectionState.Connecting,
                    coordinator.Snapshot.State
                );
                AssertEqual<string?>(
                    null,
                    coordinator.Snapshot.VerifiedExit
                );
            }
            finally
            {
                DeleteTestDirectory(directory);
            }
        }


        private static void ValidateStartupJournalLiveReadable()
        {
            string directory = CreateTestDirectory();
            string journalPath = Path.Combine(
                directory,
                "startup-journal.jsonl"
            );

            try
            {
                var journal = new StartupJournal(journalPath);
                Guid attemptId = Guid.NewGuid();

                AssertEqual(
                    true,
                    journal.TryAppend(
                        "startup.test",
                        attemptId,
                        new Dictionary<string, object?>
                        {
                            ["phase"] = "unit"
                        }
                    )
                );

                using FileStream reader = new(
                    journalPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete
                );

                AssertEqual(
                    true,
                    journal.TryAppend(
                        "startup.test.second",
                        attemptId
                    )
                );

                reader.Position = 0;
                using var textReader = new StreamReader(
                    reader,
                    Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: true,
                    bufferSize: 1024,
                    leaveOpen: true
                );

                string content = textReader.ReadToEnd();

                AssertEqual(
                    true,
                    content.Contains(
                        "startup.test",
                        StringComparison.Ordinal
                    )
                );
            }
            finally
            {
                DeleteTestDirectory(directory);
            }
        }

        private static void ValidateSingleInstanceActivationAck()
        {
            string applicationId =
                "GeniaProxy.Tests." + Guid.NewGuid().ToString("N");

            using var primary = new SingleInstanceService(applicationId);

            AssertEqual(true, primary.IsPrimaryInstance);

            using var activated = new ManualResetEventSlim(false);

            primary.ActivationRequested +=
                (_, _) => activated.Set();

            primary.StartListening();

            Thread.Sleep(100);

            using var secondary = new SingleInstanceService(applicationId);

            AssertEqual(false, secondary.IsPrimaryInstance);

            bool acknowledged = secondary.TryActivatePrimary(
                TimeSpan.FromSeconds(2),
                out string? errorMessage
            );

            AssertEqual(true, acknowledged);
            AssertEqual<string?>(null, errorMessage);
            AssertEqual(
                true,
                activated.Wait(TimeSpan.FromSeconds(2))
            );
        }

        private static void ValidateSingleInstanceActivationPipeSecurity()
        {
            MethodInfo? method = typeof(SingleInstanceService)
                .GetMethod(
                    "CreateActivationPipeSecurity",
                    BindingFlags.NonPublic | BindingFlags.Static
                );

            AssertNotNull(method);

            var security = method!.Invoke(null, null) as PipeSecurity;

            AssertNotNull(security);
            AssertEqual(true, security!.AreAccessRulesProtected);

            SecurityIdentifier currentUser =
                WindowsIdentity.GetCurrent().User
                ?? throw new Exception(
                    "Не удалось определить SID текущего пользователя."
                );

            AuthorizationRuleCollection rules =
                security.GetAccessRules(
                    includeExplicit: true,
                    includeInherited: false,
                    targetType: typeof(SecurityIdentifier)
                );

            PipeAccessRule[] allowRules = rules
                .Cast<PipeAccessRule>()
                .Where(rule =>
                    rule.AccessControlType ==
                        AccessControlType.Allow)
                .ToArray();

            AssertEqual(1, allowRules.Length);
            AssertEqual(
                currentUser.Value,
                ((SecurityIdentifier)allowRules[0]
                    .IdentityReference).Value
            );

            PipeAccessRights rights =
                allowRules[0].PipeAccessRights;

            AssertEqual(
                true,
                (rights & PipeAccessRights.ReadWrite) ==
                    PipeAccessRights.ReadWrite
            );

            AssertEqual(
                true,
                (rights & PipeAccessRights.ChangePermissions) != 0
            );

            AssertEqual(
                true,
                (rights & PipeAccessRights.TakeOwnership) != 0
            );
        }

        private static void ValidateProtocolLabBoundary()
        {
            ProtocolLabFeatureCatalog.ThrowIfAlpha1BoundaryViolated();

            AssertEqual(
                true,
                ProtocolLabFeatureCatalog.Alpha1BoundaryIsSafe
            );

            string[] required =
            [
                "anytls",
                "tuic",
                "snell",
                "whitelist-mode",
                "xray-experimental"
            ];

            foreach (string id in required)
            {
                FeatureCapability? capability =
                    ProtocolLabFeatureCatalog.All
                        .FirstOrDefault(item =>
                            item.Id.Equals(
                                id,
                                StringComparison.Ordinal
                            ));

                AssertNotNull(capability);
                AssertEqual(
                    FeatureLane.ProtocolLab,
                    capability!.Lane
                );
                AssertEqual(false, capability.EnabledByDefault);
            }
        }

        private static void ValidateProtocolLabCapabilityModel()
        {
            ProtocolLabFeatureCatalog.ThrowIfDefaultBoundaryViolated();

            AssertEqual(
                true,
                ProtocolLabFeatureCatalog.DefaultBoundaryIsSafe
            );

            FeatureCapability anyTls =
                ProtocolLabFeatureCatalog.Find("anytls")
                ?? throw new Exception("AnyTLS capability отсутствует.");

            AssertEqual(
                ProtocolLabEngineFamily.SingBox,
                anyTls.EngineFamily
            );
            AssertEqual(
                ProtocolLabSupportState.RuntimeVerified,
                anyTls.SupportState
            );
            AssertEqual(true, anyTls.SelectableInProtocolLab);

            FeatureCapability tuic =
                ProtocolLabFeatureCatalog.Find("TUIC")
                ?? throw new Exception("TUIC capability отсутствует.");

            AssertEqual(
                ProtocolLabEngineFamily.SingBox,
                tuic.EngineFamily
            );
            AssertEqual(
                ProtocolLabSupportState.RuntimeVerified,
                tuic.SupportState
            );
            AssertEqual(true, tuic.SelectableInProtocolLab);

            FeatureCapability snell =
                ProtocolLabFeatureCatalog.Find("snell")
                ?? throw new Exception("Snell capability отсутствует.");

            AssertEqual(
                ProtocolLabEngineFamily.SingBox,
                snell.EngineFamily
            );
            AssertEqual(
                ProtocolLabSupportState.EngineAvailable,
                snell.SupportState
            );
            AssertEqual(false, snell.SelectableInProtocolLab);

            FeatureCapability whitelist =
                ProtocolLabFeatureCatalog.Find("whitelist-mode")
                ?? throw new Exception(
                    "Whitelist capability отсутствует."
                );

            AssertEqual(
                ProtocolLabEngineFamily.Host,
                whitelist.EngineFamily
            );
            AssertEqual(
                ProtocolLabSupportState.DesignOnly,
                whitelist.SupportState
            );
            AssertEqual(false, whitelist.SelectableInProtocolLab);

            FeatureCapability xrayExperimental =
                ProtocolLabFeatureCatalog.Find("xray-experimental")
                ?? throw new Exception(
                    "Xray experimental capability отсутствует."
                );

            AssertEqual(
                ProtocolLabEngineFamily.Xray,
                xrayExperimental.EngineFamily
            );
            AssertEqual(
                ProtocolLabSupportState.DesignOnly,
                xrayExperimental.SupportState
            );
            AssertEqual(
                false,
                xrayExperimental.SelectableInProtocolLab
            );

            AssertEqual<FeatureCapability?>(
                null,
                ProtocolLabFeatureCatalog.Find("unknown")
            );

            AssertThrows<NotSupportedException>(() =>
                ProtocolLabFeatureCatalog.RequireSelectable("snell")
            );

            AssertThrows<NotSupportedException>(() =>
                ProtocolLabFeatureCatalog.RequireSelectable("unknown")
            );
        }

        private static void ValidateProtocolLabAnyTlsSelectionGate()
        {
            FeatureCapability capability =
                ProtocolLabFeatureCatalog.RequireSelectable("ANYTLS");

            AssertEqual("anytls", capability.Id);
            AssertEqual(
                FeatureLane.ProtocolLab,
                capability.Lane
            );
            AssertEqual(
                ProtocolLabEngineFamily.SingBox,
                capability.EngineFamily
            );
            AssertEqual(
                ProtocolLabSupportState.RuntimeVerified,
                capability.SupportState
            );
            AssertEqual(false, capability.EnabledByDefault);
            AssertEqual(true, capability.SelectableInProtocolLab);

            AssertThrows<NotSupportedException>(() =>
                ProtocolLabFeatureCatalog.RequireSelectable("snell")
            );

            AssertThrows<NotSupportedException>(() =>
                ProtocolLabFeatureCatalog.RequireSelectable(
                    "whitelist-mode"
                )
            );

            AssertThrows<NotSupportedException>(() =>
                ProtocolLabFeatureCatalog.RequireSelectable(
                    "xray-experimental"
                )
            );
        }

        private static void ValidateProtocolLabTuicSelectionGate()
        {
            FeatureCapability capability =
                ProtocolLabFeatureCatalog.RequireSelectable("TUIC");

            AssertEqual("tuic", capability.Id);
            AssertEqual(
                FeatureLane.ProtocolLab,
                capability.Lane
            );
            AssertEqual(
                ProtocolLabEngineFamily.SingBox,
                capability.EngineFamily
            );
            AssertEqual(
                ProtocolLabSupportState.RuntimeVerified,
                capability.SupportState
            );
            AssertEqual(false, capability.EnabledByDefault);
            AssertEqual(true, capability.SelectableInProtocolLab);

            AssertThrows<NotSupportedException>(() =>
                ProtocolLabFeatureCatalog.RequireSelectable("snell")
            );

            AssertThrows<NotSupportedException>(() =>
                ProtocolLabFeatureCatalog.RequireSelectable(
                    "whitelist-mode"
                )
            );

            AssertThrows<NotSupportedException>(() =>
                ProtocolLabFeatureCatalog.RequireSelectable(
                    "xray-experimental"
                )
            );
        }

        private static void ValidateProtocolLabAnyTlsConfig()
        {
            string json =
                ProtocolLabAnyTlsConfigService.CreateLocalProxyConfig(
                    "edge.example.com",
                    443,
                    "secret",
                    "tls.example.com",
                    2080
                );

            JsonObject root =
                JsonNode.Parse(json)?.AsObject()
                ?? throw new Exception(
                    "Не создан AnyTLS JSON."
                );

            JsonObject inbound =
                root["inbounds"]?[0]?.AsObject()
                ?? throw new Exception(
                    "Не создан AnyTLS mixed inbound."
                );

            AssertEqual(
                "127.0.0.1",
                inbound["listen"]?.GetValue<string>()
            );
            AssertEqual(
                2080,
                inbound["listen_port"]?.GetValue<int>()
            );

            JsonObject outbound =
                root["outbounds"]?[0]?.AsObject()
                ?? throw new Exception(
                    "Не создан AnyTLS outbound."
                );

            AssertEqual(
                "anytls",
                outbound["type"]?.GetValue<string>()
            );
            AssertEqual(
                "edge.example.com",
                outbound["server"]?.GetValue<string>()
            );
            AssertEqual(
                443,
                outbound["server_port"]?.GetValue<int>()
            );
            AssertEqual(
                "secret",
                outbound["password"]?.GetValue<string>()
            );
            AssertEqual(
                string.Empty,
                outbound["client_metadata"]?.GetValue<string>()
            );
            AssertEqual(
                true,
                outbound["tls"]?["enabled"]?.GetValue<bool>()
            );
            AssertEqual(
                "tls.example.com",
                outbound["tls"]?["server_name"]
                    ?.GetValue<string>()
            );
            AssertEqual(
                false,
                outbound["tls"]?["insecure"]?.GetValue<bool>()
            );
            AssertEqual(
                "proxy",
                root["route"]?["final"]?.GetValue<string>()
            );
        }

        private static void ValidateProtocolLabAnyTlsValidation()
        {
            AssertThrows<ArgumentException>(() =>
                ProtocolLabAnyTlsConfigService.CreateLocalProxyConfig(
                    "",
                    443,
                    "secret",
                    null,
                    2080
                )
            );

            AssertThrows<ArgumentException>(() =>
                ProtocolLabAnyTlsConfigService.CreateLocalProxyConfig(
                    "edge.example.com",
                    443,
                    "",
                    null,
                    2080
                )
            );

            AssertThrows<ArgumentOutOfRangeException>(() =>
                ProtocolLabAnyTlsConfigService.CreateLocalProxyConfig(
                    "edge.example.com",
                    0,
                    "secret",
                    null,
                    2080
                )
            );

            AssertThrows<ArgumentOutOfRangeException>(() =>
                ProtocolLabAnyTlsConfigService.CreateLocalProxyConfig(
                    "edge.example.com",
                    443,
                    "secret",
                    null,
                    70000
                )
            );

            AssertThrows<FormatException>(() =>
                ProtocolLabAnyTlsConfigService.CreateLocalProxyConfig(
                    "edge.example.com\ninvalid",
                    443,
                    "secret",
                    null,
                    2080
                )
            );

            string insecureJson =
                ProtocolLabAnyTlsConfigService.CreateLocalProxyConfig(
                    "edge.example.com",
                    443,
                    "secret",
                    null,
                    2080,
                    allowInsecureTls: true
                );

            JsonObject insecureRoot =
                JsonNode.Parse(insecureJson)!.AsObject();

            AssertEqual(
                "edge.example.com",
                insecureRoot["outbounds"]?[0]?["tls"]?
                    ["server_name"]?.GetValue<string>()
            );
            AssertEqual(
                true,
                insecureRoot["outbounds"]?[0]?["tls"]?
                    ["insecure"]?.GetValue<bool>()
            );
        }

        private static void ValidateProtocolLabAnyTlsTrustedCertificate()
        {
            string json =
                ProtocolLabAnyTlsConfigService.CreateLocalProxyConfig(
                    "127.0.0.1",
                    4443,
                    "secret",
                    "localhost",
                    2080,
                    allowInsecureTls: false,
                    trustedCertificatePath:
                        @"C:\ProtocolLab\loopback-cert.pem"
                );

            JsonObject root =
                JsonNode.Parse(json)!.AsObject();

            AssertEqual(
                false,
                root["outbounds"]?[0]?["tls"]?
                    ["insecure"]?.GetValue<bool>()
            );

            AssertEqual(
                @"C:\ProtocolLab\loopback-cert.pem",
                root["outbounds"]?[0]?["tls"]?
                    ["certificate_path"]?.GetValue<string>()
            );

            AssertThrows<FormatException>(() =>
                ProtocolLabAnyTlsConfigService.CreateLocalProxyConfig(
                    "127.0.0.1",
                    4443,
                    "secret",
                    "localhost",
                    2080,
                    allowInsecureTls: false,
                    trustedCertificatePath:
                        "C:\\ProtocolLab\\bad\ncert.pem"
                )
            );
        }

        private static void ValidateProtocolLabTuicConfig()
        {
            string json =
                ProtocolLabTuicConfigService.CreateLocalProxyConfig(
                    "edge.example.com",
                    443,
                    "11111111-2222-3333-4444-555555555555",
                    "secret",
                    "tls.example.com",
                    2080
                );

            JsonObject root =
                JsonNode.Parse(json)?.AsObject()
                ?? throw new Exception(
                    "Не создан TUIC JSON."
                );

            JsonObject outbound =
                root["outbounds"]?[0]?.AsObject()
                ?? throw new Exception(
                    "Не создан TUIC outbound."
                );

            AssertEqual(
                "tuic",
                outbound["type"]?.GetValue<string>()
            );
            AssertEqual(
                "edge.example.com",
                outbound["server"]?.GetValue<string>()
            );
            AssertEqual(
                443,
                outbound["server_port"]?.GetValue<int>()
            );
            AssertEqual(
                "11111111-2222-3333-4444-555555555555",
                outbound["uuid"]?.GetValue<string>()
            );
            AssertEqual(
                "secret",
                outbound["password"]?.GetValue<string>()
            );
            AssertEqual(
                "cubic",
                outbound["congestion_control"]?.GetValue<string>()
            );
            AssertEqual(
                "native",
                outbound["udp_relay_mode"]?.GetValue<string>()
            );
            AssertEqual(
                false,
                outbound["zero_rtt_handshake"]?.GetValue<bool>()
            );
            AssertEqual(
                "10s",
                outbound["heartbeat"]?.GetValue<string>()
            );
            AssertEqual(
                true,
                outbound["tls"]?["enabled"]?.GetValue<bool>()
            );
            AssertEqual(
                "tls.example.com",
                outbound["tls"]?["server_name"]
                    ?.GetValue<string>()
            );
            AssertEqual(
                false,
                outbound["tls"]?["insecure"]?.GetValue<bool>()
            );
        }

        private static void ValidateProtocolLabTuicValidation()
        {
            AssertThrows<FormatException>(() =>
                ProtocolLabTuicConfigService.CreateLocalProxyConfig(
                    "edge.example.com",
                    443,
                    "not-a-uuid",
                    "secret",
                    null,
                    2080
                )
            );

            AssertThrows<ArgumentException>(() =>
                ProtocolLabTuicConfigService.CreateLocalProxyConfig(
                    "edge.example.com",
                    443,
                    "11111111-2222-3333-4444-555555555555",
                    "",
                    null,
                    2080
                )
            );

            AssertThrows<NotSupportedException>(() =>
                ProtocolLabTuicConfigService.CreateLocalProxyConfig(
                    "edge.example.com",
                    443,
                    "11111111-2222-3333-4444-555555555555",
                    "secret",
                    null,
                    2080,
                    congestionControl: "invalid"
                )
            );

            AssertThrows<NotSupportedException>(() =>
                ProtocolLabTuicConfigService.CreateLocalProxyConfig(
                    "edge.example.com",
                    443,
                    "11111111-2222-3333-4444-555555555555",
                    "secret",
                    null,
                    2080,
                    udpRelayMode: "invalid"
                )
            );

            string json =
                ProtocolLabTuicConfigService.CreateLocalProxyConfig(
                    "127.0.0.1",
                    4443,
                    "11111111-2222-3333-4444-555555555555",
                    "secret",
                    "localhost",
                    2080,
                    allowInsecureTls: false,
                    trustedCertificatePath:
                        @"C:\ProtocolLab\tuic-cert.pem"
                );

            JsonObject root =
                JsonNode.Parse(json)!.AsObject();

            AssertEqual(
                @"C:\ProtocolLab\tuic-cert.pem",
                root["outbounds"]?[0]?["tls"]?
                    ["certificate_path"]?.GetValue<string>()
            );
        }

        private static void ValidateProtocolLabSnellConfig()
        {
            string json =
                ProtocolLabSnellConfigService.CreateLocalProxyConfig(
                    "edge.example.com",
                    443,
                    "123456789012",
                    2080
                );

            JsonObject root =
                JsonNode.Parse(json)?.AsObject()
                ?? throw new Exception(
                    "Не создан Snell JSON."
                );

            JsonObject outbound =
                root["outbounds"]?[0]?.AsObject()
                ?? throw new Exception(
                    "Не создан Snell outbound."
                );

            AssertEqual(
                "snell",
                outbound["type"]?.GetValue<string>()
            );
            AssertEqual(
                6,
                outbound["version"]?.GetValue<int>()
            );
            AssertEqual(
                "edge.example.com",
                outbound["server"]?.GetValue<string>()
            );
            AssertEqual(
                443,
                outbound["server_port"]?.GetValue<int>()
            );
            AssertEqual(
                "123456789012",
                outbound["psk"]?.GetValue<string>()
            );
            AssertEqual(
                false,
                outbound["reuse"]?.GetValue<bool>()
            );
            AssertEqual(
                "default",
                outbound["mode"]?.GetValue<string>()
            );
            AssertEqual(
                false,
                outbound.ContainsKey("network")
            );
            AssertEqual(
                false,
                outbound.ContainsKey("userkey")
            );
        }

        private static void ValidateProtocolLabSnellValidation()
        {
            AssertThrows<FormatException>(() =>
                ProtocolLabSnellConfigService.CreateLocalProxyConfig(
                    "edge.example.com",
                    443,
                    "short",
                    2080
                )
            );

            AssertThrows<ArgumentOutOfRangeException>(() =>
                ProtocolLabSnellConfigService.CreateLocalProxyConfig(
                    "edge.example.com",
                    0,
                    "123456789012",
                    2080
                )
            );

            AssertThrows<NotSupportedException>(() =>
                ProtocolLabSnellConfigService.CreateLocalProxyConfig(
                    "edge.example.com",
                    443,
                    "123456789012",
                    2080,
                    mode: "invalid"
                )
            );

            AssertThrows<NotSupportedException>(() =>
                ProtocolLabSnellConfigService.CreateLocalProxyConfig(
                    "edge.example.com",
                    443,
                    "123456789012",
                    2080,
                    network: "icmp"
                )
            );

            string json =
                ProtocolLabSnellConfigService.CreateLocalProxyConfig(
                    "127.0.0.1",
                    8443,
                    "123456789012",
                    2080,
                    mode: "unshaped",
                    network: "tcp",
                    reuse: true,
                    userKey: "lab-user-key"
                );

            JsonObject root =
                JsonNode.Parse(json)!.AsObject();

            AssertEqual(
                "unshaped",
                root["outbounds"]?[0]?["mode"]?.GetValue<string>()
            );
            AssertEqual(
                "tcp",
                root["outbounds"]?[0]?["network"]?.GetValue<string>()
            );
            AssertEqual(
                true,
                root["outbounds"]?[0]?["reuse"]?.GetValue<bool>()
            );
            AssertEqual(
                "lab-user-key",
                root["outbounds"]?[0]?["userkey"]?.GetValue<string>()
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

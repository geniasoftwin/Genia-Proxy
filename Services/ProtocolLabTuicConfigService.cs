using System.Text.Json;
using System.Text.Json.Nodes;

namespace GeniaProxy.Services
{
    public static class ProtocolLabTuicConfigService
    {
        private const int MaxHostLength = 253;
        private const int MaxSecretLength = 4096;

        private static readonly HashSet<string> AllowedCongestionControl =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "cubic",
                "new_reno",
                "bbr"
            };

        private static readonly HashSet<string> AllowedUdpRelayModes =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "native",
                "quic"
            };

        public static string CreateLocalProxyConfig(
            string server,
            int serverPort,
            string uuid,
            string password,
            string? serverName,
            int localPort,
            string congestionControl = "cubic",
            string udpRelayMode = "native",
            bool allowInsecureTls = false,
            string? trustedCertificatePath = null)
        {
            ProtocolLabSelectionAudit.RequireSelectableAndLog("tuic");

            server = NormalizeRequiredText(
                server,
                MaxHostLength,
                "Не указан адрес TUIC сервера.",
                "Адрес TUIC сервера слишком длинный."
            );

            password = NormalizeRequiredText(
                password,
                MaxSecretLength,
                "Не указан пароль TUIC.",
                "Пароль TUIC слишком длинный."
            );

            ValidatePort(
                serverPort,
                nameof(serverPort),
                "Порт TUIC сервера должен быть от 1 до 65535."
            );

            ValidatePort(
                localPort,
                nameof(localPort),
                "Локальный порт должен быть от 1 до 65535."
            );

            if (!Guid.TryParse(uuid, out Guid parsedUuid))
            {
                throw new FormatException(
                    "TUIC UUID имеет неправильный формат."
                );
            }

            string normalizedCongestionControl =
                NormalizeChoice(
                    congestionControl,
                    AllowedCongestionControl,
                    "Неподдерживаемый алгоритм congestion control TUIC."
                );

            string normalizedUdpRelayMode =
                NormalizeChoice(
                    udpRelayMode,
                    AllowedUdpRelayModes,
                    "Неподдерживаемый режим UDP relay TUIC."
                );

            string tlsServerName = string.IsNullOrWhiteSpace(serverName)
                ? server
                : NormalizeRequiredText(
                    serverName,
                    MaxHostLength,
                    "Не указано имя TUIC TLS-сервера.",
                    "Имя TUIC TLS-сервера слишком длинное."
                );

            var tls = new JsonObject
            {
                ["enabled"] = true,
                ["server_name"] = tlsServerName,
                ["insecure"] = allowInsecureTls
            };

            if (!string.IsNullOrWhiteSpace(trustedCertificatePath))
            {
                string certificatePath =
                    trustedCertificatePath.Trim();

                if (certificatePath.Any(char.IsControl))
                {
                    throw new FormatException(
                        "Путь к доверенному TLS-сертификату " +
                        "содержит управляющие символы."
                    );
                }

                tls["certificate_path"] = certificatePath;
            }

            var root = new JsonObject
            {
                ["log"] = new JsonObject
                {
                    ["level"] = "warn",
                    ["timestamp"] = true
                },
                ["inbounds"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "mixed",
                        ["tag"] = "mixed-in",
                        ["listen"] = "127.0.0.1",
                        ["listen_port"] = localPort,
                        ["set_system_proxy"] = false
                    }
                },
                ["outbounds"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "tuic",
                        ["tag"] = "proxy",
                        ["server"] = server,
                        ["server_port"] = serverPort,
                        ["uuid"] = parsedUuid.ToString("D"),
                        ["password"] = password,
                        ["congestion_control"] =
                            normalizedCongestionControl,
                        ["udp_relay_mode"] =
                            normalizedUdpRelayMode,

                        // sing-box documentation strongly recommends
                        // disabling TUIC 0-RTT because of replay risk.
                        ["zero_rtt_handshake"] = false,
                        ["heartbeat"] = "10s",
                        ["tls"] = tls
                    }
                },
                ["route"] = new JsonObject
                {
                    ["final"] = "proxy"
                }
            };

            return root.ToJsonString(
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }
            );
        }

        private static string NormalizeRequiredText(
            string? value,
            int maximumLength,
            string missingMessage,
            string tooLongMessage)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(missingMessage);
            }

            string normalized = value.Trim();

            if (normalized.Length > maximumLength)
            {
                throw new FormatException(tooLongMessage);
            }

            if (normalized.Any(char.IsControl))
            {
                throw new FormatException(
                    "Значение содержит недопустимые управляющие символы."
                );
            }

            return normalized;
        }

        private static string NormalizeChoice(
            string? value,
            HashSet<string> allowed,
            string errorMessage)
        {
            string normalized =
                NormalizeRequiredText(
                    value,
                    64,
                    errorMessage,
                    errorMessage
                );

            if (!allowed.Contains(normalized))
            {
                throw new NotSupportedException(errorMessage);
            }

            return normalized.ToLowerInvariant();
        }

        private static void ValidatePort(
            int value,
            string parameterName,
            string message)
        {
            if (value is < 1 or > 65535)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    message
                );
            }
        }
    }
}

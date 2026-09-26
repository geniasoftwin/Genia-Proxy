using System.Text.Json;
using System.Text.Json.Nodes;

namespace GeniaProxy.Services
{
    public static class ProtocolLabAnyTlsConfigService
    {
        private const int MaxHostLength = 253;
        private const int MaxSecretLength = 4096;

        public static string CreateLocalProxyConfig(
            string server,
            int serverPort,
            string password,
            string? serverName,
            int localPort,
            bool allowInsecureTls = false,
            string? trustedCertificatePath = null)
        {
            ProtocolLabSelectionAudit.RequireSelectableAndLog("anytls");

            server = NormalizeRequiredText(
                server,
                MaxHostLength,
                "Не указан адрес AnyTLS сервера.",
                "Адрес AnyTLS сервера слишком длинный."
            );

            password = NormalizeRequiredText(
                password,
                MaxSecretLength,
                "Не указан пароль AnyTLS.",
                "Пароль AnyTLS слишком длинный."
            );

            ValidatePort(
                serverPort,
                nameof(serverPort),
                "Порт AnyTLS сервера должен быть от 1 до 65535."
            );

            ValidatePort(
                localPort,
                nameof(localPort),
                "Локальный порт должен быть от 1 до 65535."
            );

            string tlsServerName = string.IsNullOrWhiteSpace(serverName)
                ? server
                : NormalizeRequiredText(
                    serverName,
                    MaxHostLength,
                    "Не указано имя AnyTLS TLS-сервера.",
                    "Имя AnyTLS TLS-сервера слишком длинное."
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
                        ["type"] = "anytls",
                        ["tag"] = "proxy",
                        ["server"] = server,
                        ["server_port"] = serverPort,
                        ["password"] = password,

                        // sing-box 1.13.16+ leaves AnyTLS client metadata
                        // empty by default for privacy. Keep it explicit in
                        // Protocol Lab so future changes cannot silently
                        // re-introduce client-identifying metadata.
                        ["client_metadata"] = string.Empty,

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

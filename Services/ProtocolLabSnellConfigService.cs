using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GeniaProxy.Services
{
    public static class ProtocolLabSnellConfigService
    {
        private const int MaxHostLength = 253;
        private const int MinV6PskBytes = 12;
        private const int MaxV6PskBytes = 255;

        private static readonly HashSet<string> AllowedModes =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "default",
                "unshaped",
                "unsafe-raw"
            };

        private static readonly HashSet<string> AllowedNetworks =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "tcp",
                "udp"
            };

        public static string CreateLocalProxyConfig(
            string server,
            int serverPort,
            string psk,
            int localPort,
            string mode = "default",
            string? network = null,
            bool reuse = false,
            string? userKey = null)
        {
            server = NormalizeRequiredText(
                server,
                MaxHostLength,
                "Не указан адрес Snell сервера.",
                "Адрес Snell сервера слишком длинный."
            );

            ValidatePort(
                serverPort,
                nameof(serverPort),
                "Порт Snell сервера должен быть от 1 до 65535."
            );

            ValidatePort(
                localPort,
                nameof(localPort),
                "Локальный порт должен быть от 1 до 65535."
            );

            psk = NormalizeRequiredText(
                psk,
                MaxV6PskBytes,
                "Не указан Snell PSK.",
                "Snell PSK слишком длинный."
            );

            int pskBytes = Encoding.UTF8.GetByteCount(psk);

            if (pskBytes is < MinV6PskBytes or > MaxV6PskBytes)
            {
                throw new FormatException(
                    "Snell v6 PSK должен иметь длину от " +
                    $"{MinV6PskBytes} до {MaxV6PskBytes} байт UTF-8."
                );
            }

            string normalizedMode = NormalizeChoice(
                mode,
                AllowedModes,
                "Неподдерживаемый режим Snell v6."
            );

            string? normalizedNetwork = null;

            if (!string.IsNullOrWhiteSpace(network))
            {
                normalizedNetwork = NormalizeChoice(
                    network,
                    AllowedNetworks,
                    "Неподдерживаемая сеть Snell."
                );
            }

            string? normalizedUserKey = null;

            if (!string.IsNullOrWhiteSpace(userKey))
            {
                normalizedUserKey = NormalizeRequiredText(
                    userKey,
                    4096,
                    "Не указан Snell userkey.",
                    "Snell userkey слишком длинный."
                );
            }

            var outbound = new JsonObject
            {
                ["type"] = "snell",
                ["tag"] = "proxy",
                ["server"] = server,
                ["server_port"] = serverPort,
                ["version"] = 6,
                ["psk"] = psk,
                ["reuse"] = reuse,
                ["mode"] = normalizedMode
            };

            if (normalizedNetwork is not null)
            {
                outbound["network"] = normalizedNetwork;
            }

            if (normalizedUserKey is not null)
            {
                outbound["userkey"] = normalizedUserKey;
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
                    outbound
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

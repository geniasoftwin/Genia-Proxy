using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GeniaProxy.Services
{
    public static class Hysteria2ImportService
    {
        public const int MaxSourceBytes = 32 * 1024;

        private const int MaxHostLength = 253;
        private const int MaxSecretLength = 4096;
        private const int MaxAlpnValues = 16;
        private const int MaxAlpnLength = 255;
        private const int MaxBandwidthMbps = 1_000_000;

        public static string CreateSingBoxConfig(
            string source,
            int localPort)
        {
            ValidateLocalPort(localPort);

            Uri uri = ParseUri(source);
            string server = uri.Host;

            ValidateTextLength(
                server,
                MaxHostLength,
                "Адрес сервера слишком длинный."
            );

            int serverPort = uri.Port;

            if (serverPort is < 1 or > 65535)
            {
                throw new FormatException(
                    "В ссылке отсутствует корректный порт сервера."
                );
            }

            string password = Decode(uri.UserInfo);

            if (string.IsNullOrWhiteSpace(password))
            {
                throw new FormatException(
                    "В ссылке отсутствует пароль Hysteria2."
                );
            }

            ValidateTextLength(
                password,
                MaxSecretLength,
                "Пароль Hysteria2 слишком длинный."
            );

            Dictionary<string, string> query =
                ParseQuery(uri.Query);

            if (query.ContainsKey("mport"))
            {
                throw new NotSupportedException(
                    "Ссылки Hysteria2 с переключением портов " +
                    "mport пока не поддерживаются."
                );
            }

            string serverName =
                GetFirstNotEmpty(
                    query,
                    "sni",
                    "peer"
                )
                ?? server;

            ValidateTextLength(
                serverName,
                MaxHostLength,
                "Имя сервера TLS слишком длинное."
            );

            bool insecure =
                GetBoolean(query, "insecure") ||
                GetBoolean(query, "allowInsecure");

            var tls = new JsonObject
            {
                ["enabled"] = true,
                ["server_name"] = serverName,
                ["insecure"] = insecure
            };

            AddAlpn(query, tls);

            var outbound = new JsonObject
            {
                ["type"] = "hysteria2",
                ["tag"] = "proxy",
                ["server"] = server,
                ["server_port"] = serverPort,
                ["password"] = password,
                ["tls"] = tls
            };

            AddBandwidth(query, outbound);
            AddObfuscation(query, outbound);

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

        public static bool RequestsInsecureTls(string source)
        {
            Uri uri = ParseUri(source);
            Dictionary<string, string> query =
                ParseQuery(uri.Query);

            return GetBoolean(query, "insecure") ||
                   GetBoolean(query, "allowInsecure");
        }

        private static Uri ParseUri(string source)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(source);

            source = source.Trim();

            if (Encoding.UTF8.GetByteCount(source) >
                MaxSourceBytes)
            {
                throw new FormatException(
                    "Ссылка подключения слишком длинная."
                );
            }

            if (!Uri.TryCreate(
                    source,
                    UriKind.Absolute,
                    out Uri? uri))
            {
                throw new FormatException(
                    "Ссылка имеет неправильный формат."
                );
            }

            bool supportedScheme =
                uri.Scheme.Equals(
                    "hy2",
                    StringComparison.OrdinalIgnoreCase
                ) ||
                uri.Scheme.Equals(
                    "hysteria2",
                    StringComparison.OrdinalIgnoreCase
                );

            if (!supportedScheme)
            {
                throw new NotSupportedException(
                    $"Протокол {uri.Scheme} пока не поддерживается."
                );
            }

            if (string.IsNullOrWhiteSpace(uri.Host))
            {
                throw new FormatException(
                    "В ссылке отсутствует адрес сервера."
                );
            }

            return uri;
        }

        private static Dictionary<string, string>
            ParseQuery(string queryString)
        {
            var result =
                new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase
                );

            string query = queryString.TrimStart('?');

            if (string.IsNullOrWhiteSpace(query))
            {
                return result;
            }

            foreach (string part in query.Split(
                         '&',
                         StringSplitOptions.RemoveEmptyEntries))
            {
                string[] pair = part.Split('=', 2);
                string key = Decode(pair[0]);
                string value = pair.Length > 1
                    ? Decode(pair[1])
                    : string.Empty;

                if (!string.IsNullOrWhiteSpace(key))
                {
                    result[key] = value;
                }
            }

            return result;
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
                    "Ссылка содержит некорректное " +
                    "процентное кодирование.",
                    ex
                );
            }
        }

        private static string? GetFirstNotEmpty(
            Dictionary<string, string> query,
            params string[] keys)
        {
            foreach (string key in keys)
            {
                if (query.TryGetValue(
                        key,
                        out string? value) &&
                    !string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return null;
        }

        private static bool GetBoolean(
            Dictionary<string, string> query,
            string key)
        {
            if (!query.TryGetValue(
                    key,
                    out string? value))
            {
                return false;
            }

            return value.Equals(
                       "1",
                       StringComparison.OrdinalIgnoreCase) ||
                   value.Equals(
                       "true",
                       StringComparison.OrdinalIgnoreCase) ||
                   value.Equals(
                       "yes",
                       StringComparison.OrdinalIgnoreCase);
        }

        private static void AddAlpn(
            Dictionary<string, string> query,
            JsonObject tls)
        {
            string? alpnValue =
                GetFirstNotEmpty(query, "alpn");

            if (string.IsNullOrWhiteSpace(alpnValue))
            {
                return;
            }

            string[] values = alpnValue.Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries
            );

            if (values.Length == 0)
            {
                return;
            }

            if (values.Length > MaxAlpnValues)
            {
                throw new FormatException(
                    $"Слишком много значений ALPN " +
                    $"(максимум {MaxAlpnValues})."
                );
            }

            var alpnArray = new JsonArray();

            foreach (string value in values)
            {
                ValidateTextLength(
                    value,
                    MaxAlpnLength,
                    "Значение ALPN слишком длинное."
                );

                alpnArray.Add(value);
            }

            tls["alpn"] = alpnArray;
        }

        private static void AddBandwidth(
            Dictionary<string, string> query,
            JsonObject outbound)
        {
            int? upMbps = GetPositiveInteger(
                query,
                "upmbps"
            );

            int? downMbps = GetPositiveInteger(
                query,
                "downmbps"
            );

            if (upMbps.HasValue)
            {
                outbound["up_mbps"] = upMbps.Value;
            }

            if (downMbps.HasValue)
            {
                outbound["down_mbps"] = downMbps.Value;
            }
        }

        private static int? GetPositiveInteger(
            Dictionary<string, string> query,
            string key)
        {
            if (!query.TryGetValue(
                    key,
                    out string? value))
            {
                return null;
            }

            if (!int.TryParse(
                    value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int number) ||
                number is < 1 or > MaxBandwidthMbps)
            {
                throw new FormatException(
                    $"Параметр {key} должен быть числом " +
                    $"от 1 до {MaxBandwidthMbps}."
                );
            }

            return number;
        }

        private static void AddObfuscation(
            Dictionary<string, string> query,
            JsonObject outbound)
        {
            string? obfsType =
                GetFirstNotEmpty(query, "obfs");

            if (string.IsNullOrWhiteSpace(obfsType) ||
                obfsType.Equals(
                    "none",
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!obfsType.Equals(
                    "salamander",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new NotSupportedException(
                    $"Тип obfs «{obfsType}» не поддерживается."
                );
            }

            string? obfsPassword =
                GetFirstNotEmpty(
                    query,
                    "obfs-password",
                    "obfsPassword"
                );

            if (string.IsNullOrWhiteSpace(obfsPassword))
            {
                throw new FormatException(
                    "В ссылке указан obfs, " +
                    "но отсутствует пароль обфускации."
                );
            }

            ValidateTextLength(
                obfsPassword,
                MaxSecretLength,
                "Пароль обфускации слишком длинный."
            );

            outbound["obfs"] = new JsonObject
            {
                ["type"] = "salamander",
                ["password"] = obfsPassword
            };
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

        private static void ValidateTextLength(
            string value,
            int maximumLength,
            string errorMessage)
        {
            if (value.Length > maximumLength)
            {
                throw new FormatException(errorMessage);
            }
        }
    }
}

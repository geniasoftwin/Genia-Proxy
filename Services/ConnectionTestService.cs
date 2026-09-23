using GeniaProxy.Models;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace GeniaProxy.Services
{
    public sealed record ConnectionTestRequestResult(
        bool IsSuccess,
        int StatusCode,
        double ElapsedMilliseconds,
        Version HttpVersion,
        string Error);

    public sealed record Ipv6PrivacyProbeResult(
        bool IsApplicable,
        bool HasConnectivity,
        bool LeakDetected,
        string ExitIp,
        string Detail);

    public sealed class ConnectionTestResult
    {
        public required ConnectionTestRequestResult InitialRequest
            { get; init; }

        public required IReadOnlyList<ConnectionTestRequestResult>
            ParallelRequests { get; init; }

        public required string ExitIp { get; init; }

        public required string CloudflareProtocol { get; init; }

        public required Ipv6PrivacyProbeResult Ipv6Privacy { get; init; }

        public required string DnsInterfaceSummary { get; init; }

        public int SuccessfulParallelRequests =>
            ParallelRequests.Count(item => item.IsSuccess);

        public double AverageParallelMilliseconds =>
            ParallelRequests.All(item => !item.IsSuccess)
                ? 0
                : ParallelRequests
                    .Where(item => item.IsSuccess)
                    .Average(item => item.ElapsedMilliseconds);

        public bool IsSuccess =>
            InitialRequest.IsSuccess &&
            SuccessfulParallelRequests == ParallelRequests.Count &&
            !Ipv6Privacy.LeakDetected;

        public string FormatReport(
            string profileName,
            ConnectionMode mode,
            int port)
        {
            var lines = new List<string>
            {
                "GeniaProxy — проверка канала и утечек",
                "====================================",
                $"Профиль: {profileName}",
                $"Режим: {FormatMode(mode)}",
                mode == ConnectionMode.Tun
                    ? "Маршрут: через активный TUN"
                    : $"Маршрут: SOCKS5 127.0.0.1:{port}",
                $"Внешний IPv4: " +
                    (string.IsNullOrWhiteSpace(ExitIp) ? "—" : ExitIp),
                $"Протокол Cloudflare: " +
                    (string.IsNullOrWhiteSpace(CloudflareProtocol)
                        ? "—"
                        : CloudflareProtocol),
                string.Empty,
                "Приватность:",
                FormatIpv6Privacy(Ipv6Privacy),
                "DNS-интерфейсы Windows: " +
                    (string.IsNullOrWhiteSpace(DnsInterfaceSummary)
                        ? "не определены"
                        : DnsInterfaceSummary),
                mode == ConnectionMode.Tun
                    ? "WebRTC: сетевой TUN защищает системный маршрут; " +
                      "браузерную ICE-проверку следует выполнять отдельно."
                    : "WebRTC: локальный/системный прокси не является " +
                      "полным системным туннелем; для максимальной защиты " +
                      "используйте TUN.",
                string.Empty,
                "Одиночный запрос:",
                FormatRequest(InitialRequest),
                string.Empty,
                $"Параллельная проверка: " +
                    $"{SuccessfulParallelRequests}/" +
                    $"{ParallelRequests.Count} успешно",
                $"Средняя задержка: " +
                    $"{AverageParallelMilliseconds:F0} мс"
            };

            for (int index = 0; index < ParallelRequests.Count; index++)
            {
                lines.Add(
                    $"{index + 1,2}. " +
                    FormatRequest(ParallelRequests[index])
                );
            }

            lines.Add(string.Empty);

            if (Ipv6Privacy.LeakDetected)
            {
                lines.Add(
                    "Результат: ОБНАРУЖЕНА возможная IPv6-утечка. " +
                    "TUN не следует считать leak-safe."
                );
            }
            else
            {
                lines.Add(IsSuccess
                    ? mode == ConnectionMode.Tun
                        ? "Результат: канал работает стабильно; " +
                          "IPv6-утечка в TUN probe не обнаружена."
                        : "Результат: канал работает стабильно. Для полной " +
                          "проверки системных утечек используйте режим TUN."
                    : "Результат: обнаружены ошибки канала.");
            }

            return string.Join(Environment.NewLine, lines);
        }

        private static string FormatIpv6Privacy(
            Ipv6PrivacyProbeResult result)
        {
            if (!result.IsApplicable)
            {
                return "IPv6 leak probe: не применяется к этому режиму. " +
                       result.Detail;
            }

            if (result.LeakDetected)
            {
                string ip = string.IsNullOrWhiteSpace(result.ExitIp)
                    ? "не определён"
                    : result.ExitIp;
                return $"IPv6 leak probe: FAIL · внешний IPv6 {ip}. " +
                       result.Detail;
            }

            if (!result.HasConnectivity)
            {
                return "IPv6 leak probe: PASS · прямой IPv6 через TUN " +
                       "недоступен/заблокирован. " + result.Detail;
            }

            return "IPv6 leak probe: PASS · IPv6 доступен через " +
                   "поддерживаемый туннель. " + result.Detail;
        }

        private static string FormatRequest(
            ConnectionTestRequestResult result)
        {
            string http = result.StatusCode > 0
                ? $"HTTP {result.StatusCode}"
                : "HTTP —";
            string version = result.HttpVersion.Major > 0
                ? $"HTTP/{result.HttpVersion}"
                : "HTTP/?";
            string error = string.IsNullOrWhiteSpace(result.Error)
                ? string.Empty
                : " · " + result.Error;

            return $"{http} · {result.ElapsedMilliseconds:F0} мс · " +
                   $"{version}{error}";
        }

        private static string FormatMode(ConnectionMode mode) =>
            mode switch
            {
                ConnectionMode.SystemProxy => "Системный прокси",
                ConnectionMode.Tun => "TUN",
                _ => "Локальный SOCKS5"
            };
    }

    public static class ConnectionTestService
    {
        private const string TraceUrl =
            "https://www.cloudflare.com/cdn-cgi/trace";

        public static async Task<ConnectionTestResult> RunAsync(
            int localPort,
            ConnectionMode mode,
            int parallelRequests = 20,
            CancellationToken cancellationToken = default)
        {
            if (localPort is < 1024 or > 65535)
            {
                throw new ArgumentOutOfRangeException(nameof(localPort));
            }

            if (parallelRequests is < 1 or > 50)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(parallelRequests)
                );
            }

            using var handler = new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                ConnectTimeout = TimeSpan.FromSeconds(8),
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                UseProxy = mode != ConnectionMode.Tun
            };

            if (mode != ConnectionMode.Tun)
            {
                handler.Proxy = new WebProxy(
                    $"socks5://127.0.0.1:{localPort}"
                );
            }

            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(15)
            };

            // Do not identify GeniaProxy to the remote diagnostic endpoint.
            // HttpClient's normal minimal request headers are sufficient.
            (ConnectionTestRequestResult initial, string trace) =
                await SendAsync(client, cancellationToken);

            Task<(ConnectionTestRequestResult Result, string Trace)>[] tasks =
                Enumerable.Range(0, parallelRequests)
                    .Select(_ => SendAsync(client, cancellationToken))
                    .ToArray();

            (ConnectionTestRequestResult Result, string Trace)[] responses =
                await Task.WhenAll(tasks);

            string effectiveTrace = string.IsNullOrWhiteSpace(trace)
                ? responses.Select(item => item.Trace)
                    .FirstOrDefault(item =>
                        !string.IsNullOrWhiteSpace(item))
                    ?? string.Empty
                : trace;

            Ipv6PrivacyProbeResult ipv6Privacy =
                await ProbeIpv6PrivacyAsync(mode, cancellationToken);

            return new ConnectionTestResult
            {
                InitialRequest = initial,
                ParallelRequests = responses
                    .Select(item => item.Result)
                    .ToArray(),
                ExitIp = ParseTraceValue(effectiveTrace, "ip"),
                CloudflareProtocol = ParseTraceValue(
                    effectiveTrace,
                    "http"
                ),
                Ipv6Privacy = ipv6Privacy,
                DnsInterfaceSummary = GetDnsInterfaceSummary()
            };
        }

        public static string ParseTraceValue(
            string trace,
            string key)
        {
            if (string.IsNullOrWhiteSpace(trace) ||
                string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            string prefix = key.Trim() + "=";

            foreach (string line in trace.Split(
                         ['\r', '\n'],
                         StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.StartsWith(
                        prefix,
                        StringComparison.OrdinalIgnoreCase))
                {
                    string value = line[prefix.Length..].Trim();
                    return value.Length <= 128 &&
                           !value.Any(char.IsControl)
                        ? value
                        : string.Empty;
                }
            }

            return string.Empty;
        }

        private static async Task<Ipv6PrivacyProbeResult>
            ProbeIpv6PrivacyAsync(
                ConnectionMode mode,
                CancellationToken cancellationToken)
        {
            if (mode != ConnectionMode.Tun)
            {
                return new Ipv6PrivacyProbeResult(
                    false,
                    false,
                    false,
                    string.Empty,
                    "Проверка прямого IPv6 предназначена для полного TUN."
                );
            }

            using var handler = new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                ConnectTimeout = TimeSpan.FromSeconds(5),
                PooledConnectionLifetime = TimeSpan.Zero,
                UseProxy = false,
                ConnectCallback = ConnectIpv6Async
            };

            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(8)
            };

            try
            {
                using HttpResponseMessage response = await client.GetAsync(
                    TraceUrl + "?ipv6=" + Guid.NewGuid().ToString("N"),
                    HttpCompletionOption.ResponseContentRead,
                    cancellationToken
                );

                string trace = await response.Content.ReadAsStringAsync(
                    cancellationToken
                );
                string exitIp = ParseTraceValue(trace, "ip");

                bool isIpv6 = IPAddress.TryParse(
                    exitIp,
                    out IPAddress? address
                ) && address.AddressFamily == AddressFamily.InterNetworkV6;

                // GeniaProxy 4.2.1 TUN configurations are deliberately
                // IPv4-only. The TCP socket above is forced to IPv6, so any
                // successful HTTP response is already proof that IPv6 escaped
                // the current TUN, even if the remote trace omits its IP field.
                return new Ipv6PrivacyProbeResult(
                    true,
                    true,
                    true,
                    isIpv6 ? exitIp : string.Empty,
                    isIpv6
                        ? "Текущий TUN IPv4-only, поэтому этот IPv6 вышел " +
                          "в обход туннеля."
                        : "Forced-IPv6 HTTP соединение успешно: текущий " +
                          "IPv4-only TUN не перехватил IPv6."
                );
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (
                ex is HttpRequestException or
                TaskCanceledException or
                SocketException)
            {
                return new Ipv6PrivacyProbeResult(
                    true,
                    false,
                    false,
                    string.Empty,
                    "Это ожидаемо для leak-safe IPv4-only TUN. " +
                    SanitizeProbeError(ex.GetBaseException().Message)
                );
            }
        }

        private static async ValueTask<Stream> ConnectIpv6Async(
            SocketsHttpConnectionContext context,
            CancellationToken cancellationToken)
        {
            IPAddress[] addresses = await Dns.GetHostAddressesAsync(
                context.DnsEndPoint.Host,
                cancellationToken
            );

            Exception? lastFailure = null;

            foreach (IPAddress address in addresses.Where(value =>
                         value.AddressFamily == AddressFamily.InterNetworkV6))
            {
                var socket = new Socket(
                    AddressFamily.InterNetworkV6,
                    SocketType.Stream,
                    ProtocolType.Tcp
                );

                try
                {
                    await socket.ConnectAsync(
                        new IPEndPoint(
                            address,
                            context.DnsEndPoint.Port
                        ),
                        cancellationToken
                    );

                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch (Exception ex) when (
                    ex is SocketException or OperationCanceledException)
                {
                    lastFailure = ex;
                    socket.Dispose();

                    if (ex is OperationCanceledException)
                    {
                        throw;
                    }
                }
            }

            throw new HttpRequestException(
                "IPv6 endpoint недоступен.",
                lastFailure
            );
        }

        private static string GetDnsInterfaceSummary()
        {
            try
            {
                string[] entries = NetworkInterface
                    .GetAllNetworkInterfaces()
                    .Where(item =>
                        item.OperationalStatus == OperationalStatus.Up &&
                        item.NetworkInterfaceType !=
                            NetworkInterfaceType.Loopback)
                    .Select(item => new
                    {
                        item.Name,
                        Servers = item.GetIPProperties()
                            .DnsAddresses
                            .Where(address =>
                                !address.Equals(IPAddress.Any) &&
                                !address.Equals(IPAddress.IPv6Any))
                            .Select(address => address.ToString())
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToArray()
                    })
                    .Where(item => item.Servers.Length > 0)
                    .Take(8)
                    .Select(item =>
                        $"{item.Name}=[{string.Join(", ", item.Servers)}]")
                    .ToArray();

                return string.Join("; ", entries);
            }
            catch (NetworkInformationException)
            {
                return string.Empty;
            }
        }

        private static string SanitizeProbeError(string value)
        {
            string normalized = value
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Trim();

            if (normalized.Length > 180)
            {
                normalized = normalized[..180];
            }

            return normalized;
        }

        private static async Task<(
            ConnectionTestRequestResult Result,
            string Trace)> SendAsync(
            HttpClient client,
            CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                using HttpResponseMessage response = await client.GetAsync(
                    TraceUrl + "?t=" + Guid.NewGuid().ToString("N"),
                    HttpCompletionOption.ResponseContentRead,
                    cancellationToken
                );
                string trace = await response.Content.ReadAsStringAsync(
                    cancellationToken
                );
                stopwatch.Stop();

                return (new ConnectionTestRequestResult(
                    response.IsSuccessStatusCode,
                    (int)response.StatusCode,
                    stopwatch.Elapsed.TotalMilliseconds,
                    response.Version,
                    response.IsSuccessStatusCode
                        ? string.Empty
                        : response.ReasonPhrase ?? "Ошибка HTTP"
                ), trace);
            }
            catch (Exception ex) when (
                ex is HttpRequestException or TaskCanceledException)
            {
                stopwatch.Stop();
                return (new ConnectionTestRequestResult(
                    false,
                    0,
                    stopwatch.Elapsed.TotalMilliseconds,
                    new Version(0, 0),
                    ex.GetBaseException().Message
                ), string.Empty);
            }
        }
    }
}

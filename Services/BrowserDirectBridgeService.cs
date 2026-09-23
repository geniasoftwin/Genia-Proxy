using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace GeniaProxy.Services
{
    /// <summary>
    /// Read-only browser bridge hosted inside GeniaProxy.exe.
    /// No Native Messaging host and no external helper process are used.
    /// The listener is bound strictly to IPv4 loopback.
    /// </summary>
    public sealed class BrowserDirectBridgeService : IDisposable
    {
        public const int Port = 47831;
        public const int ProtocolVersion = 1;
        public const string BridgeVersion = "direct-1-exp2";
        public const string StatusPath = "/v1/status";

        private static readonly JsonSerializerOptions JsonOptions =
            new()
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            };

        private readonly Func<BrowserDirectBridgeStatus> statusProvider;
        private readonly object sync = new();
        private TcpListener? listener;
        private CancellationTokenSource? cancellation;
        private Task? acceptLoop;
        private bool disposed;

        public BrowserDirectBridgeService(
            Func<BrowserDirectBridgeStatus> statusProvider)
        {
            ArgumentNullException.ThrowIfNull(statusProvider);
            this.statusProvider = statusProvider;
        }

        public bool IsRunning
        {
            get
            {
                lock (sync)
                {
                    return listener is not null;
                }
            }
        }

        public string Endpoint =>
            $"http://127.0.0.1:{Port}{StatusPath}";

        public void Start()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(BrowserDirectBridgeService));
            }

            lock (sync)
            {
                if (listener is not null)
                {
                    return;
                }

                var nextListener = new TcpListener(
                    IPAddress.Loopback,
                    Port
                );

                // Keep the endpoint private to this process and fail clearly
                // if another application already owns the loopback port.
                nextListener.Server.ExclusiveAddressUse = true;
                nextListener.Start(backlog: 16);

                var nextCancellation = new CancellationTokenSource();
                listener = nextListener;
                cancellation = nextCancellation;
                acceptLoop = AcceptLoopAsync(
                    nextListener,
                    nextCancellation.Token
                );
            }
        }

        public void Stop()
        {
            TcpListener? toStop;
            CancellationTokenSource? toCancel;

            lock (sync)
            {
                toStop = listener;
                toCancel = cancellation;
                listener = null;
                cancellation = null;
                acceptLoop = null;
            }

            if (toStop is null)
            {
                return;
            }

            try
            {
                toCancel?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            try
            {
                toStop.Stop();
            }
            catch (SocketException)
            {
            }

            toCancel?.Dispose();
        }

        private async Task AcceptLoopAsync(
            TcpListener activeListener,
            CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await activeListener
                        .AcceptTcpClientAsync(cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (SocketException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                _ = Task.Run(
                    () => HandleClientAsync(client, cancellationToken),
                    CancellationToken.None
                );
            }
        }

        private async Task HandleClientAsync(
            TcpClient client,
            CancellationToken serviceCancellation)
        {
            using (client)
            {
                using CancellationTokenSource requestCancellation =
                    CancellationTokenSource.CreateLinkedTokenSource(
                        serviceCancellation
                    );

                requestCancellation.CancelAfter(TimeSpan.FromSeconds(4));
                CancellationToken token = requestCancellation.Token;

                try
                {
                    client.NoDelay = true;
                    using NetworkStream stream = client.GetStream();
                    using var reader = new StreamReader(
                        stream,
                        Encoding.ASCII,
                        detectEncodingFromByteOrderMarks: false,
                        bufferSize: 4096,
                        leaveOpen: true
                    );

                    string? requestLine = await reader
                        .ReadLineAsync(token)
                        .ConfigureAwait(false);

                    if (string.IsNullOrWhiteSpace(requestLine) ||
                        requestLine.Length > 2048)
                    {
                        await WriteResponseAsync(
                            stream,
                            400,
                            "Bad Request",
                            "application/json; charset=utf-8",
                            "{\"error\":\"bad_request\"}",
                            origin: null,
                            token
                        ).ConfigureAwait(false);
                        return;
                    }

                    string[] firstLine = requestLine.Split(
                        ' ',
                        StringSplitOptions.RemoveEmptyEntries
                    );

                    if (firstLine.Length != 3)
                    {
                        await WriteResponseAsync(
                            stream,
                            400,
                            "Bad Request",
                            "application/json; charset=utf-8",
                            "{\"error\":\"bad_request\"}",
                            origin: null,
                            token
                        ).ConfigureAwait(false);
                        return;
                    }

                    string method = firstLine[0];
                    string path = firstLine[1];
                    string? origin = null;
                    int headerBytes = requestLine.Length + 2;

                    while (true)
                    {
                        string? line = await reader
                            .ReadLineAsync(token)
                            .ConfigureAwait(false);

                        if (line is null || line.Length == 0)
                        {
                            break;
                        }

                        headerBytes += line.Length + 2;
                        if (headerBytes > 16 * 1024 || line.Length > 4096)
                        {
                            await WriteResponseAsync(
                                stream,
                                431,
                                "Request Header Fields Too Large",
                                "application/json; charset=utf-8",
                                "{\"error\":\"headers_too_large\"}",
                                origin: null,
                                token
                            ).ConfigureAwait(false);
                            return;
                        }

                        int colon = line.IndexOf(':');
                        if (colon <= 0)
                        {
                            continue;
                        }

                        string name = line[..colon].Trim();
                        if (name.Equals(
                            "Origin",
                            StringComparison.OrdinalIgnoreCase))
                        {
                            origin = line[(colon + 1)..].Trim();
                        }
                    }

                    if (!IsAllowedOrigin(origin))
                    {
                        await WriteResponseAsync(
                            stream,
                            403,
                            "Forbidden",
                            "application/json; charset=utf-8",
                            "{\"error\":\"origin_not_allowed\"}",
                            origin: null,
                            token
                        ).ConfigureAwait(false);
                        return;
                    }

                    if (!path.Equals(StatusPath, StringComparison.Ordinal))
                    {
                        await WriteResponseAsync(
                            stream,
                            404,
                            "Not Found",
                            "application/json; charset=utf-8",
                            "{\"error\":\"not_found\"}",
                            origin,
                            token
                        ).ConfigureAwait(false);
                        return;
                    }

                    if (method.Equals(
                        "OPTIONS",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        await WriteResponseAsync(
                            stream,
                            204,
                            "No Content",
                            "text/plain; charset=utf-8",
                            string.Empty,
                            origin,
                            token
                        ).ConfigureAwait(false);
                        return;
                    }

                    if (!method.Equals(
                        "GET",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        await WriteResponseAsync(
                            stream,
                            405,
                            "Method Not Allowed",
                            "application/json; charset=utf-8",
                            "{\"error\":\"read_only\"}",
                            origin,
                            token
                        ).ConfigureAwait(false);
                        return;
                    }

                    BrowserDirectBridgeStatus status = statusProvider();
                    string payload = JsonSerializer.Serialize(
                        status,
                        JsonOptions
                    );

                    await WriteResponseAsync(
                        stream,
                        200,
                        "OK",
                        "application/json; charset=utf-8",
                        payload,
                        origin,
                        token
                    ).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                catch (IOException)
                {
                }
                catch (SocketException)
                {
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }

        private static bool IsAllowedOrigin(string? origin)
        {
            if (string.IsNullOrWhiteSpace(origin))
            {
                // Extension host_permissions may omit Origin. The endpoint is
                // read-only and loopback-only, so an origin-less local client
                // does not gain any control capability.
                return true;
            }

            if (!Uri.TryCreate(origin, UriKind.Absolute, out Uri? uri) ||
                !uri.Scheme.Equals(
                    "chrome-extension",
                    StringComparison.OrdinalIgnoreCase
                ))
            {
                return false;
            }

            string id = uri.Host;
            return id.Length == 32 && id.All(ch => ch is >= 'a' and <= 'p');
        }

        private static async Task WriteResponseAsync(
            NetworkStream stream,
            int statusCode,
            string reason,
            string contentType,
            string body,
            string? origin,
            CancellationToken cancellationToken)
        {
            byte[] payload = Encoding.UTF8.GetBytes(body);
            var headers = new StringBuilder(512);
            headers.Append("HTTP/1.1 ")
                .Append(statusCode)
                .Append(' ')
                .Append(reason)
                .Append("\r\n");
            headers.Append("Connection: close\r\n");
            headers.Append("Cache-Control: no-store\r\n");
            headers.Append("Pragma: no-cache\r\n");
            headers.Append("X-Content-Type-Options: nosniff\r\n");
            headers.Append("Content-Type: ")
                .Append(contentType)
                .Append("\r\n");
            headers.Append("Content-Length: ")
                .Append(payload.Length)
                .Append("\r\n");

            if (!string.IsNullOrWhiteSpace(origin))
            {
                headers.Append("Access-Control-Allow-Origin: ")
                    .Append(origin)
                    .Append("\r\n");
                headers.Append("Vary: Origin\r\n");
            }

            headers.Append("Access-Control-Allow-Methods: GET, OPTIONS\r\n");
            headers.Append("Access-Control-Allow-Headers: Content-Type\r\n");
            headers.Append("Access-Control-Allow-Private-Network: true\r\n");
            headers.Append("\r\n");

            byte[] headerBytes = Encoding.ASCII.GetBytes(headers.ToString());
            await stream.WriteAsync(
                headerBytes,
                cancellationToken
            ).ConfigureAwait(false);

            if (payload.Length > 0)
            {
                await stream.WriteAsync(
                    payload,
                    cancellationToken
                ).ConfigureAwait(false);
            }

            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            Stop();
            GC.SuppressFinalize(this);
        }
    }

    public sealed record BrowserDirectBridgeStatus(
        string Type,
        int Protocol,
        bool Connected,
        string Version,
        string BridgeVersion,
        string Engine,
        string ExpectedEngine,
        string ObservedEngine,
        string CoreCoherence,
        string Profile,
        string Mode,
        BrowserDirectLocalProxy? LocalProxy,
        BrowserDirectEndpoint? Endpoint,
        string? ExpectedExitIp,
        string? VerifiedExitIp,
        string? VerifiedExitAt,
        bool? VerifiedExitSucceeded,
        long UptimeSeconds
    );

    public sealed record BrowserDirectLocalProxy(
        string Scheme,
        string Host,
        int Port
    );

    public sealed record BrowserDirectEndpoint(
        string Host,
        int? Port
    );
}

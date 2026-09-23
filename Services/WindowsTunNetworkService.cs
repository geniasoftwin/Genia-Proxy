using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using GeniaProxy.Models;
using Microsoft.Win32;

namespace GeniaProxy.Services
{
    public sealed record XrayTunPreparation(
        string OriginalHost,
        int ProxyPort,
        IPAddress ProxyEndpoint,
        int PhysicalInterfaceIndex,
        string PhysicalInterfaceId,
        string PhysicalInterfaceAlias,
        IPAddress PhysicalIpv4,
        IPAddress PhysicalGateway,
        IReadOnlyList<IPAddress> OriginalDnsServers,
        bool OriginalDnsWasAutomatic);

    public sealed record TunAdapterInfo(
        int InterfaceIndex,
        string InterfaceId,
        string InterfaceAlias,
        IPAddress Ipv4Address);

    public sealed record TunRoutePlanItem(
        string DestinationPrefix,
        int InterfaceIndex,
        string NextHop);

    public enum TunRestoreOutcome
    {
        NothingToRestore,
        Restored,
        DnsPreserved
    }

    public sealed class WindowsTunNetworkService
    {
        public const string XrayTunInterfaceName = "geniaproxy-tun";
        public const string SingBoxTunInterfaceName = "GeniaProxy";

        private static readonly IPAddress PublicRouteProbe =
            IPAddress.Parse("1.1.1.1");

        private static readonly IPAddress PublicIpv6RouteProbe =
            IPAddress.Parse("2606:4700:4700::1111");

        private static readonly TimeSpan SingBoxRouteReadyTimeout =
            TimeSpan.FromSeconds(12);

        private static readonly TimeSpan SingBoxRoutePollInterval =
            TimeSpan.FromMilliseconds(250);

        private const int SingBoxTunInterfaceMetric = 5;

        private static readonly string[] LeakSafeDnsServers =
            ["1.1.1.1", "1.0.0.1"];

        private static readonly JsonSerializerOptions JsonOptions =
            new()
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true
            };

        public string BackupPath { get; } = Path.Combine(
            AppContext.BaseDirectory,
            "data",
            "tun-network-backup.json"
        );

        public bool HasPendingBackup => File.Exists(BackupPath);

        public static async Task<XrayTunPreparation> PrepareXrayAsync(
            string source,
            CancellationToken cancellationToken)
        {
            XrayVlessEndpoint endpoint =
                XrayProfileImportService.GetPrimaryVlessEndpoint(source);

            IPAddress proxyEndpoint = await ResolveIpv4Async(
                endpoint.Address,
                cancellationToken
            );

            int physicalInterfaceIndex =
                GetBestInterfaceIndex(proxyEndpoint);

            NetworkInterface physicalInterface =
                FindInterfaceByIndex(physicalInterfaceIndex)
                ?? throw new InvalidOperationException(
                    "Не удалось определить физический интерфейс " +
                    "для подключения к proxy endpoint."
                );

            IPInterfaceProperties properties =
                physicalInterface.GetIPProperties();

            IPAddress physicalIpv4 = properties.UnicastAddresses
                .Select(value => value.Address)
                .FirstOrDefault(value =>
                    value.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(value))
                ?? throw new InvalidOperationException(
                    "У физического интерфейса отсутствует IPv4-адрес."
                );

            IPAddress physicalGateway = properties.GatewayAddresses
                .Select(value => value.Address)
                .FirstOrDefault(value =>
                    value.AddressFamily == AddressFamily.InterNetwork &&
                    !value.Equals(IPAddress.Any))
                ?? throw new InvalidOperationException(
                    "У физического интерфейса отсутствует IPv4 gateway."
                );

            bool hasIpv6DefaultGateway = properties.GatewayAddresses
                .Any(value =>
                    value.Address.AddressFamily == AddressFamily.InterNetworkV6 &&
                    !value.Address.Equals(IPAddress.IPv6Any));

            if (hasIpv6DefaultGateway)
            {
                throw new InvalidOperationException(
                    "Обнаружен IPv6 default gateway. В GeniaProxy 4.2.x " +
                    "Xray TUN пока работает только в leak-safe IPv4 режиме. " +
                    "Отключите IPv6 для этого подключения или используйте " +
                    "локальный/системный прокси до реализации IPv6 TUN."
                );
            }

            IReadOnlyList<IPAddress> dnsServers = properties.DnsAddresses
                .Where(value =>
                    value.AddressFamily == AddressFamily.InterNetwork)
                .ToArray();

            bool dnsAutomatic = IsDnsAutomatic(physicalInterface.Id);

            return new XrayTunPreparation(
                endpoint.Address,
                endpoint.Port,
                proxyEndpoint,
                physicalInterfaceIndex,
                physicalInterface.Id,
                physicalInterface.Name,
                physicalIpv4,
                physicalGateway,
                dnsServers,
                dnsAutomatic
            );
        }

        public static async Task<TunAdapterInfo> WaitForTunAdapterReadyAsync(
            string expectedName,
            Func<bool> isCoreRunning,
            string coreDisplayName,
            CancellationToken cancellationToken)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(15);
            string stage = "adapter не найден";

            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!isCoreRunning())
                {
                    throw new InvalidOperationException(
                        $"{coreDisplayName} завершился до готовности TUN. " +
                        $"Последняя стадия: {stage}."
                    );
                }

                NetworkInterface? adapter = FindTunAdapter(expectedName);

                if (adapter is null)
                {
                    stage = $"adapter {expectedName} не найден";
                    await Task.Delay(150, cancellationToken);
                    continue;
                }

                if (adapter.OperationalStatus != OperationalStatus.Up)
                {
                    stage = $"adapter {adapter.Name} ещё не Up";
                    await Task.Delay(150, cancellationToken);
                    continue;
                }

                IPv4InterfaceProperties? ipv4Properties;

                try
                {
                    ipv4Properties = adapter.GetIPProperties()
                        .GetIPv4Properties();
                }
                catch (NetworkInformationException)
                {
                    ipv4Properties = null;
                }

                if (ipv4Properties is null || ipv4Properties.Index <= 0)
                {
                    stage = $"у adapter {adapter.Name} нет IPv4 ifIndex";
                    await Task.Delay(150, cancellationToken);
                    continue;
                }

                IPAddress? ipv4 = adapter.GetIPProperties()
                    .UnicastAddresses
                    .Select(value => value.Address)
                    .FirstOrDefault(value =>
                        value.AddressFamily == AddressFamily.InterNetwork &&
                        !value.Equals(IPAddress.Any));

                if (ipv4 is null)
                {
                    stage = $"у adapter {adapter.Name} нет usable IPv4";
                    await Task.Delay(150, cancellationToken);
                    continue;
                }

                return new TunAdapterInfo(
                    ipv4Properties.Index,
                    adapter.Id,
                    adapter.Name,
                    ipv4
                );
            }

            throw new TimeoutException(
                "TUN не перешёл в готовое состояние за 15 секунд. " +
                $"Последняя стадия: {stage}."
            );
        }

        public async Task ApplyXrayAsync(
            XrayTunPreparation preparation,
            TunAdapterInfo adapter,
            CancellationToken cancellationToken)
        {
            if (HasPendingBackup)
            {
                throw new InvalidOperationException(
                    "Обнаружена незавершённая резервная копия TUN. " +
                    "Сначала восстановите прежнее состояние сети."
                );
            }

            IReadOnlyList<TunRoutePlanItem> routePlan =
                BuildXrayRoutePlan(preparation, adapter);

            foreach (TunRoutePlanItem route in routePlan)
            {
                await EnsureDestinationPrefixAbsentAsync(
                    route.DestinationPrefix,
                    cancellationToken
                );
            }

            var snapshot = new TunNetworkSnapshot
            {
                FormatVersion = 1,
                SessionId = Guid.NewGuid().ToString("N"),
                CreatedUtc = DateTime.UtcNow,
                PhysicalInterfaceIndex = preparation.PhysicalInterfaceIndex,
                PhysicalInterfaceId = preparation.PhysicalInterfaceId,
                PhysicalInterfaceAlias = preparation.PhysicalInterfaceAlias,
                PhysicalIpv4 = preparation.PhysicalIpv4.ToString(),
                PhysicalGateway = preparation.PhysicalGateway.ToString(),
                OriginalDnsServers = preparation.OriginalDnsServers
                    .Select(value => value.ToString())
                    .ToList(),
                OriginalDnsWasAutomatic =
                    preparation.OriginalDnsWasAutomatic,
                AppliedDnsServers = LeakSafeDnsServers.ToList(),
                TunInterfaceIndex = adapter.InterfaceIndex,
                TunInterfaceId = adapter.InterfaceId,
                TunInterfaceAlias = adapter.InterfaceAlias,
                TunIpv4 = adapter.Ipv4Address.ToString(),
                ProxyEndpointIps = [preparation.ProxyEndpoint.ToString()],
                AppliedRoutes = routePlan.Select(route =>
                    new TunAppliedRoute
                    {
                        DestinationPrefix = route.DestinationPrefix,
                        InterfaceIndex = route.InterfaceIndex,
                        NextHop = route.NextHop
                    }).ToList()
            };

            SaveSnapshot(snapshot);

            try
            {
                // Порядок критичен: endpoint /32 обязан появиться раньше /1.
                foreach (TunRoutePlanItem route in routePlan)
                {
                    await AddRouteAsync(route, cancellationToken);
                }

                // Xray/Wintun may expose an APIPA address on the TUN adapter.
                // On some Windows builds both GetBestInterface and
                // Find-NetRoute return ERROR_HOST_UNREACHABLE (1232) even
                // though the explicit routes were installed successfully.
                // Verify the exact routes that GeniaProxy owns instead; the
                // DNS/HTTP probes below remain the end-to-end connectivity
                // check and rollback the snapshot on failure.
                foreach (TunRoutePlanItem route in routePlan)
                {
                    await VerifyAppliedRouteAsync(
                        route,
                        cancellationToken
                    );
                }

                await SetDnsAsync(
                    preparation.PhysicalInterfaceIndex,
                    LeakSafeDnsServers,
                    cancellationToken
                );

                await FlushDnsAsync(cancellationToken);

                // The .NET DNS resolver can return HostNotFound on Windows
                // while Xray TUN is already routing traffic correctly. Verify
                // the actual Windows DNS client path first, then independently
                // verify UDP DNS transport and HTTPS using the resolved IPv4.
                await VerifySystemDnsThroughTunWithRetryAsync(
                    cancellationToken
                );

                IPAddress resolvedAddress = await VerifyDirectDnsOverTunAsync(
                    cancellationToken
                );

                await VerifyHttpPinnedAsync(
                    resolvedAddress,
                    cancellationToken
                );
            }
            catch
            {
                try
                {
                    await RestoreAsync(CancellationToken.None);
                }
                catch
                {
                    // Backup остаётся на диске для crash recovery.
                }

                throw;
            }
        }

        public static async Task EnsureIpv6LeakSafeAsync(
            CancellationToken cancellationToken)
        {
            int? interfaceIndex = await TryFindBestInterfaceIndexAsync(
                PublicIpv6RouteProbe,
                cancellationToken
            );

            if (interfaceIndex is null)
            {
                return;
            }

            NetworkInterface? networkInterface =
                FindInterfaceByAnyIndex(interfaceIndex.Value);

            string interfaceName =
                networkInterface?.Name ?? "неизвестный интерфейс";

            throw new InvalidOperationException(
                "Обнаружен доступный системный IPv6 route через " +
                $"{interfaceName} (ifIndex {interfaceIndex.Value}). " +
                "TUN GeniaProxy 4.2.x работает в leak-safe IPv4 режиме " +
                "и не будет запущен, чтобы настоящий IPv6 не ушёл в обход " +
                "туннеля. Отключите IPv6 для этого подключения или " +
                "используйте локальный/системный прокси."
            );
        }

        // Compatibility wrapper for code compiled against the pre-HF4 name.
        public static Task EnsureSingBoxIpv6LeakSafeAsync(
            CancellationToken cancellationToken)
        {
            return EnsureIpv6LeakSafeAsync(cancellationToken);
        }

        public static async Task VerifySingBoxAsync(
            TunAdapterInfo adapter,
            CancellationToken cancellationToken)
        {
            // On Windows the TUN adapter can report Up slightly before
            // sing-box auto_route has finished updating the routing table.
            // Treat route readiness as a separate asynchronous stage instead
            // of failing the whole connection on the first observation.
            await WaitForBestInterfaceAsync(
                PublicRouteProbe,
                adapter.InterfaceIndex,
                "публичного IPv4",
                SingBoxRouteReadyTimeout,
                cancellationToken
            );

            // sing-box 1.13.x strict_route intentionally prevents Windows from
            // leaking DNS through non-TUN interfaces. Windows therefore needs
            // usable DNS servers attached to the TUN interface itself and a
            // preferred interface metric. Keep the physical NIC untouched.
            await ConfigureSingBoxTunInterfaceAsync(
                adapter,
                cancellationToken
            );

            await FlushDnsAsync(cancellationToken);

            // Verify the actual Windows DNS client path after the TUN DNS/metric
            // is configured. This catches the exact failure mode that browsers
            // and normal applications would otherwise see.
            await VerifySystemDnsThroughTunAsync(cancellationToken);

            // Keep the HF9 direct DNS + pinned HTTPS probes as transport checks.
            // They prove UDP DNS and TCP/TLS can traverse the TUN independently
            // from the Windows resolver implementation.
            IPAddress resolvedAddress = await VerifyDirectDnsOverTunAsync(
                cancellationToken
            );

            await VerifyHttpPinnedAsync(
                resolvedAddress,
                cancellationToken
            );
        }

        public async Task<TunRestoreOutcome> RestoreAsync(
            CancellationToken cancellationToken)
        {
            if (!HasPendingBackup)
            {
                return TunRestoreOutcome.NothingToRestore;
            }

            TunNetworkSnapshot snapshot = LoadSnapshot();
            bool dnsPreserved = false;

            // Сначала освобождаем full-route, затем DNS, и только потом
            // удаляем bypass proxy endpoint. Такой rollback не обрывает
            // восстановление сети на полпути.
            foreach (TunAppliedRoute route in snapshot.AppliedRoutes
                         .Where(route =>
                             route.DestinationPrefix is "0.0.0.0/1" or
                             "128.0.0.0/1"))
            {
                await RemoveRouteAsync(route, cancellationToken);
            }

            if (CurrentDnsMatchesApplied(snapshot))
            {
                if (snapshot.OriginalDnsWasAutomatic)
                {
                    await ResetDnsAsync(
                        snapshot.PhysicalInterfaceIndex,
                        cancellationToken
                    );
                }
                else
                {
                    await SetDnsAsync(
                        snapshot.PhysicalInterfaceIndex,
                        snapshot.OriginalDnsServers,
                        cancellationToken
                    );
                }

                await FlushDnsAsync(cancellationToken);
            }
            else
            {
                dnsPreserved = true;
            }

            foreach (TunAppliedRoute route in snapshot.AppliedRoutes
                         .Where(route =>
                             route.DestinationPrefix.EndsWith(
                                 "/32",
                                 StringComparison.Ordinal)))
            {
                await RemoveRouteAsync(route, cancellationToken);
            }

            File.Delete(BackupPath);

            return dnsPreserved
                ? TunRestoreOutcome.DnsPreserved
                : TunRestoreOutcome.Restored;
        }

        public static IReadOnlyList<TunRoutePlanItem> BuildXrayRoutePlan(
            XrayTunPreparation preparation,
            TunAdapterInfo adapter)
        {
            ArgumentNullException.ThrowIfNull(preparation);
            ArgumentNullException.ThrowIfNull(adapter);

            return
            [
                new TunRoutePlanItem(
                    preparation.ProxyEndpoint + "/32",
                    preparation.PhysicalInterfaceIndex,
                    preparation.PhysicalGateway.ToString()
                ),
                new TunRoutePlanItem(
                    "0.0.0.0/1",
                    adapter.InterfaceIndex,
                    adapter.Ipv4Address.ToString()
                ),
                new TunRoutePlanItem(
                    "128.0.0.0/1",
                    adapter.InterfaceIndex,
                    adapter.Ipv4Address.ToString()
                )
            ];
        }

        private static async Task<IPAddress> ResolveIpv4Async(
            string host,
            CancellationToken cancellationToken)
        {
            if (IPAddress.TryParse(host, out IPAddress? parsed))
            {
                if (parsed.AddressFamily != AddressFamily.InterNetwork)
                {
                    throw new NotSupportedException(
                        "Xray TUN 4.2.x поддерживает только IPv4 proxy endpoint."
                    );
                }

                return parsed;
            }

            IPAddress[] addresses = await Dns.GetHostAddressesAsync(
                host,
                cancellationToken
            );

            return addresses.FirstOrDefault(value =>
                       value.AddressFamily == AddressFamily.InterNetwork)
                   ?? throw new InvalidOperationException(
                       $"Не удалось разрешить IPv4 для {host}."
                   );
        }

        private static NetworkInterface? FindTunAdapter(string expectedName)
        {
            NetworkInterface[] interfaces =
                NetworkInterface.GetAllNetworkInterfaces();

            return interfaces.FirstOrDefault(value =>
                       string.Equals(
                           value.Name,
                           expectedName,
                           StringComparison.OrdinalIgnoreCase))
                   ?? interfaces.FirstOrDefault(value =>
                       string.Equals(
                           value.Description,
                           expectedName,
                           StringComparison.OrdinalIgnoreCase));
        }

        private static NetworkInterface? FindInterfaceByIndex(int index)
        {
            foreach (NetworkInterface networkInterface in
                     NetworkInterface.GetAllNetworkInterfaces())
            {
                try
                {
                    IPv4InterfaceProperties? properties =
                        networkInterface.GetIPProperties()
                            .GetIPv4Properties();

                    if (properties?.Index == index)
                    {
                        return networkInterface;
                    }
                }
                catch (NetworkInformationException)
                {
                    // Интерфейс без IPv4 пропускаем.
                }
            }

            return null;
        }

        private static NetworkInterface? FindInterfaceByAnyIndex(int index)
        {
            foreach (NetworkInterface networkInterface in
                     NetworkInterface.GetAllNetworkInterfaces())
            {
                IPInterfaceProperties properties =
                    networkInterface.GetIPProperties();

                try
                {
                    if (properties.GetIPv4Properties()?.Index == index)
                    {
                        return networkInterface;
                    }
                }
                catch (NetworkInformationException)
                {
                    // IPv4 на интерфейсе может отсутствовать.
                }

                try
                {
                    if (properties.GetIPv6Properties()?.Index == index)
                    {
                        return networkInterface;
                    }
                }
                catch (NetworkInformationException)
                {
                    // IPv6 на интерфейсе может отсутствовать.
                }
            }

            return null;
        }

        private static async Task<int?> TryFindBestInterfaceIndexAsync(
            IPAddress destination,
            CancellationToken cancellationToken)
        {
            string escapedDestination = destination.ToString()
                .Replace("'", "''", StringComparison.Ordinal);

            string script =
                "$ErrorActionPreference='Stop'; " +
                $"$route = Find-NetRoute -RemoteIPAddress '{escapedDestination}' " +
                "-ErrorAction SilentlyContinue | " +
                "Where-Object { $_.PSObject.Properties['DestinationPrefix'] } | " +
                "Select-Object -First 1; " +
                "if ($null -eq $route) { [Console]::Out.Write('NONE'); exit 0 }; " +
                "[Console]::Out.Write($route.InterfaceIndex.ToString(" +
                "[Globalization.CultureInfo]::InvariantCulture))";

            string stdout = await RunPowerShellCaptureAsync(
                script,
                cancellationToken
            );

            string value = stdout.Trim();
            if (string.Equals(value, "NONE", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (!int.TryParse(
                    value,
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out int interfaceIndex) ||
                interfaceIndex <= 0)
            {
                throw new InvalidOperationException(
                    $"Find-NetRoute вернул некорректный ifIndex для " +
                    $"{destination}: '{value}'."
                );
            }

            return interfaceIndex;
        }

        private static async Task WaitForBestInterfaceAsync(
            IPAddress destination,
            int expectedInterfaceIndex,
            string purpose,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            DateTime deadline = DateTime.UtcNow.Add(timeout);
            InvalidOperationException? lastFailure = null;

            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    await VerifyBestInterfaceAsync(
                        destination,
                        expectedInterfaceIndex,
                        purpose,
                        cancellationToken
                    );
                    return;
                }
                catch (InvalidOperationException ex)
                {
                    lastFailure = ex;
                }

                TimeSpan remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    break;
                }

                TimeSpan delay = remaining < SingBoxRoutePollInterval
                    ? remaining
                    : SingBoxRoutePollInterval;

                await Task.Delay(delay, cancellationToken);
            }

            string detail = lastFailure?.Message ??
                $"Best route для {purpose} ({destination}) не подтверждён.";

            throw new InvalidOperationException(
                $"sing-box TUN auto_route не стал активным за " +
                $"{timeout.TotalSeconds:0} с. Последняя проверка: {detail}",
                lastFailure
            );
        }

        private static async Task VerifyBestInterfaceAsync(
            IPAddress destination,
            int expectedInterfaceIndex,
            string purpose,
            CancellationToken cancellationToken)
        {
            int actual;
            uint nativeResult = TryGetBestInterfaceIndexNative(
                destination,
                out int nativeIndex
            );

            if (nativeResult == 0 && nativeIndex > 0)
            {
                actual = nativeIndex;
            }
            else if (nativeResult is 1231 or 1232)
            {
                // Wintun/Xray с APIPA и явно добавленными /1 может быть
                // корректно представлен в таблице маршрутов, но старый
                // GetBestInterface иногда возвращает NETWORK/HOST_UNREACHABLE.
                // В этом случае подтверждаем фактический route через
                // документированный NetTCPIP Find-NetRoute.
                actual = await FindBestInterfaceIndexAsync(
                    destination,
                    cancellationToken
                );
            }
            else
            {
                throw new InvalidOperationException(
                    $"Windows не смог определить best route для {destination}. " +
                    $"Код IP Helper: {nativeResult}."
                );
            }

            if (actual != expectedInterfaceIndex)
            {
                throw new InvalidOperationException(
                    $"Best route для {purpose} ({destination}) указывает " +
                    $"на ifIndex {actual}, ожидался {expectedInterfaceIndex}."
                );
            }
        }

        private static int GetBestInterfaceIndex(IPAddress destination)
        {
            uint result = TryGetBestInterfaceIndexNative(
                destination,
                out int bestIndex
            );

            if (result != 0 || bestIndex <= 0)
            {
                throw new InvalidOperationException(
                    $"Windows не смог определить best route для {destination}. " +
                    $"Код IP Helper: {result}."
                );
            }

            return bestIndex;
        }

        private static uint TryGetBestInterfaceIndexNative(
            IPAddress destination,
            out int bestIndex)
        {
            if (destination.AddressFamily != AddressFamily.InterNetwork)
            {
                throw new NotSupportedException(
                    "Проверка best route поддерживает только IPv4."
                );
            }

            uint address = BitConverter.ToUInt32(
                destination.GetAddressBytes(),
                0
            );
            uint result = GetBestInterface(address, out uint nativeIndex);
            bestIndex = nativeIndex == 0
                ? 0
                : checked((int)nativeIndex);
            return result;
        }

        private static async Task<int> FindBestInterfaceIndexAsync(
            IPAddress destination,
            CancellationToken cancellationToken)
        {
            string escapedDestination = destination.ToString()
                .Replace("'", "''", StringComparison.Ordinal);

            string script =
                "$ErrorActionPreference='Stop'; " +
                $"$route = Find-NetRoute -RemoteIPAddress '{escapedDestination}' " +
                "-ErrorAction Stop | " +
                "Where-Object { $_.PSObject.Properties['DestinationPrefix'] } | " +
                "Select-Object -First 1; " +
                "if ($null -eq $route) { throw 'Find-NetRoute did not return NetRoute.' }; " +
                "[Console]::Out.Write($route.InterfaceIndex.ToString(" +
                "[Globalization.CultureInfo]::InvariantCulture))";

            string stdout = await RunPowerShellCaptureAsync(
                script,
                cancellationToken
            );

            if (!int.TryParse(
                    stdout.Trim(),
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out int interfaceIndex) ||
                interfaceIndex <= 0)
            {
                throw new InvalidOperationException(
                    $"Find-NetRoute вернул некорректный ifIndex для " +
                    $"{destination}: '{stdout.Trim()}'."
                );
            }

            return interfaceIndex;
        }

        private static bool IsDnsAutomatic(string interfaceId)
        {
            string normalizedId = interfaceId.StartsWith('{')
                ? interfaceId
                : "{" + interfaceId + "}";

            string path =
                @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\" +
                normalizedId;

            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(path);

            if (key is null)
            {
                // Без доказательства static-конфигурации безопаснее считать,
                // что адреса DNS пришли автоматически.
                return true;
            }

            string? staticNameServer = key.GetValue("NameServer")?.ToString();
            return string.IsNullOrWhiteSpace(staticNameServer);
        }

        private static bool CurrentDnsMatchesApplied(TunNetworkSnapshot snapshot)
        {
            NetworkInterface? networkInterface =
                FindInterfaceByIndex(snapshot.PhysicalInterfaceIndex);

            if (networkInterface is null)
            {
                return false;
            }

            string[] current = networkInterface.GetIPProperties()
                .DnsAddresses
                .Where(value =>
                    value.AddressFamily == AddressFamily.InterNetwork)
                .Select(value => value.ToString())
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            string[] applied = snapshot.AppliedDnsServers
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return current.SequenceEqual(
                applied,
                StringComparer.OrdinalIgnoreCase
            );
        }

        private static async Task VerifyAppliedRouteAsync(
            TunRoutePlanItem route,
            CancellationToken cancellationToken)
        {
            string escapedPrefix = route.DestinationPrefix
                .Replace("'", "''", StringComparison.Ordinal);
            string script =
                "$ErrorActionPreference='Stop'; " +
                "$route = Get-NetRoute -AddressFamily IPv4 -ErrorAction Stop | " +
                $"Where-Object {{ $_.DestinationPrefix -eq '{escapedPrefix}' -and " +
                $"$_.InterfaceIndex -eq {route.InterfaceIndex} }} | " +
                "Select-Object -First 1; " +
                "if ($null -eq $route) { throw 'Expected route not found.' }; " +
                "[Console]::Out.Write($route.InterfaceIndex.ToString(" +
                "[Globalization.CultureInfo]::InvariantCulture))";

            try
            {
                string stdout = await RunPowerShellCaptureAsync(
                    script,
                    cancellationToken
                );

                if (!int.TryParse(
                        stdout.Trim(),
                        System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out int interfaceIndex) ||
                    interfaceIndex != route.InterfaceIndex)
                {
                    throw new InvalidOperationException(
                        $"Route {route.DestinationPrefix} подтверждён с " +
                        $"неожиданным ifIndex: '{stdout.Trim()}'."
                    );
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new InvalidOperationException(
                    $"Не удалось подтвердить TUN route {route.DestinationPrefix} " +
                    $"через ifIndex {route.InterfaceIndex}, gateway {route.NextHop}: " +
                    ex.Message,
                    ex
                );
            }
        }

        private static async Task EnsureDestinationPrefixAbsentAsync(
            string destinationPrefix,
            CancellationToken cancellationToken)
        {
            string script =
                "$ErrorActionPreference='Stop'; " +
                "$route = Get-NetRoute -AddressFamily IPv4 -ErrorAction Stop | " +
                $"Where-Object {{ $_.DestinationPrefix -eq '{destinationPrefix}' }} | " +
                "Select-Object -First 1; " +
                "if ($null -ne $route) { " +
                $"throw 'Route {destinationPrefix} already exists.' " +
                "}";

            try
            {
                await RunPowerShellAsync(script, cancellationToken);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"TUN preflight для route {destinationPrefix} не выполнен: " +
                    ex.Message,
                    ex
                );
            }
        }

        private static async Task AddRouteAsync(
            TunRoutePlanItem route,
            CancellationToken cancellationToken)
        {
            (string destination, string mask) =
                GetRouteExeAddressAndMask(route.DestinationPrefix);

            string path = Path.Combine(
                Environment.SystemDirectory,
                "route.exe"
            );

            try
            {
                await RunProcessAsync(
                    path,
                    [
                        "ADD",
                        destination,
                        "MASK",
                        mask,
                        route.NextHop,
                        "METRIC",
                        "1",
                        "IF",
                        route.InterfaceIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    ],
                    cancellationToken
                );
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Не удалось добавить TUN route {route.DestinationPrefix} " +
                    $"через ifIndex {route.InterfaceIndex}, gateway {route.NextHop}: " +
                    ex.Message,
                    ex
                );
            }
        }

        private static async Task RemoveRouteAsync(
            TunAppliedRoute route,
            CancellationToken cancellationToken)
        {
            string escapedPrefix = EscapePowerShellSingleQuotedString(
                route.DestinationPrefix
            );
            string escapedNextHop = EscapePowerShellSingleQuotedString(
                route.NextHop
            );

            string script =
                "$ErrorActionPreference='Stop'; " +
                "$routes = @(Get-NetRoute -AddressFamily IPv4 -ErrorAction Stop | " +
                $"Where-Object {{ $_.DestinationPrefix -eq '{escapedPrefix}' -and " +
                $"$_.InterfaceIndex -eq {route.InterfaceIndex} -and " +
                $"$_.NextHop -eq '{escapedNextHop}' }}); " +
                "if ($routes.Count -gt 0) { " +
                "$routes | Remove-NetRoute -Confirm:$false -ErrorAction Stop " +
                "}";

            try
            {
                await RunPowerShellAsync(script, cancellationToken);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Не удалось удалить TUN route {route.DestinationPrefix} " +
                    $"с ifIndex {route.InterfaceIndex}: " + ex.Message,
                    ex
                );
            }
        }

        private static string EscapePowerShellSingleQuotedString(
            string value)
        {
            ArgumentNullException.ThrowIfNull(value);

            return value.Replace(
                "'",
                "''",
                StringComparison.Ordinal
            );
        }

        private static (string Destination, string Mask)
            GetRouteExeAddressAndMask(string destinationPrefix)
        {
            string[] parts = destinationPrefix.Split('/', 2);

            if (parts.Length != 2 ||
                !IPAddress.TryParse(parts[0], out IPAddress? destination) ||
                destination.AddressFamily != AddressFamily.InterNetwork ||
                !int.TryParse(parts[1], out int prefixLength) ||
                prefixLength is < 0 or > 32)
            {
                throw new InvalidDataException(
                    $"Некорректный IPv4 prefix для route.exe: {destinationPrefix}."
                );
            }

            uint maskValue = prefixLength == 0
                ? 0U
                : uint.MaxValue << (32 - prefixLength);

            byte[] maskBytes =
            [
                (byte)(maskValue >> 24),
                (byte)(maskValue >> 16),
                (byte)(maskValue >> 8),
                (byte)maskValue
            ];

            return (
                destination.ToString(),
                new IPAddress(maskBytes).ToString()
            );
        }

        private static async Task ConfigureSingBoxTunInterfaceAsync(
            TunAdapterInfo adapter,
            CancellationToken cancellationToken)
        {
            try
            {
                await SetDnsAsync(
                    adapter.InterfaceIndex,
                    LeakSafeDnsServers,
                    cancellationToken
                );

                await SetTunInterfaceMetricAsync(
                    adapter.InterfaceIndex,
                    SingBoxTunInterfaceMetric,
                    cancellationToken
                );
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new InvalidOperationException(
                    $"Не удалось настроить DNS/metric для sing-box TUN " +
                    $"{adapter.InterfaceAlias} (ifIndex {adapter.InterfaceIndex}): " +
                    ex.Message,
                    ex
                );
            }
        }

        private static async Task SetTunInterfaceMetricAsync(
            int interfaceIndex,
            int interfaceMetric,
            CancellationToken cancellationToken)
        {
            if (interfaceMetric is < 1 or > 9999)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(interfaceMetric),
                    "Метрика TUN должна быть от 1 до 9999."
                );
            }

            string script =
                "$ErrorActionPreference='Stop'; " +
                $"Set-NetIPInterface -InterfaceIndex {interfaceIndex} " +
                "-AddressFamily IPv4 -AutomaticMetric Disabled " +
                $"-InterfaceMetric {interfaceMetric} -ErrorAction Stop";

            await RunPowerShellAsync(script, cancellationToken);
        }

        private static async Task VerifySystemDnsThroughTunWithRetryAsync(
            CancellationToken cancellationToken)
        {
            const int maxAttempts = 2;
            TimeSpan retryDelay = TimeSpan.FromSeconds(5);

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    await VerifySystemDnsThroughTunAsync(cancellationToken);
                    return;
                }
                catch (InvalidOperationException) when (attempt < maxAttempts)
                {
                    Debug.WriteLine(
                        $"Windows system DNS readiness retry {attempt + 1}/{maxAttempts} " +
                        $"через {retryDelay.TotalSeconds:0} с."
                    );

                    await Task.Delay(retryDelay, cancellationToken);
                }
            }
        }

        private static async Task VerifySystemDnsThroughTunAsync(
            CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource
                .CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));

            const string host = "www.microsoft.com";
            string script =
                "$ErrorActionPreference='Stop'; " +
                $"$answer = Resolve-DnsName '{host}' -Type A -DnsOnly " +
                "-QuickTimeout -ErrorAction Stop | " +
                "Where-Object { $_.IPAddress } | " +
                "Select-Object -ExpandProperty IPAddress -First 1; " +
                "if ([string]::IsNullOrWhiteSpace($answer)) { " +
                "throw 'No IPv4 DNS answer.' }; " +
                "[Console]::Out.Write($answer)";

            try
            {
                string stdout = await RunPowerShellCaptureAsync(
                    script,
                    timeout.Token
                );

                if (!IPAddress.TryParse(stdout.Trim(), out IPAddress? address) ||
                    address.AddressFamily != AddressFamily.InterNetwork)
                {
                    throw new InvalidOperationException(
                        $"Windows DNS probe вернул некорректный IPv4: '{stdout.Trim()}'."
                    );
                }
            }
            catch (OperationCanceledException ex)
                when (!cancellationToken.IsCancellationRequested)
            {
                throw new InvalidOperationException(
                    "Windows system DNS через TUN превысил таймаут 5 с.",
                    ex
                );
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new InvalidOperationException(
                    "Windows system DNS через TUN не работает: " +
                    ex.Message,
                    ex
                );
            }
        }

        private static async Task SetDnsAsync(
            int interfaceIndex,
            IEnumerable<string> servers,
            CancellationToken cancellationToken)
        {
            string[] normalized = servers
                .Select(value => IPAddress.Parse(value).ToString())
                .ToArray();

            if (normalized.Length == 0)
            {
                await ResetDnsAsync(interfaceIndex, cancellationToken);
                return;
            }

            string values = string.Join(
                ",",
                normalized.Select(value => $"'{value}'")
            );

            string script =
                "$ErrorActionPreference='Stop'; " +
                $"Set-DnsClientServerAddress -InterfaceIndex {interfaceIndex} " +
                $"-ServerAddresses @({values})";

            await RunPowerShellAsync(script, cancellationToken);
        }

        private static Task SetDnsAsync(
            int interfaceIndex,
            IEnumerable<IPAddress> servers,
            CancellationToken cancellationToken)
        {
            return SetDnsAsync(
                interfaceIndex,
                servers.Select(value => value.ToString()),
                cancellationToken
            );
        }

        private static async Task ResetDnsAsync(
            int interfaceIndex,
            CancellationToken cancellationToken)
        {
            string script =
                "$ErrorActionPreference='Stop'; " +
                $"Set-DnsClientServerAddress -InterfaceIndex {interfaceIndex} " +
                "-ResetServerAddresses";

            await RunPowerShellAsync(script, cancellationToken);
        }

        private static async Task FlushDnsAsync(
            CancellationToken cancellationToken)
        {
            string path = Path.Combine(
                Environment.SystemDirectory,
                "ipconfig.exe"
            );

            await RunProcessAsync(
                path,
                ["/flushdns"],
                cancellationToken
            );
        }

        private static async Task<IPAddress> VerifyDirectDnsOverTunAsync(
            CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource
                .CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));

            const string host = "www.cloudflare.com";
            ushort transactionId = (ushort)Random.Shared.Next(1, ushort.MaxValue);
            byte[] query = BuildDnsAQuery(host, transactionId);

            using var udp = new UdpClient(AddressFamily.InterNetwork);
            var endpoint = new IPEndPoint(PublicRouteProbe, 53);

            try
            {
                await udp.SendAsync(
                    query,
                    endpoint,
                    timeout.Token
                );

                UdpReceiveResult result = await udp.ReceiveAsync(
                    timeout.Token
                );

                return ParseDnsAResponse(
                    result.Buffer,
                    transactionId,
                    host
                );
            }
            catch (OperationCanceledException ex)
                when (!cancellationToken.IsCancellationRequested)
            {
                throw new InvalidOperationException(
                    "Прямой DNS probe через TUN (1.1.1.1:53) превысил " +
                    "таймаут 5 с.",
                    ex
                );
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new InvalidOperationException(
                    "Прямой DNS probe через TUN (1.1.1.1:53) не выполнен: " +
                    ex.Message,
                    ex
                );
            }
        }

        private static byte[] BuildDnsAQuery(
            string host,
            ushort transactionId)
        {
            using var stream = new MemoryStream();

            WriteDnsUInt16(stream, transactionId);
            WriteDnsUInt16(stream, 0x0100); // recursion desired
            WriteDnsUInt16(stream, 1);      // QDCOUNT
            WriteDnsUInt16(stream, 0);      // ANCOUNT
            WriteDnsUInt16(stream, 0);      // NSCOUNT
            WriteDnsUInt16(stream, 0);      // ARCOUNT

            foreach (string label in host.Split('.', StringSplitOptions.RemoveEmptyEntries))
            {
                byte[] bytes = Encoding.ASCII.GetBytes(label);

                if (bytes.Length is 0 or > 63)
                {
                    throw new InvalidDataException(
                        $"Некорректная DNS-метка в имени '{host}'."
                    );
                }

                stream.WriteByte((byte)bytes.Length);
                stream.Write(bytes, 0, bytes.Length);
            }

            stream.WriteByte(0);
            WriteDnsUInt16(stream, 1); // QTYPE=A
            WriteDnsUInt16(stream, 1); // QCLASS=IN

            return stream.ToArray();
        }

        private static IPAddress ParseDnsAResponse(
            byte[] response,
            ushort transactionId,
            string host)
        {
            if (response.Length < 12)
            {
                throw new InvalidDataException(
                    "DNS-ответ короче заголовка."
                );
            }

            ushort responseId = ReadDnsUInt16(response, 0);
            ushort flags = ReadDnsUInt16(response, 2);
            ushort questionCount = ReadDnsUInt16(response, 4);
            ushort answerCount = ReadDnsUInt16(response, 6);
            int rcode = flags & 0x000F;

            if (responseId != transactionId)
            {
                throw new InvalidDataException(
                    "DNS-ответ имеет неожиданный transaction ID."
                );
            }

            if ((flags & 0x8000) == 0)
            {
                throw new InvalidDataException(
                    "Полученный DNS-пакет не является ответом."
                );
            }

            if (rcode != 0)
            {
                throw new InvalidDataException(
                    $"DNS-сервер вернул RCODE={rcode}."
                );
            }

            int offset = 12;

            for (int index = 0; index < questionCount; index++)
            {
                offset = SkipDnsName(response, offset);
                EnsureDnsBytesAvailable(response, offset, 4);
                offset += 4; // QTYPE + QCLASS
            }

            for (int index = 0; index < answerCount; index++)
            {
                offset = SkipDnsName(response, offset);
                EnsureDnsBytesAvailable(response, offset, 10);

                ushort type = ReadDnsUInt16(response, offset);
                ushort dnsClass = ReadDnsUInt16(response, offset + 2);
                ushort dataLength = ReadDnsUInt16(response, offset + 8);
                offset += 10;

                EnsureDnsBytesAvailable(response, offset, dataLength);

                if (type == 1 && dnsClass == 1 && dataLength == 4)
                {
                    return new IPAddress(
                        new byte[]
                        {
                            response[offset],
                            response[offset + 1],
                            response[offset + 2],
                            response[offset + 3]
                        }
                    );
                }

                offset += dataLength;
            }

            throw new InvalidDataException(
                $"DNS probe не вернул A-запись для {host}."
            );
        }

        private static int SkipDnsName(byte[] message, int offset)
        {
            int labels = 0;

            while (true)
            {
                EnsureDnsBytesAvailable(message, offset, 1);
                byte length = message[offset];

                if (length == 0)
                {
                    return offset + 1;
                }

                if ((length & 0xC0) == 0xC0)
                {
                    EnsureDnsBytesAvailable(message, offset, 2);
                    return offset + 2;
                }

                if ((length & 0xC0) != 0 || length > 63)
                {
                    throw new InvalidDataException(
                        "DNS-ответ содержит некорректное имя."
                    );
                }

                offset += 1 + length;
                labels++;

                if (labels > 128)
                {
                    throw new InvalidDataException(
                        "DNS-ответ содержит слишком длинное имя."
                    );
                }
            }
        }

        private static void EnsureDnsBytesAvailable(
            byte[] message,
            int offset,
            int count)
        {
            if (offset < 0 || count < 0 || offset > message.Length - count)
            {
                throw new InvalidDataException(
                    "DNS-ответ имеет некорректную длину."
                );
            }
        }

        private static ushort ReadDnsUInt16(byte[] buffer, int offset)
        {
            EnsureDnsBytesAvailable(buffer, offset, 2);
            return (ushort)((buffer[offset] << 8) | buffer[offset + 1]);
        }

        private static void WriteDnsUInt16(Stream stream, ushort value)
        {
            stream.WriteByte((byte)(value >> 8));
            stream.WriteByte((byte)value);
        }

        private static async Task VerifyHttpPinnedAsync(
            IPAddress pinnedAddress,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(pinnedAddress);

            if (pinnedAddress.AddressFamily != AddressFamily.InterNetwork)
            {
                throw new NotSupportedException(
                    "Pinned HTTP probe поддерживает только IPv4."
                );
            }

            using var handler = new SocketsHttpHandler
            {
                UseProxy = false,
                ConnectTimeout = TimeSpan.FromSeconds(6),
                ConnectCallback = (context, token) =>
                    ConnectPinnedIpv4Async(
                        pinnedAddress,
                        context.DnsEndPoint.Port,
                        token
                    )
            };

            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(10)
            };

            try
            {
                using HttpResponseMessage response = await client.GetAsync(
                    "https://www.cloudflare.com/cdn-cgi/trace",
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken
                );

                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException(
                        "Pinned IPv4 HTTP probe через TUN завершился с кодом " +
                        (int)response.StatusCode + "."
                    );
                }
            }
            catch (OperationCanceledException ex)
                when (!cancellationToken.IsCancellationRequested)
            {
                throw new InvalidOperationException(
                    "Pinned IPv4 HTTP probe через TUN превысил таймаут.",
                    ex
                );
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new InvalidOperationException(
                    "Pinned IPv4 HTTP probe через TUN не выполнен: " +
                    ex.Message,
                    ex
                );
            }
        }

        private static async ValueTask<Stream> ConnectPinnedIpv4Async(
            IPAddress address,
            int port,
            CancellationToken cancellationToken)
        {
            var socket = new Socket(
                AddressFamily.InterNetwork,
                SocketType.Stream,
                ProtocolType.Tcp
            )
            {
                NoDelay = true
            };

            try
            {
                await socket.ConnectAsync(
                    new IPEndPoint(address, port),
                    cancellationToken
                );

                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        private static async Task VerifyDnsAsync(
            CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource
                .CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));

            try
            {
                IPAddress[] result = await Dns.GetHostAddressesAsync(
                    "www.cloudflare.com",
                    AddressFamily.InterNetwork,
                    timeout.Token
                );

                if (result.Length == 0)
                {
                    throw new InvalidOperationException(
                        "DNS probe через TUN не вернул IPv4-адрес."
                    );
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new InvalidOperationException(
                    "IPv4 DNS probe через TUN не выполнен: " + ex.Message,
                    ex
                );
            }
        }

        private static async Task VerifyHttpAsync(
            CancellationToken cancellationToken)
        {
            using var handler = new SocketsHttpHandler
            {
                UseProxy = false,
                ConnectTimeout = TimeSpan.FromSeconds(6),
                ConnectCallback = ConnectIpv4Async
            };

            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(10)
            };

            try
            {
                using HttpResponseMessage response = await client.GetAsync(
                    "https://www.cloudflare.com/cdn-cgi/trace",
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken
                );

                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException(
                        "IPv4 HTTP probe через TUN завершился с кодом " +
                        (int)response.StatusCode + "."
                    );
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new InvalidOperationException(
                    "IPv4 HTTP probe через TUN не выполнен: " + ex.Message,
                    ex
                );
            }
        }

        private static async ValueTask<Stream> ConnectIpv4Async(
            SocketsHttpConnectionContext context,
            CancellationToken cancellationToken)
        {
            IPAddress[] addresses = await Dns.GetHostAddressesAsync(
                context.DnsEndPoint.Host,
                AddressFamily.InterNetwork,
                cancellationToken
            );

            IPAddress address = addresses.FirstOrDefault()
                ?? throw new SocketException((int)SocketError.HostNotFound);

            var socket = new Socket(
                AddressFamily.InterNetwork,
                SocketType.Stream,
                ProtocolType.Tcp
            )
            {
                NoDelay = true
            };

            try
            {
                await socket.ConnectAsync(
                    new IPEndPoint(address, context.DnsEndPoint.Port),
                    cancellationToken
                );

                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        private static async Task RunPowerShellAsync(
            string script,
            CancellationToken cancellationToken)
        {
            _ = await RunPowerShellCaptureAsync(script, cancellationToken);
        }

        private static async Task<string> RunPowerShellCaptureAsync(
            string script,
            CancellationToken cancellationToken)
        {
            string path = Path.Combine(
                Environment.SystemDirectory,
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe"
            );

            const string utf8Preamble =
                "$utf8 = New-Object System.Text.UTF8Encoding($false); " +
                "[Console]::OutputEncoding = $utf8; " +
                "$OutputEncoding = $utf8; ";

            return await RunProcessAsync(
                path,
                [
                    "-NoLogo",
                    "-NoProfile",
                    "-NonInteractive",
                    "-ExecutionPolicy",
                    "Bypass",
                    "-Command",
                    utf8Preamble + script
                ],
                cancellationToken
            );
        }

        private static async Task<string> RunProcessAsync(
            string executable,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = new Process
            {
                StartInfo = startInfo
            };

            if (!process.Start())
            {
                throw new InvalidOperationException(
                    $"Не удалось запустить {Path.GetFileName(executable)}."
                );
            }

            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    // Сохраняем исходное исключение.
                }

                throw;
            }

            string stdout = await stdoutTask;
            string stderr = await stderrTask;

            if (process.ExitCode != 0)
            {
                string detail = string.IsNullOrWhiteSpace(stderr)
                    ? stdout.Trim()
                    : stderr.Trim();

                throw new InvalidOperationException(
                    $"{Path.GetFileName(executable)} завершился с кодом " +
                    $"{process.ExitCode}. {detail}".Trim()
                );
            }

            return stdout.Trim();
        }

        private void SaveSnapshot(TunNetworkSnapshot snapshot)
        {
            string json = JsonSerializer.Serialize(snapshot, JsonOptions);

            AtomicFileWriter.WriteAllText(
                BackupPath,
                json,
                new UTF8Encoding(false)
            );
        }

        private TunNetworkSnapshot LoadSnapshot()
        {
            var file = new FileInfo(BackupPath);

            if (!file.Exists)
            {
                throw new FileNotFoundException(
                    "Резервная копия TUN не найдена.",
                    BackupPath
                );
            }

            if (file.Length > 256 * 1024)
            {
                throw new InvalidDataException(
                    "Резервная копия TUN имеет недопустимый размер."
                );
            }

            TunNetworkSnapshot snapshot = JsonSerializer.Deserialize<
                    TunNetworkSnapshot>(
                    File.ReadAllText(BackupPath, Encoding.UTF8),
                    JsonOptions
                )
                ?? throw new InvalidDataException(
                    "Резервная копия TUN пуста."
                );

            ValidateSnapshotForRestore(snapshot);
            return snapshot;
        }

        public static void ValidateSnapshotForRestore(
            TunNetworkSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);

            if (snapshot.FormatVersion != 1 ||
                snapshot.PhysicalInterfaceIndex <= 0 ||
                snapshot.TunInterfaceIndex <= 0 ||
                snapshot.AppliedRoutes is null ||
                snapshot.AppliedRoutes.Count != 3 ||
                snapshot.AppliedRoutes.Any(route => route is null) ||
                snapshot.ProxyEndpointIps is null ||
                snapshot.ProxyEndpointIps.Count != 1 ||
                snapshot.OriginalDnsServers is null ||
                snapshot.AppliedDnsServers is null)
            {
                throw new InvalidDataException(
                    "Резервная копия TUN имеет неподдерживаемый формат."
                );
            }

            if (!Guid.TryParseExact(
                    snapshot.SessionId,
                    "N",
                    out _))
            {
                throw new InvalidDataException(
                    "Резервная копия TUN содержит некорректный sessionId."
                );
            }

            _ = ParseSnapshotIpv4(
                snapshot.PhysicalIpv4,
                "physicalIpv4"
            );
            IPAddress physicalGateway = ParseSnapshotIpv4(
                snapshot.PhysicalGateway,
                "physicalGateway"
            );
            IPAddress tunIpv4 = ParseSnapshotIpv4(
                snapshot.TunIpv4,
                "tunIpv4"
            );
            IPAddress proxyEndpoint = ParseSnapshotIpv4(
                snapshot.ProxyEndpointIps[0],
                "proxyEndpointIps[0]"
            );

            foreach (string dnsServer in snapshot.OriginalDnsServers)
            {
                _ = ParseSnapshotIpv4(
                    dnsServer,
                    "originalDnsServers"
                );
            }

            string[] appliedDns = snapshot.AppliedDnsServers
                .Select(value => ParseSnapshotIpv4(
                    value,
                    "appliedDnsServers"
                ).ToString())
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();

            string[] expectedDns = LeakSafeDnsServers
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();

            if (!appliedDns.SequenceEqual(
                    expectedDns,
                    StringComparer.Ordinal))
            {
                throw new InvalidDataException(
                    "Резервная копия TUN содержит неожиданные applied DNS."
                );
            }

            string proxyPrefix = proxyEndpoint + "/32";

            ValidateOwnedSnapshotRoute(
                snapshot.AppliedRoutes,
                proxyPrefix,
                snapshot.PhysicalInterfaceIndex,
                physicalGateway
            );
            ValidateOwnedSnapshotRoute(
                snapshot.AppliedRoutes,
                "0.0.0.0/1",
                snapshot.TunInterfaceIndex,
                tunIpv4
            );
            ValidateOwnedSnapshotRoute(
                snapshot.AppliedRoutes,
                "128.0.0.0/1",
                snapshot.TunInterfaceIndex,
                tunIpv4
            );

            string[] destinations = snapshot.AppliedRoutes
                .Select(route => route.DestinationPrefix)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();

            string[] expectedDestinations =
            [
                "0.0.0.0/1",
                "128.0.0.0/1",
                proxyPrefix
            ];

            Array.Sort(
                expectedDestinations,
                StringComparer.Ordinal
            );

            if (!destinations.SequenceEqual(
                    expectedDestinations,
                    StringComparer.Ordinal))
            {
                throw new InvalidDataException(
                    "Резервная копия TUN содержит неожиданный маршрут."
                );
            }

        }

        private static void ValidateOwnedSnapshotRoute(
            IReadOnlyList<TunAppliedRoute> routes,
            string destinationPrefix,
            int interfaceIndex,
            IPAddress nextHop)
        {
            TunAppliedRoute[] matches = routes
                .Where(route => string.Equals(
                    route.DestinationPrefix,
                    destinationPrefix,
                    StringComparison.Ordinal))
                .ToArray();

            if (matches.Length != 1)
            {
                throw new InvalidDataException(
                    $"Резервная копия TUN должна содержать ровно один " +
                    $"маршрут {destinationPrefix}."
                );
            }

            TunAppliedRoute route = matches[0];

            if (route.InterfaceIndex != interfaceIndex ||
                !IPAddress.TryParse(
                    route.NextHop,
                    out IPAddress? parsedNextHop) ||
                parsedNextHop.AddressFamily !=
                    AddressFamily.InterNetwork ||
                !parsedNextHop.Equals(nextHop))
            {
                throw new InvalidDataException(
                    $"Маршрут {destinationPrefix} в резервной копии " +
                    "TUN не соответствует сохранённой топологии."
                );
            }
        }

        private static IPAddress ParseSnapshotIpv4(
            string value,
            string fieldName)
        {
            if (!IPAddress.TryParse(
                    value,
                    out IPAddress? parsed) ||
                parsed.AddressFamily != AddressFamily.InterNetwork)
            {
                throw new InvalidDataException(
                    $"Резервная копия TUN содержит некорректное " +
                    $"поле {fieldName}."
                );
            }

            return parsed;
        }

        [DllImport("iphlpapi.dll", SetLastError = false)]
        private static extern uint GetBestInterface(
            uint dwDestAddr,
            out uint pdwBestIfIndex);

    }
}

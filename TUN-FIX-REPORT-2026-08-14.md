# GeniaProxy 4.2.0 — отчёт по TUN и инструкция по исправлению

Дата диагностики: 2026-08-14

## 1. Итог

Xray TUN в GeniaProxy 4.2.0 не сломан на уровне Wintun, Xray inbound, VLESS/XHTTP/XMUX или VPS. Ручные тесты доказали, что полный IPv4-трафик Windows успешно проходит через `geniaproxy-tun -> Xray -> VLESS + XHTTP + XMUX -> VPS`, а Cloudflare видит внешний IP `203.0.113.10`.

Корневая проблема находится в orchestration Windows: bundled Xray 26.3.27 создаёт Wintun-интерфейс, но GeniaProxy 4.2.0 ошибочно рассчитывает, что поля `gateway`, `dns`, `autoSystemRoutingTable` и `autoOutboundsInterface` автоматически выполнят Windows-настройку. В текущей версии ядра на Windows этого не произошло. Дополнительно `ConnectionSession.WaitForTunReadyAsync()` ждёт лишь 700 мс и проверяет только, что процесс ядра жив.

Для Xray TUN GeniaProxy должен сам управлять Windows IPv4 routes, DNS, readiness и rollback. Sing-box TUN следует оставить на его собственных `auto_route/strict_route`, но его readiness тоже нужно сделать фактическим, а не временным.

## 2. Что уже подтверждено реальными тестами

- `xray.exe` — 26.3.27, windows/amd64.
- `wintun.dll` находится рядом с `xray.exe` и загружается.
- Xray создаёт Wintun adapter и переводит его в `Up`.
- В Xray 26.3.27 заданный `gateway` не назначился: Windows выдала APIPA `169.254.167.8`.
- Маршрут `/32` к Cloudflare через TUN приводит пакет в Xray TUN inbound.
- `freedom/direct` без bypass создаёт routing loop — это подтверждено повторяющимися Xray-соединениями через TUN.
- При VLESS/XHTTP outbound и отдельном физическом пути к VPS routing loop исчезает.
- Cloudflare через TUN показал `ip=203.0.113.10`.
- Полный IPv4 через `0.0.0.0/1` и `128.0.0.0/1` работает.
- `/32 203.0.113.10` остаётся через физический интерфейс `Кабель`, gateway `192.168.0.1`.
- Публичный DNS-запрос через TUN работает: OpenDNS увидел `203.0.113.10`.
- Системный DNS `192.168.0.1` является потенциальным DNS-leak, поскольку локальная сеть `/24` более специфична, чем `/1` TUN routes.
- После временной замены DNS на `1.1.1.1, 1.0.0.1` системное разрешение имён и HTTPS через TUN работают.
- На тестовой машине отсутствует IPv6 default route `::/0`; IPv6 сейчас фактически fail-closed.

## 3. Найденные проблемы в исходнике 4.2.0

### P0-1. Xray TUN конфиг полагается на неподходящий Windows auto-routing

Файл: `Services/XrayProfileImportService.cs`, текущие строки примерно 286-303.

Сейчас `CreateTunInbound()` формирует:

```json
{
  "name": "GeniaProxy",
  "desc": "GeniaProxy",
  "mtu": 1500,
  "gateway": ["10.29.0.1/30"],
  "dns": ["1.1.1.1", "8.8.8.8"],
  "autoSystemRoutingTable": ["0.0.0.0/0"],
  "autoOutboundsInterface": "auto"
}
```

Bundled Xray 26.3.27 выпущен раньше Windows-реализации этих helper-параметров. Поля принимаются конфигом, но проведённый Windows-тест показал, что адрес и системные маршруты не были применены.

**Исправление:** для bundled Xray 26.3.27 сделать TUN inbound минимальным и детерминированным:

```json
{
  "tag": "tun-in",
  "protocol": "tun",
  "settings": {
    "name": "geniaproxy-tun",
    "desc": "GeniaProxy",
    "mtu": 1500
  }
}
```

Windows routes/DNS должен настраивать отдельный сервис GeniaProxy. Не менять сохранённый профиль пользователя — только runtime config.

### P0-2. `WaitForTunReadyAsync()` не проверяет TUN

Файл: `Services/ConnectionSession.cs`, строки примерно 499-510.

Текущий код:

```csharp
await Task.Delay(700, cancellationToken);
if (!coreManager.IsRunning) ...
```

Это не readiness. Adapter может быть `Up`, но Windows default route останется физическим, что и было обнаружено.

**Исправление:** разбить readiness на стадии:

1. core process alive;
2. TUN adapter найден;
3. adapter `OperationalStatus.Up`;
4. у adapter есть usable IPv4 (APIPA `169.254/16` допустим для Xray 26.3.27);
5. для Xray применён физический bypass к proxy endpoint;
6. `0.0.0.0/1` и `128.0.0.0/1` применены через TUN;
7. best route к публичному IPv4 указывает в TUN;
8. best route к proxy endpoint остаётся физическим;
9. DNS настроен leak-safe;
10. DNS probe успешен;
11. IPv6 policy проверена;
12. HTTP probe с `UseProxy=false` успешен;
13. только после этого выставлять `Running`.

Timeout: 10-15 секунд, polling 100-200 мс. В `TimeoutException` указывать последнюю стадию.

### P0-3. Нет Windows TUN network transaction / rollback

`ConnectionSession.StopAsync`, `CleanupFailedStartAsync`, `HandleUnexpectedExit` и `Dispose` умеют восстанавливать System Proxy, но не знают про TUN routes/DNS.

**Исправление:** добавить отдельный сервис, например:

- `Services/WindowsTunNetworkService.cs`
- `Models/TunNetworkSnapshot.cs`

или `TunNetworkSession : IAsyncDisposable`.

Сервис должен применять сетевые изменения транзакционно и уметь откатывать частично применённую конфигурацию.

### P0-4. Нет crash recovery для DNS/routes

Если GeniaProxy или Xray аварийно завершится после установки `/1` routes и DNS, Windows может остаться в изменённом состоянии.

**Исправление:** хранить `data/tun-network-backup.json`, аналогично `SystemProxyService`.

Snapshot минимум:

```text
formatVersion
sessionId
createdUtc
physicalInterfaceIndex
physicalInterfaceGuid
physicalInterfaceAlias
physicalIpv4
physicalGateway
originalDnsServers
originalDnsWasAutomatic
appliedDnsServers
tunInterfaceIndex
tunInterfaceAlias
proxyEndpointIps
appliedRoutes
```

При старте приложения, если backup остался, выполнять безопасное восстановление. DNS восстанавливать только если текущая конфигурация всё ещё совпадает с той, которую применил GeniaProxy; иначе не перетирать внешние изменения.

### P0-5. DNS leak

Активный физический DNS в тесте: `192.168.0.1`. Локальный `192.168.0.0/24` route идёт напрямую, поэтому такой DNS обходит `/1` TUN routes.

**Исправление:** после установки TUN routes временно назначать активному физическому интерфейсу публичные IPv4 DNS, например:

```text
1.1.1.1
1.0.0.1
```

Они попадают под `/1` и уходят через TUN. На Disconnect вернуть исходный DNS **с сохранением режима DHCP/static**, а не просто списка адресов.

### P0-6. IPv6 leak policy отсутствует

На тестовой машине `::/0` отсутствует, поэтому leak не наблюдался. На другой сети IPv6 default route может существовать.

**Исправление для безопасного 4.2.x hotfix:** перед Connected проверять физический IPv6 default route. Пока полноценный IPv6 TUN не протестирован, при наличии рабочего `::/0` не объявлять TUN leak-safe. Наиболее безопасный первый вариант — отказать в TUN-подключении с понятным сообщением и не менять IPv6 молча.

Позже можно реализовать и протестировать отдельную IPv6 policy (TUN routes или firewall/WFP). Не включать непроверенный IPv6 full-route в текущий hotfix.

## 4. Рекомендуемая архитектура

### Новый `WindowsTunNetworkService`

Ответственность:

- определить физический best route до включения TUN;
- получить interface index/alias, локальный IPv4 и gateway;
- сохранить DNS snapshot и способ его получения (automatic/static);
- создать proxy bypass `/32`;
- добавить TUN `/1` routes;
- настроить DNS;
- проверить effective routes;
- восстановить всё в обратном порядке;
- восстановить stale backup при следующем запуске.

Не помещать route/DNS логику в `MainWindow.xaml.cs`.

### Новый `XrayTunRuntimeConfigService`

Для текущего поддерживаемого сценария VLESS+XHTTP:

1. до изменения routes прочитать upstream host/port из `settings.vnext[0]`;
2. если `address` — hostname, разрешить IPv4 через текущую физическую сеть;
3. выбрать конкретный IPv4 endpoint;
4. в runtime JSON заменить только `vnext[0].address` на resolved IPv4;
5. `tlsSettings.serverName` и `xhttpSettings.host` оставить исходными;
6. исходный профиль в `data/profiles` не менять.

Так Xray после включения TUN не зависит от bootstrap DNS для proxy endpoint.

Если импортированный Xray JSON не имеет поддерживаемого `vless/settings/vnext` endpoint, TUN hotfix должен завершиться понятной ошибкой, а не угадывать адрес. Local/SystemProxy режимы при этом остаются доступными.

## 5. Точный startup pipeline для Xray TUN

```text
Read profile
  -> Inspect/resolve Xray core
  -> Require Administrator
  -> Resolve proxy hostname to IPv4 BEFORE TUN
  -> Capture physical interface + gateway + DNS state
  -> Build runtime config (minimal TUN inbound)
  -> Pin runtime VLESS address to resolved proxy IP
  -> Validate safe config
  -> Start Xray
  -> Assign Xray to Job Object immediately
  -> Wait TUN adapter exists
  -> Wait adapter Up
  -> Wait usable IPv4 (169.254/16 allowed)
  -> Save persistent TUN backup
  -> Add proxy /32 via physical gateway
  -> Verify proxy endpoint best route = physical
  -> Add 0.0.0.0/1 via TUN
  -> Add 128.0.0.0/1 via TUN
  -> Verify public IPv4 best route = TUN
  -> Set leak-safe DNS on active physical adapter
  -> Flush DNS cache
  -> DNS probe
  -> IPv6 policy check
  -> HTTP probe UseProxy=false
  -> Set Active* fields
  -> State = Running
```

Критически важно: proxy `/32` должен появиться **до** `/1` routes.

## 6. Shutdown / rollback pipeline

При ручном Disconnect:

```text
State = Stopping
  -> remove 0.0.0.0/1
  -> remove 128.0.0.0/1
  -> restore original DNS mode/servers
  -> flush DNS cache
  -> remove proxy /32 bypass
  -> delete TUN backup if restore successful
  -> stop Xray
  -> dispose Job Object
  -> delete runtime config
  -> reset session
  -> State = Stopped
```

При failed Start: тот же rollback должен выполняться для всех уже применённых стадий.

При unexpected Xray exit: сначала немедленно вернуть Windows routes/DNS, затем переводить состояние в `Failed` и запускать существующий reconnect flow.

При `Dispose`: сначала network rollback, затем dispose core/process resources.

## 7. Изменения в `ConnectionSession.cs`

### Новые поля

Пример:

```csharp
private readonly WindowsTunNetworkService tunNetworkService = new();
private TunNetworkSession? tunNetworkSession;
private XrayTunPreparation? xrayTunPreparation;
```

### `Initialize()`

После System Proxy recovery добавить TUN recovery:

```csharp
if (tunNetworkService.HasPendingBackup)
{
    tunNetworkService.RestoreStaleSession();
}
```

Если восстановление требует admin, а приложение запущено не elevated, логировать и показать пользователю требование запустить GeniaProxy от администратора для восстановления.

### `StartAsync()`

Для `Xray + Tun` выполнить подготовку сети **до** создания runtime config.

После `coreManager.Start(...)` перенести Job Object assignment выше readiness, чтобы Xray гарантированно умер вместе с приложением даже во время стадии `Starting`.

Вместо старого `WaitForTunReadyAsync`:

```csharp
TunAdapterInfo adapter = await WaitForTunAdapterReadyAsync(...);

if (selectedCore == ProxyCoreKind.Xray)
{
    tunNetworkSession = await tunNetworkService.ApplyXrayAsync(
        preparation,
        adapter,
        token);
}
else
{
    await VerifySingBoxTunRoutingAsync(adapter, token);
}

await VerifyTunConnectivityAsync(adapter, token);
```

### `StopAsync()`

Условие раннего выхода должно учитывать pending TUN state. Нельзя делать `return`, если core уже умер, но routes/DNS ещё принадлежат GeniaProxy.

Перед `coreManager.StopAsync()`:

```csharp
await RestoreTunNetworkAsync();
```

### `CleanupFailedStartAsync()`

Первым делом TUN rollback, затем stop core.

### `HandleUnexpectedExit()`

Перед `ResetSession()` и `Failed` выполнить TUN rollback.

### `Dispose()`

Гарантированно попытаться восстановить TUN state до уничтожения сервисов.

## 8. Изменения в `XrayProfileImportService.cs`

Текущий `CreateTunInbound()` заменить на минимальный inbound для bundled 26.3.27.

Не добавлять в runtime Xray 26.3.27 следующие поля как механизм Windows orchestration:

```text
gateway
dns
autoSystemRoutingTable
autoOutboundsInterface
```

Стабильное имя интерфейса лучше сделать `geniaproxy-tun`. Оно используется readiness и diagnostic logging.

XHTTP/XMUX код не менять: текущие `stream-one` и XMUX defaults уже подтверждены рабочими параллельными тестами.

## 9. Sing-box TUN

Файл `Services/JsonProfileImportService.cs` сейчас формирует:

```json
{
  "type": "tun",
  "interface_name": "GeniaProxy",
  "address": ["172.19.0.1/30"],
  "auto_route": true,
  "strict_route": true,
  "stack": "mixed/system/gvisor"
}
```

Не переносить ручные Xray `/1` routes на sing-box без отдельного теста: sing-box уже управляет route сам. Но generic readiness должен проверять реальный adapter/best route и для sing-box.

То есть:

- Xray TUN: manual Windows orchestration GeniaProxy;
- sing-box TUN: `auto_route/strict_route`, GeniaProxy только ждёт и проверяет фактический результат.

## 10. Проверка best route

Не парсить `route print`: он локализован и `route print <IP>` не всегда показывает вычисленный best route так, как нужно приложению.

Предпочтительно использовать Windows IP Helper API (`GetBestRoute2`/связанные API). Допустимый первый вариант — `Find-NetRoute` для диагностического скрипта, но production-код лучше не зависеть от PowerShell output.

Readiness должна доказать две вещи одновременно:

```text
best route to 1.1.1.1 -> TUN interface index
best route to proxy endpoint IP -> physical interface index
```

## 11. Как применять routes

Production-вариант: Windows IP Helper API (`CreateIpForwardEntry2`, `DeleteIpForwardEntry2`, `GetBestRoute2`).

Упрощённый hotfix допустимо сделать через `route.exe`, если:

- используется только `ArgumentList`, без shell interpolation;
- проверяется exit code;
- не парсится локализованный текст;
- routes создаются только в ActiveStore/не persistent;
- rollback идемпотентен.

Маршруты:

```text
<proxy IPv4>/32 -> physical gateway, physical ifIndex
0.0.0.0/1       -> on-link, TUN ifIndex
128.0.0.0/1     -> on-link, TUN ifIndex
```

Не удалять обычный Windows `0.0.0.0/0`; две `/1` безопаснее для rollback и сохраняют исходный default route.

## 12. DNS snapshot

Важно сохранить не только `ServerAddresses`, но и источник DNS.

Если исходный DNS пришёл через DHCP, после Disconnect нужно вернуть automatic DNS (`ResetServerAddresses`), а не сделать DHCP-адрес статическим.

Для Windows можно определить static-vs-DHCP по настройкам интерфейса/registry и хранить это в snapshot.

Перед изменением DNS backup должен быть уже записан на диск.

## 13. HTTP readiness probe

`ConnectionTestService` уже правильно использует `UseProxy = false` в TUN mode. Его 1+20 запросов не нужно запускать при каждом Connect — это тяжёлый пользовательский тест.

Для startup добавить лёгкий probe, например один HTTPS GET с timeout 8-10 с через `SocketsHttpHandler { UseProxy = false }`.

Успех probe + best-route checks означает, что статус `Running` основан на реальном сетевом состоянии.

## 14. Побочный эффект Xray `run -test`

Ручной тест показал, что `xray.exe run -test` с TUN config способен создать/поднять Wintun adapter на время проверки. `ConnectionSession` сейчас вызывает `CheckConfigAsync` до настоящего старта.

Это не корневая причина, но может осложнять adapter readiness и future auto-routing.

Рекомендуемый P1 вариант: для Xray TUN проверять отдельный validation config с безопасным локальным SOCKS inbound, а настоящий TUN inbound создавать только для runtime start. Outbound/XHTTP/TLS часть всё равно будет проверена Xray, а созданный GeniaProxy TUN inbound покрывается собственными unit/integration тестами.

Минимальный вариант: оставить `-test`, но readiness после `Start` должна ждать адаптер текущего живого процесса и не считать кратковременный adapter от preflight достаточным.

## 15. Job Object

Сейчас Job Object назначается после readiness. Для TUN лучше назначать процесс сразу после `coreManager.Start()` и `TryGetRunningProcess()`.

Это уменьшает окно, в котором приложение может аварийно завершиться, а Xray останется жить с частично настроенной сетью.

## 16. Тесты, которые нужно изменить/добавить

Файл: `tests/GeniaProxy.Tests/Program.cs`.

Существующая проверка `CreateTunConfigs` сейчас ожидает `autoOutboundsInterface == "auto"`. Её нужно заменить.

Минимальные unit/regression tests:

1. Xray TUN config имеет `protocol=tun` и стабильное `name=geniaproxy-tun`.
2. Xray TUN config для bundled 26.3.27 не содержит auto Windows fields.
3. XHTTP/XMUX defaults не изменились.
4. Runtime endpoint pinning заменяет только `vnext.address`, но сохраняет SNI и XHTTP host.
5. Route plan всегда ставит proxy `/32` раньше `/1`.
6. Rollback удаляет `/1` раньше proxy bypass.
7. DNS snapshot различает DHCP/static.
8. Readiness не проходит, если process alive, но adapter отсутствует.
9. Readiness не проходит, если adapter Up, но best public route остаётся physical.
10. Unexpected exit вызывает TUN rollback.
11. Failed start вызывает TUN rollback.
12. Stop очищает TUN state даже если core уже не работает.
13. IPv6 default route блокирует leak-safe Xray TUN hotfix (до реализации IPv6).

Реальные Windows integration tests (admin/manual или отдельный Windows test harness):

- adapter появляется и Up;
- proxy endpoint best route physical;
- 1.1.1.1 best route TUN;
- Cloudflare exit IP через VPS;
- OpenDNS `myip.opendns.com` видит VPS;
- DNS после Disconnect восстановлен;
- `/1` routes после Disconnect отсутствуют;
- `taskkill /F xray.exe` приводит к автоматическому rollback;
- аварийно закрытый GeniaProxy восстанавливается при следующем запуске из backup.

## 17. Документация, которую нужно поправить

### `README.md`

Строки около 94-95 сейчас утверждают, что в TUN проверяется поднятый интерфейс. Фактически 4.2.0 проверяет только process alive после 700 мс. После fix описать реальные readiness stages.

### `CHANGELOG.md`

Строка 8 про Xray DNS interface и auto external interface не соответствует поведению bundled 26.3.27 на Windows. После fix написать, что Windows routing/DNS для Xray управляет GeniaProxy.

### `AUDIT-4.1.0.md`

Строки 13-14 утверждают полный route и auto physical interface. Добавить примечание/исправление, что это было schema-level review и оказалось неверным для bundled Xray Windows implementation.

### `RELEASE-4.2.0.txt`

Заменить утверждение о DNS интерфейса Xray на фактическую реализацию manual Windows orchestration.

### `RELEASE-CHECKLIST.md`

Добавить обязательные команды/проверки:

```powershell
Find-NetRoute -RemoteIPAddress 1.1.1.1
Find-NetRoute -RemoteIPAddress <proxy-ip>
Get-DnsClientServerAddress -AddressFamily IPv4
Get-NetRoute -AddressFamily IPv6 -DestinationPrefix "::/0"
curl.exe --noproxy "*" -4 https://www.cloudflare.com/cdn-cgi/trace
```

И проверку cleanup после normal Stop и forced core crash.

### `PRODUCT-SCOPE.md`

Файл содержит устаревшие противоречия: заголовок 4.2.0, но текст говорит «Версия 4.1», ниже утверждается отсутствие TUN. Его нужно актуализировать отдельно.

## 18. Что не нужно менять в рамках этого fix

Не трогать без причины:

- XHTTP `stream-one`;
- XMUX `maxConcurrency`, `hMaxRequestTimes`, `hMaxReusableSecs`;
- VLESS TLS/SNI/host;
- SystemProxyService;
- QR/профили/UI unrelated changes;
- sing-box `auto_route/strict_route` до отдельного sing-box TUN теста.

## 19. Рекомендуемая очередность работ

### P0 — сделать перед следующим релизом

- minimal Xray TUN inbound;
- WindowsTunNetworkService;
- proxy endpoint resolution/pinning;
- `/32` bypass + `/1` routes;
- DNS snapshot/apply/restore;
- real readiness;
- cleanup на Stop/Fail/UnexpectedExit/Dispose;
- persistent crash-recovery backup;
- conservative IPv6 leak policy;
- regression tests.

### P1 — сразу после P0

- убрать side effect TUN из Xray preflight config test;
- native IP Helper API вместо shell commands, если P0 сделан через `route.exe`/PowerShell;
- network-change handling (Ethernet/Wi-Fi switch);
- расширить endpoint extraction для других Xray outbound formats.

### P2

- полноценный IPv6 TUN;
- WFP/firewall kill-switch;
- автоматический rebind при смене default route;
- support нескольких proxy endpoint IP / multi-upstream profiles.

## 20. Definition of Done

Xray TUN можно считать исправленным, только если на чистой Windows машине выполняется весь набор:

```text
Xray process alive                         PASS
Wintun adapter exists                      PASS
Adapter Up                                 PASS
Usable IPv4 assigned                       PASS
Proxy IP best route -> physical            PASS
1.1.1.1 best route -> TUN                  PASS
System DNS -> public resolver through TUN  PASS
DNS public egress -> VPS                    PASS
HTTP without proxy -> VPS exit IP          PASS
20 parallel HTTPS -> 20/20                 PASS
IPv6 -> tunnel or fail-closed               PASS
Normal Disconnect restores Windows         PASS
Forced Xray crash restores Windows         PASS
Failed Connect restores Windows            PASS
Next app start recovers stale backup       PASS
```

Только после этих проверок UI должен показывать `Running / TUN · весь трафик`.

## 21. Финальный статус 2026-08-14

P0 закрыт реальными Windows-тестами для Xray и sing-box.

Для sing-box 1.13.18 дополнительная диагностика показала, что `auto_route` и
`strict_route` корректно направляют IPv4 через TUN, но Windows DNS Client при
`strict_route` требует DNS, назначенный самому TUN-интерфейсу. Итоговая 4.2.0:

- ждёт фактический best route через `GeniaProxy`;
- назначает `1.1.1.1/1.0.0.1` интерфейсу `GeniaProxy`;
- отключает AutomaticMetric и ставит IPv4 metric `5`;
- не меняет DNS физического NIC;
- проверяет Windows System DNS, direct UDP DNS и pinned IPv4 HTTPS;
- после Stop TUN/DNS/metric исчезают вместе с adapter;
- при crash существующий reconnect flow создаёт новую чистую TUN-сессию.

На реальном профиле Hysteria2/TLS подтверждены `gVisor` и `mixed`, оба дали
20/20 параллельных HTTPS-запросов через ожидаемый exit IP.

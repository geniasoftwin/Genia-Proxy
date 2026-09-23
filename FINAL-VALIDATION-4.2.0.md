# GeniaProxy 4.2.0 — итоговая Windows-валидация

Дата: 2026-08-14

## Подтверждённый baseline

- .NET: `net10.0-windows`.
- sing-box: `1.13.18`, revision `45ca32d`.
- Xray: `26.3.27`.
- Regression suite: 22/22.

## Xray TUN

Подтверждено реальными Windows-тестами:

- Wintun adapter создаётся и переходит в `Up`;
- proxy endpoint закрепляется за физическим маршрутом до включения полного TUN;
- IPv4 full-tunnel строится двумя `/1` routes;
- explicit route verification подтверждает `/32` proxy bypass и оба `/1` TUN routes без зависимости от нестабильного `Find-NetRoute` на APIPA/Wintun;
- leak-safe DNS применяется транзакционно и восстанавливается;
- Cloudflare видит ожидаемый VPS exit IP;
- 20 параллельных HTTPS-запросов проходят;
- обычный Stop очищает routes/DNS/adapter/process;
- forced `xray.exe` crash вызывает rollback;
- stale network backup восстанавливается асинхронно при следующем старте;
- link-local/multicast/broadcast трафик из Xray TUN блокируется, чтобы исключить APIPA/NetBIOS routing loop.
- PowerShell route/DNS diagnostics читаются как UTF-8 без mojibake.

Ограничение: Xray TUN 4.2.0 fail-closed для IPv4. При активном IPv6 default route запуск блокируется.

## sing-box TUN

Подтверждено на профиле Hysteria2/TLS:

- `auto_route=true` и `strict_route=true`;
- TUN adapter `GeniaProxy` с IPv4 `172.19.0.1`;
- GeniaProxy назначает TUN DNS `1.1.1.1` и `1.0.0.1`;
- `AutomaticMetric=Disabled`, `InterfaceMetric=5`;
- физический DNS не меняется;
- Windows System DNS работает через TUN;
- direct UDP DNS probe через TUN работает;
- pinned IPv4 HTTPS readiness работает;
- `gVisor`: exit IP подтверждён, 20/20 параллельных HTTPS PASS;
- `mixed`: exit IP подтверждён, 20/20 параллельных HTTPS PASS;
- обычный Stop удаляет процесс/TUN и возвращает best route на физический NIC;
- forced `sing-box.exe` crash определяется, после чего штатный reconnect flow успешно поднимает новую TUN-сессию.

## Privacy smoke-test

Проверка через 2ip не показала:

- HTTP proxy headers;
- открытые HTTP/web/VPN proxy ports;
- VPN fingerprint;
- WebRTC IP leak;
- Tor/hosting/suspicious-hostname признаки.

Сервис увидел публичный DNS и обнаружил факт туннеля по сетевому fingerprint. Это не считается утечкой исходного IP. Также возможна разница timezone между browser/OS и exit IP; GeniaProxy 4.2.0 системный timezone не меняет.

## Известные ограничения / следующий P1

1. Полноценный IPv6 TUN и WFP/firewall kill-switch.
2. Выбор DNS provider вместо фиксированного Cloudflare DNS.
3. Встроенный privacy-check: exit IP, DNS, IPv6, WebRTC guidance и timezone warning.
4. Дополнительная live-диагностика route/DNS при failed readiness.
5. `system` stack sing-box следует отдельно прогнать на нескольких Windows 10/11 машинах; `mixed` остаётся стандартным, `gVisor` — совместимым fallback.

Final v3 cleanup (2026-08-14)
- Xray TUN readiness uses the validated route + DNS + pinned HTTPS readiness path.
- Routine core traffic lines are hidden from the default UI journal.
- Important core errors remain visible; failed config checks dump full core output.
- The journal context menu can toggle "Подробный журнал ядра" for new messages.
- Network behavior and validated engine binaries are otherwise unchanged.

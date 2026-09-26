# Changelog

## 4.5.0 Alpha 1 Engine Refresh RC3 — Stable engine baseline

- Keeps accepted sing-box 1.14.1 unchanged.
- Restores the Windows-validated Xray 26.3.27 baseline after RC2 A/B testing showed intermittent XHTTP/XMUX TUN UDP readiness failures on Xray 26.9.8.
- Pins the exact Xray 26.3.27 windows/amd64 executable by SHA-256 `15C2D007954AC53BA69B80EC91242786B3C0B71D52649165B4CA1D5CC96EF8F1` and size `35,613,696` bytes.
- Does not change Windows TUN/DNS/routes, Control Plane, Direct Bridge, Browser Switcher, profile generation, or readiness algorithms.
- Retains the existing fail-closed block for `pinnedPeerCertSha256` while bundled Xray 26.3.27 is used.
- Xray 26.9.x remains Experimental/HOLD for Protocol Lab or a later upstream-fixed candidate.
- FileVersion: 4.5.0.8.

## 4.5.0 Alpha 1 Engine Refresh RC2 — Xray 26.9.8

- Keeps the accepted sing-box 1.14.1 RC1 engine unchanged.
- Replaces only the Xray candidate from 26.3.27 with official Xray-core 26.9.8 windows-64 for isolated Windows regression testing.
- Pins the official Xray-windows-64.zip SHA-256 before extraction and records executable provenance.
- GitHub marks v26.9.8 as a pre-release; RC2 is therefore a candidate only and is not promoted to the stable baseline until Local/TUN/XHTTP/REALITY regression passes.
- EXP1 FIX4 Control Plane, startup recovery, Direct Bridge, Browser Switcher, Windows TUN/DNS/routes orchestration and Wintun remain unchanged.
- Existing fail-closed Xray profile validation remains enabled during RC2 even though upstream security fixes are newer than the previous 26.3.27 baseline.

## 4.5.0 Alpha 1 EXP1 FIX3

## 4.5.0 Alpha 1 Engine Refresh RC1 — sing-box 1.14.1

- Branched from the frozen EXP1 FIX4 baseline.
- Updated only the sing-box candidate from 1.14.0 to official stable 1.14.1.
- Xray remains pinned to 26.3.27 for isolation of regressions.
- The builder verifies the official windows-amd64 release archive SHA-256 before extraction and verifies the extracted core reports sing-box 1.14.1.
- TUN/DNS/routes/Control Plane/Direct Bridge algorithms are unchanged.


- Added idempotent verification refresh for an already VERIFIED current session.
- Added `verificationRefreshed` structured journal event and regression coverage for refresh + stale-session rejection.
- Updated Switcher TUN WebRTC audit semantics: extension-origin local ICE is informational when TUN is VERIFIED and `raw-public=0`.
- Clear stale transient Manager transition/endpoint/current-verification metadata after Manager/Direct Bridge loss while preserving fail-closed guard behavior.
- Frozen 4.4.0 networking/TUN/DNS/core baseline remains unchanged.


## 4.5.0 Alpha 1 EXP1 FIX2

- Added session-bound verification guards: late verification results from an older session are ignored.
- Added automatic post-start TUN exit verification (`manager-tun-auto`) without changing frozen TUN/DNS/core algorithms.
- Direct Bridge verified-exit metadata now comes only from the active Control Plane session; legacy profile `LastExitIp` / `LastTestUtc` are no longer advertised as current route verification.
- Added regression test for stale-session verification rejection.

## 4.5.0 Alpha 1 EXP1 FIX1

- Fixed live JSONL journal readability on Windows by releasing the append handle after every event.
- Cleaned new ControlPlane analyzer warnings without modifying the frozen 4.4.0 networking baseline.
﻿# GeniaProxy 4.5.0 Alpha 1 EXP1 — State Machine & Session Journal

- Started the 4.5 control-plane over the frozen 4.4.0 Final Stable Direct Bridge network baseline.
- Added explicit connection lifecycle states and guarded legal transitions.
- Added per-connection session IDs; verified exit data never carries into a new session.
- Added append-only JSONL structured session journal under `data/diagnostics`.
- Existing Manager channel test now promotes the current control-plane session to `verified` only on a successful result with a concrete exit IP.
- Added read-only control-plane fields to Diagnostics; no TUN/DNS/routes/core algorithms were changed.
- Authenticated Direct Bridge and Route Coherence v2 remain deferred to later Alpha 1 experiments.

# GeniaProxy 4.4.0 Final Stable Direct Bridge

- Promoted the validated Direct Bridge EXP2/FIX3 branch to Final Stable without changing the frozen 4.3.3 network/TUN/Xray baseline.
- Switcher Direct 5.6.0 Stable uses an independent, single-in-flight Direct Bridge lane and coalesced proxy-error state handling.
- Local -> TUN -> Local and TUN -> TUN transitions are fail-closed and require fresh TUN verification after every Manager/TUN loss.
- Direct Bridge remains reachable to the extension during Hard Kill Switch bootstrap through a narrowly scoped loopback allow rule; general web traffic stays blocked.
- Normal WPF shutdown APPCRASH (`Window.VerifyNotClosing` / `Window_Closing`) is fixed by deferring final `Close()` until a later Dispatcher turn.
- Intentional policy: after GeniaProxy closes/disappears, Switcher remains fail-closed until the user manually disables proxy protection.
- Internal transport id `direct-1-exp2` is retained for protocol compatibility.

# 4.3.3 Final Stable LTS

- Promoted the validated 4.3.3 RC2 network baseline to Final Stable LTS.
- Bundled Genia Proxy Switcher 5.5.6 Stable with adaptive fail-closed TUN warm-up verification.
- Stable extension location: `browser-integration\Genia-Proxy-Switcher`; Browser Integration opens the current `browser-integration` folder without a version-specific path.
- Local→TUN→Local browser routing handoff is automatic while preserving fail-closed behavior.
- No intended changes to the validated Windows TUN/DNS/Xray orchestration.

## 4.3.3 RC2 — Final Candidate polish

- Kept the 4.3.2 network/TUN/Xray/recovery baseline unchanged.
- Kept pinned sing-box 1.14.0.
- NativeHost Bridge v2 RC5: successful GeniaProxy channel-test exit is exposed as verified egress metadata; endpoint and egress semantics are separated.
- Genia Proxy Switcher 5.5.4: read-only TUN Monitor works with browser SOCKS control OFF, preserving autonomous browser verification without controlling the VPN.
- Chrome proxy-error event bursts are coalesced for 30 seconds while the guard is already closed.
- BrowserIntegrationService analyzer warnings were removed (cached JSON options, char EndsWith, static path helpers).

## 4.3.3 RC1 — sing-box 1.14.0 + Browser Integration v2

- Pinned official stable sing-box 1.14.0 with exact SHA-256 build gate.
- Added optional read-only NativeHost Bridge v2 RC4 integration.
- Bundled Genia Proxy Switcher 5.5.3 Manager Bridge v2 package.
- Added integrated Bridge install/update/remove/status UI and safe Switcher extraction.
- Added automatic portable `managerRoot` refresh after moving the GeniaProxy folder.
- Browser Integration assets are hash-gated and do not alter TUN/DNS/Xray orchestration.
- Added Browser Extension ID regression test.

## 4.3.2 LTS Compatibility Update — REALITY Vision RAW/TCP

- Final fingerprint hardening: REALITY `fp` is preserved exactly from the share link/runtime profile and is never silently replaced with `chrome`; malformed fingerprint tokens fail closed.
- Windows device gate passed with bundled Xray 26.3.27 using explicit `fp=firefox`: Local SOCKS 20/20, TUN 20/20, expected exit IPv4, IPv6 leak PASS, DNS/routes rollback PASS.
- Added isolated import/QR support for `VLESS + REALITY + Vision + RAW/TCP` on top of the frozen 4.3.2 LTS network baseline.
- RAW/TCP share links require `security=reality`, `flow=xtls-rprx-vision`, valid REALITY SNI/fingerprint/public key/shortId, and no header obfuscation.
- URI `type=tcp` and `type=raw` are accepted; newly generated Xray JSON uses canonical `network=raw` + `rawSettings`.
- Existing XHTTP/TLS, XHTTP/REALITY/XMUX, TUN, DNS, rollback, security and bundled engines are unchanged.
- Unified GP application icon integrated for Windows and Android.

## 4.3.2 Final Stable LTS — long-term network freeze

- Promoted the fully tested LTS RC2 to Final Stable without changing network/runtime algorithms or bundled core binaries.
- Forced-crash recovery gate passed: stale TUN state is detected, constrained UAC recovery is offered, original DNS is restored, and Local/TUN reconnect works afterward.
- Manual leak gate passed for Xray REALITY TUN: expected exit IPv4, 20/20 HTTPS, IPv6 fail-closed probe and DNS rollback.
- Security review re-confirmed loopback-only local inbounds, fail-closed Xray `pinnedPeerCertSha256` rejection, validated TUN snapshot ownership and release-state hygiene.
- NETWORK BASELINE FROZEN. Future changes require a separate maintenance/security branch and a new regression cycle.

## 4.3.2 LTS RC2 — crash-recovery gate fix

- Added a constrained one-time UAC helper for validated stale TUN snapshot recovery while the main application remains in USER mode.
- Fixed USER shutdown after a forced TUN crash: pending privileged DNS recovery is preserved for the helper instead of causing a misleading shutdown error.
- New connections remain fail-closed until stale TUN DNS/routes are successfully recovered.
- XHTTP/TLS, REALITY/XMUX, TUN route/DNS implementation, engines and Wintun are otherwise unchanged from RC1.

# GeniaProxy 4.3.1 Experimental — REALITY + XMUX

- Added isolated Xray import for `VLESS + XHTTP + REALITY` without changing the existing TLS/TUN/DNS/rollback baseline.
- REALITY URI import accepts `sni/serverName`, `fp`, `pbk/publicKey/password`, `sid/shortId` and `spx/spiderX`.
- Generated client JSON targets bundled Xray 26.3.27 and uses `realitySettings.publicKey`; imported JSON accepts both `publicKey` and the newer `password` spelling and canonicalizes to `publicKey` for bundled Xray 26.3.27.
- Added fail-closed validation for required REALITY client fields and shortId format.
- Existing XHTTP XMUX defaults (`16-32`, `600-900`, `1800-3000`) are applied unchanged to REALITY profiles.
- Compact QR/share round-trip now supports both XHTTP+TLS and XHTTP+REALITY.
- Added regression tests for REALITY import, password alias compatibility, shortId validation and QR round-trip.
- Stable `VLESS + XHTTP + TLS` behavior remains unchanged.

# GeniaProxy 4.2.1 HF4 Security

- Extended the IPv6 fail-closed TUN preflight from sing-box to Xray.
- Hardened Xray TUN crash-recovery snapshot validation and PowerShell route rollback against local data tampering.
- Temporarily block Xray profiles containing `pinnedPeerCertSha256` with bundled Xray 26.3.27.
- Core update/rollback must now be performed in non-elevated USER mode.
- Forward cancellation directly to asynchronous profile endpoint DNS resolution.
- Added four security regression tests; network topology, XHTTP/XMUX, QR and bundled engines are unchanged.

# GeniaProxy 4.2.1 Final Stable

- Promoted the tested HF3 candidate to Final Stable.
- Fixed USER / ADMIN badge sizing so `ADMIN` is not clipped at Windows DPI/scaling variants.
- Network, TUN, DNS, rollback, privacy and QR code paths remain frozen from HF3.

# GeniaProxy 4.2.1

## HF3 Final UX polish

- USER -> ADMIN TUN handoff now closes the inactive USER instance immediately instead of visibly waiting on the generic graceful-shutdown path.
- Added dynamic remote server display derived from the currently selected profile; domain endpoints are resolved for UI display without hardcoded VPS addresses.
- Added current exit-IP display after a real channel/privacy test; stale results are cleared on profile/session changes.
- Added regression coverage for Hysteria2 and VLESS endpoint extraction.
- Network engines, TUN routes/DNS/rollback, XHTTP/XMUX, QR and privacy logic are unchanged from HF2.

## Privacy / UI hardening

## HF2 Final polish

- Replaced the legacy application icon with the strict blue/cyan GP brand icon for EXE, taskbar and tray.
- Added a visible USER / ADMIN privilege indicator to the main header.
- TUN started without elevation now offers an in-app UAC restart instead of a generic local-proxy error.
- The selected profile, core and TUN mode are preserved and connection continues automatically after successful elevation.
- Added a bounded single-instance handoff so the elevated process waits for the normal process to close cleanly.
- TUN/system/local start failures now use mode-specific messages.
- Secondary WinForms service dialogs inherit the executable icon.
- Network engines, TUN routes, DNS, XHTTP/XMUX and rollback logic are unchanged from HF1.

- Added fail-closed IPv6 route preflight for sing-box TUN.
- Extended channel diagnostics with TUN IPv6 leak probe and Windows DNS inventory.
- Removed GeniaProxy-specific User-Agent from the remote channel test.
- Marked TUN as the recommended full-tunnel mode.
- Reworked WPF surfaces to strict square geometry with no internal corner radii.
- Preserved the frozen 4.2.0 TUN/DNS/rollback architecture and final fixes.

# История изменений

## 4.2.0 — 2026-08-14

- Завершён Windows TUN P0 для обоих ядер. Xray использует транзакционное управление Windows routes/DNS со snapshot, rollback и crash recovery; sing-box сохраняет `auto_route/strict_route`, а GeniaProxy назначает DNS `1.1.1.1/1.0.0.1` самому TUN-интерфейсу и фиксирует его IPv4 metric = 5.
- Sing-box зафиксирован на стабильной ветке `1.13.18`; подтверждены TUN-стеки `mixed` и `gVisor`, системный DNS и 20/20 параллельных HTTPS-запросов через ожидаемый exit IP.
- Readiness TUN теперь проверяет реальную сеть, а не только живой процесс: adapter/IPv4, routes, DNS и HTTPS. Для sing-box добавлены прямой DNS-probe и pinned IPv4 HTTPS-probe; для Xray проверяются точные маршруты, которыми управляет GeniaProxy: физический endpoint `/32` и два full-tunnel `/1`.
- Исправлена гонка sing-box `auto_route`: GeniaProxy ждёт фактической смены best route перед последующими проверками. Для Xray/Wintun с APIPA устранена ложная ошибка Windows `Find-NetRoute` 1232: readiness подтверждает фактически установленные GeniaProxy routes и затем выполняет DNS/HTTPS probes.
- Исправлено зависание интерфейса при восстановлении stale TUN-сессии: startup recovery выполняется асинхронно.
- Xray TUN блокирует APIPA/NetBIOS multicast/broadcast трафик, который ранее мог образовывать routing loop.
- Аварийное завершение ядра корректно обнаруживается существующим reconnect-потоком; обычная остановка возвращает физический маршрут и исходный DNS.
- Контекстное меню журнала получило собственный WPF template без системной светлой колонки/артефактов на тёмной теме.
- Вывод дочернего Windows PowerShell нормализован в UTF-8, чтобы исключить битую кириллицу в диагностике route/DNS.
- Встроенная проверка канала выполняет один и 20 параллельных HTTPS-запросов и показывает внешний IP, HTTP-версию и задержку.
- Порт, режим подключения, выбор ядра и TUN-стек сохраняются отдельно для каждого профиля.
- Сохранены XHTTP `stream-one`, XMUX-настройки 4.1.1 и безопасная нормализация профилей.

## 4.1.1 — 2026-08-13

- восстановлено сворачивание главного WPF-окна в системный трей;
- контекстное меню журнала переведено на штатный WPF ContextMenu и больше не
  закрывается сразу после правого клика;
- исправлена ошибка сборки из-за устаревшей ссылки `operationCoordinator`;
- для XHTTP добавлены безопасные значения XMUX по умолчанию с сохранением
  явно заданных параметров профиля;

## 4.1.0 — 2026-08-12

- добавлено второе ядро Xray-core 26.3.27 с автоматическим выбором по формату
  профиля и ручной фиксацией ядра в настройках;
- добавлен импорт VLESS + XHTTP + TLS из ссылки и готовых JSON-профилей Xray;
- добавлены локальный SOCKS5, системный прокси Windows и TUN-режим;
- локальный режим сохраняет SOCKS5 `127.0.0.1:2080` для расширения Chrome,
  не затрагивая трафик остальных программ;
- TUN требует права администратора и использует Wintun;
- добавлены отдельное обслуживание, диагностика и SHA-256 для Xray/Wintun;
- добавлены проверки импорта VLESS, выбора ядра и генерации TUN-конфигураций.

## 4.0.0 — 2026-08-09

- завершён переход главного окна, импорта и настроек на компактный WPF-интерфейс;
- добавлен локальный обмен выбранным JSON-профилем через QR с предупреждением о секрете;
- QR можно скопировать как изображение или сохранить в PNG без внешних сервисов;
- закреплена QRCoder 1.8.0 (MIT), сведения добавлены в сторонние уведомления;
- функциональность после Preview 5 заморожена для финального выпуска.

## 4.0.0-preview.5 — 2026-08-09

- по правому клику журнал открывает тёмное меню «Копировать всё» и «Очистить журнал»;
- пустой журнал автоматически блокирует обе операции.

## 4.0.0-preview.4 — 2026-08-09

- стандартное контекстное меню заменено тёмной popup-панелью без белой колонки значков;
- импорт полностью перенесён в WPF, сохранено заполнение из буфера обмена;
- в окне импорта добавлена отдельная загрузка JSON-файла размером до 1 МБ;
- добавлены безопасное переименование и удаление остановленных профилей;
- добавлены реальные WPF-настройки автозапуска, автоподключения и переподключения;
- автоповтор ограничен тремя сбоями за две минуты и отменяется ручной остановкой;
- состояние журнала и его раскрытая высота сохраняются между запусками;
- добавлен регрессионный тест файловых операций профиля.
- устранены неоднозначные WPF/WinForms-ссылки новых диалогов при сборке Windows.

## 4.0.0-preview.3 — 2026-08-09

- быстрый импорт оставлен на главном экране, редкое обновление списка перенесено в «Сервис»;
- добавлено единое меню обслуживания без неработающих пунктов-заглушек;
- возвращены сведения о профиле, резервное копирование и восстановление;
- возвращены проверенное управление ядром sing-box и диагностический отчёт;
- опасные изменения профилей блокируются во время активного подключения.

## 4.0.0-preview.2 — 2026-08-09

- окно уплотнено до единой панели без бокового меню и общей полосы прокрутки;
- журнал сворачивается отдельно и по умолчанию не занимает место;
- кнопки окна возвращены штатной рамке Windows для предсказуемой работы;
- устранены предупреждения анализатора CA1001, CA1861 и CA1865.

## 4.0.0-preview.1 — 2026-08-09

- создано отдельное промежуточное WPF-окно без изменения стабильной версии 3.0;
- подключение выполняет реальную проверку профиля и SOCKS5-ответа порта;
- протокол определяется из фактических `outbounds` и `endpoints` профиля;
- добавлены реальные состояния процесса, системного прокси и времени сеанса;
- страна, ping, внешний IP, скорость и графики не показываются без источника;
- новый `ConnectionSession` отделяет жизненный цикл sing-box от WPF-интерфейса;
- импорт Hysteria2 и JSON использует прежнюю безопасную нормализацию;
- внешние пакеты и фоновые API не добавлены.
- явно подключён `System.IO` для стабильной сборки гибридного WPF/WinForms-проекта.

## 3.0.0 — 2026-08-09

- главное окно переработано в компактную компоновку плеера;
- возвращён простой выпадающий выбор профиля без перекрывающей текст строки;
- для новых установок журнал свёрнут по умолчанию;
- добавлена отдельная проверка выбранного профиля без подключения;
- отчёт проверки показывает протоколы, outbounds/endpoints и изменения безопасного режима;
- диагностику и отчёт проверки можно сохранить в UTF-8 файл;
- операции импорта, проверки, запуска и остановки проходят через единый координатор;
- перекрывающиеся операции теперь блокируются централизованно;
- добавлены регрессионные проверки координатора и анализа профилей;
- portable-скрипт очищает старые артефакты, принудительно задаёт версию и
  проверяет версию опубликованного EXE;
- устранено предупреждение анализатора CA1859 при проверке профиля;
- выровнена панель управления при DPI 125–150%: подпись порта больше не
  переносится, а кнопки подключения имеют одинаковую ширину;
- путь файловой публикации Visual Studio синхронизирован через `PublishUrl`
  и `PublishDir`, поэтому команда «Перейти» открывает фактический результат;
- сохранён минималистичный WinForms/.NET стек без новых библиотек и фоновых API.

## 2.0.3 — 2026-08-09

- обновление ядра теперь проверяет установочную копию, исключая подмену файла между проверкой и заменой;
- отменяемые проверки отката выполняются до изменения рабочего `sing-box.exe`;
- откат восстанавливает ядро, резервную копию и SHA-256 при ошибке транзакции;
- импорт сохраняет WireGuard `endpoints`, необходимые новым версиям sing-box;
- потенциально опасные endpoint-типы по-прежнему отклоняются;
- добавлены регрессионные проверки WireGuard endpoint и запрета неподдерживаемых endpoints;
- номер версии portable-архива теперь автоматически читается из проекта;
- минимальный SDK обновлён до актуального исправления .NET 10 LTS — 10.0.302.

## 2.0.2 — 2026-07-18

- журнал можно свернуть одной кнопкой, превратив главное окно в компактную панель управления;
- состояние журнала и последняя удобная высота раскрытого окна сохраняются между запусками;
- скрытый журнал продолжает собирать диагностические сообщения без лишней прокрутки интерфейса;
- после раскрытия журнал сразу показывает последние сообщения;
- кнопка обслуживания ядра переименована в «Обновить ядро»;
- после обновления показываются предыдущая и новая версии sing-box;
- сохранены все проверки целостности, резервная копия и безопасный откат ядра;
- интерфейс не дополнен тяжёлыми графиками, статистикой или сложными режимами.

## 2.0.1 — 2026-07-17

- исправлена сборка диагностики: `Assembly` указан через полное пространство имён `System.Reflection`;
- удалены BEL, ANSI OSC/CSI и другие управляющие символы из журнала;
- отключён системный balloon-tip при сворачивании, чтобы исключить скрытые звуки Windows;
- журнал обновляется пакетами с небольшой задержкой и ограничением очереди;
- уровень журнала импортируемых профилей изменён на `warn`;
- временная конфигурация создаётся под случайным именем в `data\runtime` и удаляется сразу после запуска;
- готовность порта проверяется реальным SOCKS5-приветствием;
- JSON-профили ограничены размером 1 МБ и очищаются от дополнительных слушающих сервисов;
- добавлено предупреждение при `insecure=true`;
- остановка ядра больше не теряет ссылку на процесс при ошибке завершения;
- Windows Job Object подключается без рефлексии;
- усилена проверка владельца системных настроек прокси;
- повреждённые настройки сохраняются как `settings.corrupt-*.json`;
- рабочая папка `data` и резервное ядро исключены из публикации;
- скрипт релиза создаёт чистый portable ZIP и проверяет его содержимое;
- версия приложения повышена до 2.0.1;
- SHA-256 ядра проверяется перед каждым запуском без временного кэша;
- ядро не исполняется для определения версии при отсутствующей контрольной сумме;
- автоподключение блокируется, пока целостность ядра не подтверждена;
- старые резервные копии системного прокси без метаданных владельца не перезаписывают текущие настройки Windows;
- очищаются оставшиеся после аварии runtime-файлы и атомарные временные копии с секретами;
- безопасный tag исходного mixed inbound сохраняется для совместимости правил маршрутизации;
- release-скрипт запускает встроенные регрессионные проверки до публикации.

## 2.0.0 — 2026-07-14

- добавлено безопасное локальное обновление ядра sing-box с откатом;
- отображаются версия, SHA-256, размер и состояние резервного ядра;
- контрольная сумма ядра проверяется при запуске приложения;
- процесс sing-box защищён Windows Job Object;
- добавлен импорт готовых JSON-конфигураций;
- добавлены экспорт и восстановление профилей в ZIP;
- добавлены автоподключение, запуск с Windows и ограниченный автоповтор;
- из трея можно выбрать профиль и переподключиться;
- добавлена диагностическая сводка без содержимого профилей и паролей;
- профиль публикации больше не содержит абсолютного пути компьютера.

## 1.1.1 — 2026-07-14

- устранены предупреждения встроенного анализатора .NET;
- явно зафиксировано поведение отмены при чтении вывода процесса;
- преобразования и даты не зависят от региональных настроек Windows.

## 1.1.0 — 2026-07-14

- добавлена отмена запуска при закрытии приложения;
- проверка конфигурации ограничена тайм-аутом;
- устранены гонки событий процесса при закрытии формы;
- профили и настройки записываются атомарно;
- введена единая проверка имён профилей Windows;
- журнал пакетируется и ограничен по объёму;
- восстановление прокси не затирает изменения другой программы;
- добавлены автономные проверки логики импорта.

Final v3 cleanup (2026-08-14)
- Xray TUN readiness uses the validated route + DNS + pinned HTTPS readiness path.
- Routine core traffic lines are hidden from the default UI journal.
- Important core errors remain visible; failed config checks dump full core output.
- The journal context menu can toggle "Подробный журнал ядра" for new messages.
- Network behavior and validated engine binaries are otherwise unchanged.

## 4.5.0 Alpha 1 EXP1 FIX4
- Startup Recovery Hardening after TUN crash/restart testing.
- Early startup JSONL diagnostics before MainWindow.
- ACK-based single-instance activation with safe takeover attempt and visible unresponsive-primary warning.
- Pre-UI pending TUN recovery check for administrator launches.
- Protocol Lab capability boundary added for AnyTLS, TUIC, Snell, Whitelist Mode and Xray experimental work; all remain disabled in Alpha 1.

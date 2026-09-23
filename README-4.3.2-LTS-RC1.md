# GeniaProxy 4.3.2 LTS RC1

Цель ветки — финальная долгосрочная заморозка Windows 4.3.x без новой сетевой архитектуры.

Проверяемая база:
- VLESS + XHTTP + TLS / 443 — production baseline;
- VLESS + XHTTP stream-one + XMUX + REALITY / 8443 — подтверждено direct 100/100 и native GeniaProxy 20/20;
- TUN/DNS/rollback — HF4 baseline;
- Xray 26.3.27, sing-box 1.13.18, wintun — байт-в-байт от проверенной Windows базы.

Security note:
- bundled Xray 26.3.27 попадает в upstream affected range GHSA-5wf9-h793-w73c только для сценария pinnedPeerCertSha256; GeniaProxy fail-closed запрещает такие Xray-профили до запуска ядра. Обновление Xray не включено в LTS RC1 намеренно, потому что TUN orchestration этой Windows ветки проверялся именно с 26.3.27.
- обновление core допускается только отдельным security maintenance cycle с полным TUN/rollback regression.

Release gate:
- 30/30 source tests;
- Xray REALITY/XHTTP/XMUX schema smoke;
- hashes engine/wintun;
- portable output без пользовательских профилей/state;
- ручные 5x20 TLS и REALITY, reconnect/restart, Local/System/TUN, privacy/rollback.

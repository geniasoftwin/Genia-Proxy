# Сторонние компоненты

## sing-box 1.14.0

GeniaProxy 4.4.0 использует официальный `sing-box.exe` как отдельное внешнее ядро процесса.

- проект: https://github.com/SagerNet/sing-box
- релиз: https://github.com/SagerNet/sing-box/releases/tag/v1.14.0
- лицензия: GNU GPL v3 or later
- полный текст уведомления upstream: `engine\SING-BOX-LICENSE.txt`
- GeniaProxy не заявляет аффилированность или одобрение со стороны SagerNet.

## Xray-core 26.3.27

GeniaProxy использует официальный `xray.exe` для поддержки VLESS, XHTTP и
других профилей формата Xray.

- проект: https://github.com/XTLS/Xray-core
- релиз: https://github.com/XTLS/Xray-core/releases/tag/v26.3.27
- лицензия: Mozilla Public License 2.0
- полный текст: `engine\XRAY-LICENSE.txt`

## Wintun 0.14.1

`wintun.dll` используется для TUN-режима. Для воспроизводимой сборки
`Prepare-Engines.ps1` загружает официальный архив Wintun 0.14.1 напрямую
с wintun.net и проверяет SHA-256 архива и amd64 DLL перед установкой.

- проект: https://www.wintun.net/
- версия: 0.14.1
- лицензия: WireGuard LLC General Business License
- полный текст: `engine\WINTUN-LICENSE.txt`

## QRCoder 1.8.0

GeniaProxy использует QRCoder для локального формирования QR-кодов.

- проект: https://github.com/Shane32/QRCoder
- пакет: https://www.nuget.org/packages/QRCoder/1.8.0
- лицензия: MIT

Профили не отправляются QRCoder или внешним веб-сервисам: QR формируется
внутри процесса GeniaProxy.

The MIT License (MIT)

Copyright (c) 2013-2025 Raffael Herrmann

Copyright (c) 2024-2025 Shane Krueger

Permission is hereby granted, free of charge, to any person obtaining a copy of
this software and associated documentation files (the "Software"), to deal in
the Software without restriction, including without limitation the rights to
use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of
the Software, and to permit persons to whom the Software is furnished to do so,
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

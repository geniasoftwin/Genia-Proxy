# XHTTP XMUX fix

Изменение добавляет проверенные XMUX-параметры для клиентских VLESS/XHTTP outbound:

```json
"extra": {
  "xmux": {
    "maxConcurrency": "16-32",
    "hMaxRequestTimes": "600-900",
    "hMaxReusableSecs": "1800-3000"
  }
}
```

Изменён файл:

- `Services/XrayProfileImportService.cs`

Поведение:

- применяется при импорте `vless://` с `type=xhttp` / `type=splithttp`;
- применяется при нормализации импортированного Xray JSON для VLESS + XHTTP;
- существующий `extra` сохраняется;
- существующие пользовательские XMUX-поля сохраняются;
- если уже задан `maxConnections` или `maxConcurrency`, второй конфликтующий параметр автоматически не добавляется;
- Hysteria2, другие Xray transport и другие outbound не меняются.

Тест `CreateVlessXhttpConfig` расширен проверками дефолтного XMUX и сохранения явно заданного `maxConnections`.

## Сборка на Windows

Из корня исходников:

```powershell
.\build-release.ps1
```

Скрипт сам запускает тесты перед публикацией. Для проекта требуется .NET SDK 10.0.302 или новее (это требование уже указано в исходном `build-release.ps1`).

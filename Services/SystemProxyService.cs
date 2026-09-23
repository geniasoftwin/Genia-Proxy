using Microsoft.Win32;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace GeniaProxy.Services
{
    public enum ProxyRestoreOutcome
    {
        NothingToRestore,
        Restored,
        CurrentSettingsPreserved
    }

    public sealed class SystemProxyService
    {
        private const string InternetSettingsPath =
            @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";

        private const int InternetOptionRefresh = 37;
        private const int InternetOptionSettingsChanged = 39;

        private readonly JsonSerializerOptions jsonOptions =
            new()
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true
            };

        public string BackupPath { get; }

        public bool IsEnabledByApplication { get; private set; }

        public bool HasPendingBackup =>
            File.Exists(BackupPath);

        public SystemProxyService()
        {
            BackupPath = Path.Combine(
                AppContext.BaseDirectory,
                "data",
                "system-proxy-backup.json"
            );
        }

        public void Enable(int port)
        {
            if (port is < 1 or > 65535)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(port),
                    "Номер порта должен быть от 1 до 65535."
                );
            }

            if (File.Exists(BackupPath))
            {
                throw new InvalidOperationException(
                    "Обнаружена незавершённая резервная копия " +
                    "системного прокси. Сначала восстановите " +
                    "прежние настройки."
                );
            }

            string applicationProxyServer =
                $"127.0.0.1:{port}";

            ProxySnapshot snapshot =
                CaptureCurrentSettings();

            snapshot.FormatVersion = 1;
            snapshot.SessionId = Guid.NewGuid().ToString("N");
            snapshot.CreatedUtc = DateTime.UtcNow;
            snapshot.AppliedProxyServer =
                applicationProxyServer;
            snapshot.AppliedProxyOverride = "<local>";
            snapshot.AppliedAutoDetect = 0;

            SaveSnapshot(snapshot);

            try
            {
                using RegistryKey key =
                    OpenInternetSettings(writable: true);

                key.SetValue(
                    "ProxyEnable",
                    1,
                    RegistryValueKind.DWord
                );

                key.SetValue(
                    "ProxyServer",
                    applicationProxyServer,
                    RegistryValueKind.String
                );

                key.SetValue(
                    "ProxyOverride",
                    "<local>",
                    RegistryValueKind.String
                );

                // На время работы ручного прокси
                // отключаем PAC и автоопределение.
                key.DeleteValue(
                    "AutoConfigURL",
                    throwOnMissingValue: false
                );

                key.SetValue(
                    "AutoDetect",
                    0,
                    RegistryValueKind.DWord
                );

                NotifyWindows();

                IsEnabledByApplication = true;
            }
            catch
            {
                try
                {
                    RestoreCore(force: true);
                }
                catch
                {
                    // Сохраняем исходное исключение.
                }

                throw;
            }
        }

        public ProxyRestoreOutcome Restore()
        {
            return RestoreCore(force: false);
        }

        private ProxyRestoreOutcome RestoreCore(
            bool force)
        {
            if (!File.Exists(BackupPath))
            {
                IsEnabledByApplication = false;
                return ProxyRestoreOutcome.NothingToRestore;
            }

            ProxySnapshot snapshot =
                LoadSnapshot();

            if (!force &&
                string.IsNullOrWhiteSpace(
                    snapshot.AppliedProxyServer))
            {
                // Старые резервные копии не содержат сведений,
                // позволяющих доказать, что текущие параметры всё ещё
                // установлены GeniaProxy. Безопаснее сохранить текущее
                // состояние Windows, чем безусловно перезаписать его.
                File.Delete(BackupPath);
                IsEnabledByApplication = false;

                return ProxyRestoreOutcome
                    .CurrentSettingsPreserved;
            }

            if (!force)
            {
                ProxySnapshot currentSettings =
                    CaptureCurrentSettings();

                bool applicationSettingsStillActive =
                    currentSettings.ProxyEnable != 0 &&
                    string.Equals(
                        currentSettings.ProxyServer,
                        snapshot.AppliedProxyServer,
                        StringComparison.OrdinalIgnoreCase
                    ) &&
                    currentSettings.ProxyOverrideExists &&
                    string.Equals(
                        currentSettings.ProxyOverride,
                        snapshot.AppliedProxyOverride ?? "<local>",
                        StringComparison.OrdinalIgnoreCase
                    ) &&
                    !currentSettings.AutoConfigUrlExists &&
                    currentSettings.AutoDetectExists &&
                    currentSettings.AutoDetect ==
                        snapshot.AppliedAutoDetect;

                if (!applicationSettingsStillActive)
                {
                    File.Delete(BackupPath);
                    IsEnabledByApplication = false;

                    return ProxyRestoreOutcome
                        .CurrentSettingsPreserved;
                }
            }

            using RegistryKey key =
                OpenInternetSettings(writable: true);

            RestoreDword(
                key,
                "ProxyEnable",
                snapshot.ProxyEnableExists,
                snapshot.ProxyEnable
            );

            RestoreString(
                key,
                "ProxyServer",
                snapshot.ProxyServerExists,
                snapshot.ProxyServer
            );

            RestoreString(
                key,
                "ProxyOverride",
                snapshot.ProxyOverrideExists,
                snapshot.ProxyOverride
            );

            RestoreString(
                key,
                "AutoConfigURL",
                snapshot.AutoConfigUrlExists,
                snapshot.AutoConfigUrl
            );

            RestoreDword(
                key,
                "AutoDetect",
                snapshot.AutoDetectExists,
                snapshot.AutoDetect
            );

            NotifyWindows();

            File.Delete(BackupPath);

            IsEnabledByApplication = false;

            return ProxyRestoreOutcome.Restored;
        }

        private static ProxySnapshot CaptureCurrentSettings()
        {
            using RegistryKey key =
                OpenInternetSettings(writable: false);

            object? proxyEnable =
                ReadRegistryValue(key, "ProxyEnable");

            object? proxyServer =
                ReadRegistryValue(key, "ProxyServer");

            object? proxyOverride =
                ReadRegistryValue(key, "ProxyOverride");

            object? autoConfigUrl =
                ReadRegistryValue(key, "AutoConfigURL");

            object? autoDetect =
                ReadRegistryValue(key, "AutoDetect");

            return new ProxySnapshot
            {
                ProxyEnableExists =
                    proxyEnable is not null,

                ProxyEnable =
                    proxyEnable is null
                        ? 0
                        : Convert.ToInt32(
                            proxyEnable,
                            CultureInfo.InvariantCulture
                        ),

                ProxyServerExists =
                    proxyServer is not null,

                ProxyServer =
                    proxyServer?.ToString(),

                ProxyOverrideExists =
                    proxyOverride is not null,

                ProxyOverride =
                    proxyOverride?.ToString(),

                AutoConfigUrlExists =
                    autoConfigUrl is not null,

                AutoConfigUrl =
                    autoConfigUrl?.ToString(),

                AutoDetectExists =
                    autoDetect is not null,

                AutoDetect =
                    autoDetect is null
                        ? 0
                        : Convert.ToInt32(
                            autoDetect,
                            CultureInfo.InvariantCulture
                        )
            };
        }

        private static object? ReadRegistryValue(
            RegistryKey key,
            string name)
        {
            return key.GetValue(
                name,
                null,
                RegistryValueOptions
                    .DoNotExpandEnvironmentNames
            );
        }

        private void SaveSnapshot(
            ProxySnapshot snapshot)
        {
            string json =
                JsonSerializer.Serialize(
                    snapshot,
                    jsonOptions
                );

            AtomicFileWriter.WriteAllText(
                BackupPath,
                json,
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false
                )
            );
        }

        private ProxySnapshot LoadSnapshot()
        {
            var backupFile = new FileInfo(BackupPath);

            if (backupFile.Length > 256 * 1024)
            {
                throw new InvalidDataException(
                    "Резервная копия настроек прокси " +
                    "имеет недопустимый размер."
                );
            }

            string json =
                File.ReadAllText(
                    BackupPath,
                    Encoding.UTF8
                );

            using JsonDocument document =
                JsonDocument.Parse(
                    json,
                    new JsonDocumentOptions
                    {
                        MaxDepth = 16,
                        AllowTrailingCommas = false,
                        CommentHandling =
                            JsonCommentHandling.Disallow
                    }
                );

            JsonElement root = document.RootElement;

            bool hasRequiredShape =
                root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty(
                    "proxyEnableExists",
                    out _
                ) &&
                root.TryGetProperty(
                    "proxyServerExists",
                    out _
                ) &&
                root.TryGetProperty(
                    "proxyOverrideExists",
                    out _
                ) &&
                root.TryGetProperty(
                    "autoConfigUrlExists",
                    out _
                ) &&
                root.TryGetProperty(
                    "autoDetectExists",
                    out _
                );

            if (!hasRequiredShape)
            {
                throw new InvalidDataException(
                    "Резервная копия настроек прокси " +
                    "имеет неправильный формат."
                );
            }

            return JsonSerializer
                .Deserialize<ProxySnapshot>(
                    json,
                    jsonOptions
                )
                ?? throw new InvalidDataException(
                    "Резервная копия настроек прокси повреждена."
                );
        }

        private static RegistryKey OpenInternetSettings(
            bool writable)
        {
            return Registry.CurrentUser.OpenSubKey(
                InternetSettingsPath,
                writable
            )
            ?? throw new InvalidOperationException(
                "Не удалось открыть системные настройки прокси Windows."
            );
        }

        private static void RestoreDword(
            RegistryKey key,
            string name,
            bool existed,
            int value)
        {
            if (existed)
            {
                key.SetValue(
                    name,
                    value,
                    RegistryValueKind.DWord
                );
            }
            else
            {
                key.DeleteValue(
                    name,
                    throwOnMissingValue: false
                );
            }
        }

        private static void RestoreString(
            RegistryKey key,
            string name,
            bool existed,
            string? value)
        {
            if (existed)
            {
                key.SetValue(
                    name,
                    value ?? string.Empty,
                    RegistryValueKind.String
                );
            }
            else
            {
                key.DeleteValue(
                    name,
                    throwOnMissingValue: false
                );
            }
        }

        private static void NotifyWindows()
        {
            bool settingsChanged =
                InternetSetOption(
                    IntPtr.Zero,
                    InternetOptionSettingsChanged,
                    IntPtr.Zero,
                    0
                );

            bool refreshed =
                InternetSetOption(
                    IntPtr.Zero,
                    InternetOptionRefresh,
                    IntPtr.Zero,
                    0
                );

            if (!settingsChanged || !refreshed)
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "Windows не удалось уведомить " +
                    "об изменении настроек прокси."
                );
            }
        }

        [DllImport(
            "wininet.dll",
            SetLastError = true)]
        private static extern bool InternetSetOption(
            IntPtr hInternet,
            int option,
            IntPtr buffer,
            int bufferLength
        );

        private sealed class ProxySnapshot
        {
            public int FormatVersion { get; set; }

            public string? SessionId { get; set; }

            public DateTime CreatedUtc { get; set; }

            public bool ProxyEnableExists { get; set; }

            public int ProxyEnable { get; set; }

            public bool ProxyServerExists { get; set; }

            public string? ProxyServer { get; set; }

            public bool ProxyOverrideExists { get; set; }

            public string? ProxyOverride { get; set; }

            public bool AutoConfigUrlExists { get; set; }

            public string? AutoConfigUrl { get; set; }

            public bool AutoDetectExists { get; set; }

            public int AutoDetect { get; set; }

            public string? AppliedProxyServer { get; set; }

            public string? AppliedProxyOverride { get; set; }

            public int AppliedAutoDetect { get; set; }
        }
    }
}

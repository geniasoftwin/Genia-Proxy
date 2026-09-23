using GeniaProxy.Models;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace GeniaProxy.Services
{
    public sealed class SettingsService
    {
        private readonly JsonSerializerOptions jsonOptions =
            new()
            {
                WriteIndented = true,
                PropertyNamingPolicy =
                    JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true
            };

        public string SettingsPath { get; }

        public SettingsService()
        {
            SettingsPath = Path.Combine(
                AppContext.BaseDirectory,
                "data",
                "settings.json"
            );
        }

        public AppSettings Load()
        {
            if (!File.Exists(SettingsPath))
            {
                return new AppSettings();
            }

            try
            {
                string json = File.ReadAllText(
                    SettingsPath,
                    Encoding.UTF8
                );

                AppSettings? settings =
                    JsonSerializer.Deserialize<AppSettings>(
                        json,
                        jsonOptions
                    );

                return Normalize(
                    settings ?? new AppSettings()
                );
            }
            catch
            {
                PreserveCorruptedSettings();
                return new AppSettings();
            }
        }

        public void Save(AppSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);

            AppSettings normalized = Normalize(settings);

            string json = JsonSerializer.Serialize(
                normalized,
                jsonOptions
            );

            AtomicFileWriter.WriteAllText(
                SettingsPath,
                json,
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false
                )
            );
        }

        private static AppSettings Normalize(
            AppSettings settings)
        {
            string lastProfile =
                settings.LastProfile?.Trim()
                ?? string.Empty;

            if (lastProfile.Length > 100 ||
                ProfileNameValidator
                    .GetValidationError(lastProfile) is not null)
            {
                lastProfile = string.Empty;
            }

            var normalizedProfiles =
                new Dictionary<string, ProfileRuntimeSettings>(
                    StringComparer.OrdinalIgnoreCase
                );

            if (settings.ProfileSettings is not null)
            {
                foreach ((string rawName, ProfileRuntimeSettings value)
                    in settings.ProfileSettings.Take(256))
                {
                    string name = rawName?.Trim() ?? string.Empty;

                    if (value is null ||
                        ProfileNameValidator.GetValidationError(name)
                            is not null)
                    {
                        continue;
                    }

                    normalizedProfiles[name] = new ProfileRuntimeSettings
                    {
                        LocalPort = value.LocalPort is >= 1024 and <= 65535
                            ? value.LocalPort
                            : 2080,
                        ConnectionMode = Enum.IsDefined(value.ConnectionMode)
                            ? value.ConnectionMode
                            : ConnectionMode.LocalProxy,
                        CorePreference = Enum.IsDefined(value.CorePreference)
                            ? value.CorePreference
                            : CorePreference.Automatic,
                        TunStackPreference =
                            Enum.IsDefined(value.TunStackPreference)
                                ? value.TunStackPreference
                                : TunStackPreference.Mixed,
                        LastTestUtc = value.LastTestUtc,
                        LastTestSucceeded = value.LastTestSucceeded,
                        LastTestAverageMilliseconds =
                            value.LastTestAverageMilliseconds is >= 0 and <= 60000
                                ? value.LastTestAverageMilliseconds
                                : null,
                        LastExitIp = NormalizeExitIp(value.LastExitIp)
                    };
                }
            }

            return new AppSettings
            {
                LocalPort = settings.LocalPort is >= 1 and <= 65535
                    ? settings.LocalPort
                    : 2080,
                LastProfile = lastProfile,
                UseSystemProxy = settings.UseSystemProxy,
                ConnectionMode = Enum.IsDefined(settings.ConnectionMode)
                    ? settings.ConnectionMode
                    : ConnectionMode.LocalProxy,
                CorePreference = Enum.IsDefined(settings.CorePreference)
                    ? settings.CorePreference
                    : CorePreference.Automatic,
                TunStackPreference =
                    Enum.IsDefined(settings.TunStackPreference)
                        ? settings.TunStackPreference
                        : TunStackPreference.Mixed,
                ProfileSettings = normalizedProfiles,
                AutoConnect = settings.AutoConnect,
                StartWithWindows = settings.StartWithWindows,
                ReconnectOnFailure =
                    settings.ReconnectOnFailure,
                ReconnectDelaySeconds = Math.Clamp(
                    settings.ReconnectDelaySeconds,
                    1,
                    60
                ),
                LogCollapsed = settings.LogCollapsed,
                ExpandedWindowHeight = Math.Clamp(
                    settings.ExpandedWindowHeight,
                    380,
                    1600
                )
            };
        }

        private static string NormalizeExitIp(string? value)
        {
            string result = value?.Trim() ?? string.Empty;
            return result.Length <= 64 && !result.Any(char.IsControl)
                ? result
                : string.Empty;
        }

        private void PreserveCorruptedSettings()
        {
            try
            {
                if (!File.Exists(SettingsPath))
                {
                    return;
                }

                string directory =
                    Path.GetDirectoryName(SettingsPath)
                    ?? AppContext.BaseDirectory;

                string backupPath = Path.Combine(
                    directory,
                    "settings.corrupt-" +
                    DateTime.UtcNow.ToString(
                        "yyyyMMdd-HHmmss-fff",
                        CultureInfo.InvariantCulture
                    ) +
                    ".json"
                );

                File.Move(
                    SettingsPath,
                    backupPath,
                    overwrite: false
                );
            }
            catch
            {
                // Повреждённый файл не должен мешать запуску.
            }
        }
    }
}

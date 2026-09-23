using Microsoft.Win32;

namespace GeniaProxy.Services
{
    public static class StartupService
    {
        private const string RunKeyPath =
            @"Software\Microsoft\Windows\CurrentVersion\Run";

        private const string ValueName = "GeniaProxy";

        public static bool IsEnabled
        {
            get
            {
                using RegistryKey? key =
                    Registry.CurrentUser.OpenSubKey(
                        RunKeyPath,
                        writable: false
                    );

                return key?.GetValue(ValueName)
                    is string value &&
                    !string.IsNullOrWhiteSpace(value);
            }
        }

        public static void SetEnabled(bool enabled)
        {
            using RegistryKey key =
                Registry.CurrentUser.CreateSubKey(
                    RunKeyPath,
                    writable: true
                );

            if (enabled)
            {
                key.SetValue(
                    ValueName,
                    $"\"{Application.ExecutablePath}\"",
                    RegistryValueKind.String
                );
            }
            else
            {
                key.DeleteValue(
                    ValueName,
                    throwOnMissingValue: false
                );
            }
        }
    }
}

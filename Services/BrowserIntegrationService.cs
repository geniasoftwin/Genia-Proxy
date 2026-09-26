using System.Text.Json;

namespace GeniaProxy.Services
{
    /// <summary>
    /// Browser Integration metadata for the Direct Bridge branch.
    /// The browser bridge itself is hosted inside GeniaProxy.exe by
    /// BrowserDirectBridgeService; no Native Messaging helper is installed.
    /// </summary>
    public sealed class BrowserIntegrationService
    {
        public const string SwitcherVersion = "5.6.0";
        public const string SwitcherManifestVersion = "5.6.0.7";
        public const string BridgeVersion = "direct-1-exp2";

        public static string BundledRoot => Path.Combine(
            AppContext.BaseDirectory,
            "browser-integration"
        );

        public static string BundledSwitcherDirectory => Path.Combine(
            BundledRoot,
            "Genia-Proxy-Switcher"
        );

        public static string BundledSwitcherManifestPath => Path.Combine(
            BundledSwitcherDirectory,
            "manifest.json"
        );

        public static string GetSwitcherFolder()
        {
            Directory.CreateDirectory(BundledRoot);
            return BundledRoot;
        }

        public BrowserIntegrationStatus GetStatus()
        {
            bool switcherReady = IsSwitcherFolderReady();
            string summary = switcherReady
                ? "Direct Bridge встроен в GeniaProxy.exe; NativeHost не используется."
                : "Direct Bridge встроен, но папка Switcher отсутствует или имеет неподдерживаемую версию.";

            return new BrowserIntegrationStatus(
                SwitcherReady: switcherReady,
                DirectBridgePort: BrowserDirectBridgeService.Port,
                DirectBridgeEndpoint:
                    $"http://127.0.0.1:{BrowserDirectBridgeService.Port}" +
                    BrowserDirectBridgeService.StatusPath,
                ExtensionDirectory: BundledSwitcherDirectory,
                Summary: summary
            );
        }

        private static bool IsSwitcherFolderReady()
        {
            string[] requiredFiles =
            [
                "manifest.json",
                "background.js",
                "popup.html",
                "popup.js",
                "off.png",
                "on.png"
            ];

            if (!requiredFiles.All(file =>
                File.Exists(Path.Combine(BundledSwitcherDirectory, file))))
            {
                return false;
            }

            try
            {
                using FileStream stream = File.OpenRead(
                    BundledSwitcherManifestPath
                );
                using JsonDocument document = JsonDocument.Parse(stream);
                JsonElement root = document.RootElement;

                string? name = root.TryGetProperty(
                    "name",
                    out JsonElement nameElement)
                    ? nameElement.GetString()
                    : null;

                string? version = root.TryGetProperty(
                    "version",
                    out JsonElement versionElement)
                    ? versionElement.GetString()
                    : null;

                return string.Equals(
                        name,
                        "Genia Proxy Switcher Direct",
                        StringComparison.Ordinal
                    ) &&
                    string.Equals(
                        version,
                        SwitcherManifestVersion,
                        StringComparison.Ordinal
                    );
            }
            catch
            {
                return false;
            }
        }
    }

    public sealed record BrowserIntegrationStatus(
        bool SwitcherReady,
        int DirectBridgePort,
        string DirectBridgeEndpoint,
        string ExtensionDirectory,
        string Summary
    );
}

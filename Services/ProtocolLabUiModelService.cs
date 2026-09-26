namespace GeniaProxy.Services
{
    public sealed record ProtocolLabUiCapability(
        string Id,
        string DisplayName,
        string EngineLabel,
        string SupportLabel,
        string SupportColor,
        bool Selectable,
        bool EnabledByDefault,
        string Detail);

    public static class ProtocolLabUiModelService
    {
        public static IReadOnlyList<ProtocolLabUiCapability>
            GetCapabilities()
        {
            return ProtocolLabFeatureCatalog.All
                .Where(capability =>
                    capability.Lane == FeatureLane.ProtocolLab)
                .Select(Create)
                .ToArray();
        }

        private static ProtocolLabUiCapability Create(
            FeatureCapability capability)
        {
            return new ProtocolLabUiCapability(
                capability.Id,
                DisplayName(capability.Id),
                EngineLabel(capability.EngineFamily),
                SupportLabel(capability.SupportState),
                SupportColor(capability.SupportState),
                capability.SelectableInProtocolLab,
                capability.EnabledByDefault,
                Detail(capability)
            );
        }

        private static string DisplayName(string id) =>
            id.ToLowerInvariant() switch
            {
                "anytls" => "AnyTLS",
                "tuic" => "TUIC",
                "snell" => "Snell v6",
                "whitelist-mode" => "Whitelist-oriented mode",
                "xray-experimental" => "Xray experimental",
                _ => id
            };

        private static string EngineLabel(
            ProtocolLabEngineFamily engine) =>
            engine switch
            {
                ProtocolLabEngineFamily.SingBox => "sing-box",
                ProtocolLabEngineFamily.Xray => "Xray",
                ProtocolLabEngineFamily.Host => "Host",
                _ => engine.ToString()
            };

        private static string SupportLabel(
            ProtocolLabSupportState state) =>
            state switch
            {
                ProtocolLabSupportState.RuntimeVerified =>
                    "RUNTIME VERIFIED",
                ProtocolLabSupportState.EngineAvailable =>
                    "ENGINE AVAILABLE",
                ProtocolLabSupportState.DesignOnly =>
                    "DESIGN ONLY",
                _ => state.ToString().ToUpperInvariant()
            };

        private static string SupportColor(
            ProtocolLabSupportState state) =>
            state switch
            {
                ProtocolLabSupportState.RuntimeVerified =>
                    "#28D9C5",
                ProtocolLabSupportState.EngineAvailable =>
                    "#E5C07B",
                _ => "#7F96A3"
            };

        private static string Detail(
            FeatureCapability capability)
        {
            return capability.SupportState switch
            {
                ProtocolLabSupportState.RuntimeVerified =>
                    "Изолированный runtime smoke пройден. " +
                    "Доступно только внутри Protocol Lab.",
                ProtocolLabSupportState.DesignOnly
                    when capability.Id.Equals(
                        "whitelist-mode",
                        StringComparison.OrdinalIgnoreCase) =>
                    "Runtime не реализован. Маршруты/DNS и " +
                    "stable-путь не изменяются; выбор заблокирован.",
                ProtocolLabSupportState.DesignOnly
                    when capability.Id.Equals(
                        "xray-experimental",
                        StringComparison.OrdinalIgnoreCase) =>
                    "Экспериментальная Xray-ветка остаётся " +
                    "design-only; выбор заблокирован.",
                _ =>
                    "Capability пока не прошла полный runtime gate."
            };
        }
    }
}

namespace GeniaProxy.Services
{
    public enum FeatureLane
    {
        Stable,
        ProtocolLab
    }

    public sealed record FeatureCapability(
        string Id,
        FeatureLane Lane,
        bool EnabledByDefault,
        string PlannedMilestone);

    public static class ProtocolLabFeatureCatalog
    {
        private static readonly FeatureCapability[] Capabilities =
        [
            new("anytls", FeatureLane.ProtocolLab, false, "Alpha 2 / Protocol Lab"),
            new("tuic", FeatureLane.ProtocolLab, false, "Alpha 2 / Protocol Lab"),
            new("snell", FeatureLane.ProtocolLab, false, "Alpha 2 / Protocol Lab"),
            new("whitelist-mode", FeatureLane.ProtocolLab, false, "Alpha 2 / Protocol Lab"),
            new("xray-experimental", FeatureLane.ProtocolLab, false, "Alpha 2 / Protocol Lab")
        ];

        public static IReadOnlyList<FeatureCapability> All => Capabilities;

        public static bool Alpha1BoundaryIsSafe =>
            Capabilities
                .Where(capability => capability.Lane == FeatureLane.ProtocolLab)
                .All(capability => !capability.EnabledByDefault);

        public static void ThrowIfAlpha1BoundaryViolated()
        {
            FeatureCapability? enabledExperimental = Capabilities
                .FirstOrDefault(capability =>
                    capability.Lane == FeatureLane.ProtocolLab &&
                    capability.EnabledByDefault);

            if (enabledExperimental is not null)
            {
                throw new InvalidOperationException(
                    "Alpha 1 Protocol Lab boundary violated by capability: " +
                    enabledExperimental.Id
                );
            }
        }
    }
}

namespace GeniaProxy.Services
{
    public enum FeatureLane
    {
        Stable,
        ProtocolLab
    }

    public enum ProtocolLabEngineFamily
    {
        Host,
        SingBox,
        Xray
    }

    public enum ProtocolLabSupportState
    {
        EngineAvailable,
        RuntimeVerified,
        DesignOnly
    }

    public sealed record FeatureCapability(
        string Id,
        FeatureLane Lane,
        bool EnabledByDefault,
        string PlannedMilestone,
        ProtocolLabEngineFamily EngineFamily,
        ProtocolLabSupportState SupportState,
        bool SelectableInProtocolLab);

    public static class ProtocolLabFeatureCatalog
    {
        private static readonly FeatureCapability[] Capabilities =
        [
            new(
                "anytls",
                FeatureLane.ProtocolLab,
                false,
                "Alpha 2 / Protocol Lab",
                ProtocolLabEngineFamily.SingBox,
                ProtocolLabSupportState.RuntimeVerified,
                true
            ),
            new(
                "tuic",
                FeatureLane.ProtocolLab,
                false,
                "Alpha 2 / Protocol Lab",
                ProtocolLabEngineFamily.SingBox,
                ProtocolLabSupportState.RuntimeVerified,
                true
            ),
            new(
                "snell",
                FeatureLane.ProtocolLab,
                false,
                "Alpha 2 / Protocol Lab",
                ProtocolLabEngineFamily.SingBox,
                ProtocolLabSupportState.EngineAvailable,
                false
            ),
            new(
                "whitelist-mode",
                FeatureLane.ProtocolLab,
                false,
                "Alpha 2 / Protocol Lab",
                ProtocolLabEngineFamily.Host,
                ProtocolLabSupportState.DesignOnly,
                false
            ),
            new(
                "xray-experimental",
                FeatureLane.ProtocolLab,
                false,
                "Alpha 2 / Protocol Lab",
                ProtocolLabEngineFamily.Xray,
                ProtocolLabSupportState.DesignOnly,
                false
            )
        ];

        public static IReadOnlyList<FeatureCapability> All => Capabilities;

        public static bool DefaultBoundaryIsSafe =>
            Capabilities
                .Where(capability =>
                    capability.Lane == FeatureLane.ProtocolLab)
                .All(capability => !capability.EnabledByDefault);

        // Kept as a compatibility alias for the completed Alpha 1
        // regression/build gates.
        public static bool Alpha1BoundaryIsSafe =>
            DefaultBoundaryIsSafe;

        public static FeatureCapability? Find(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            return Capabilities.FirstOrDefault(capability =>
                capability.Id.Equals(
                    id,
                    StringComparison.OrdinalIgnoreCase
                )
            );
        }

        public static FeatureCapability RequireSelectable(string id)
        {
            FeatureCapability capability =
                Find(id)
                ?? throw new NotSupportedException(
                    "Unknown Protocol Lab capability: " + id
                );

            if (capability.Lane != FeatureLane.ProtocolLab ||
                !capability.SelectableInProtocolLab)
            {
                throw new NotSupportedException(
                    "Protocol Lab capability is not selectable: " +
                    capability.Id
                );
            }

            return capability;
        }

        public static void ThrowIfDefaultBoundaryViolated()
        {
            FeatureCapability? enabledExperimental = Capabilities
                .FirstOrDefault(capability =>
                    capability.Lane == FeatureLane.ProtocolLab &&
                    capability.EnabledByDefault);

            if (enabledExperimental is not null)
            {
                throw new InvalidOperationException(
                    "Protocol Lab default boundary violated by capability: " +
                    enabledExperimental.Id
                );
            }
        }

        public static void ThrowIfAlpha1BoundaryViolated()
        {
            ThrowIfDefaultBoundaryViolated();
        }
    }
}

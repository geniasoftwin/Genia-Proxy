namespace GeniaProxy.Services
{
    public enum XrayExperimentalImplementationState
    {
        DesignOnly,
        IsolatedEngineValidated,
        RuntimeVerified
    }

    public sealed record XrayExperimentalBoundary(
        XrayExperimentalImplementationState State,
        bool Selectable,
        bool MayReplaceStablePinnedEngine,
        bool MayReuseStableProfilePathWithoutValidation,
        bool MayRelaxStableProfileHardening,
        bool RequiresSeparateEnginePin,
        bool RequiresEngineHashVerification,
        bool RequiresSeparateRuntimeEvidence,
        bool RequiresExplicitExperimentalOptIn);

    public static class ProtocolLabXrayExperimentalBoundary
    {
        public static XrayExperimentalBoundary Current { get; } =
            new(
                XrayExperimentalImplementationState.DesignOnly,
                Selectable: false,
                MayReplaceStablePinnedEngine: false,
                MayReuseStableProfilePathWithoutValidation: false,
                MayRelaxStableProfileHardening: false,
                RequiresSeparateEnginePin: true,
                RequiresEngineHashVerification: true,
                RequiresSeparateRuntimeEvidence: true,
                RequiresExplicitExperimentalOptIn: true
            );

        public static void ThrowIfRuntimeActivationRequested()
        {
            throw new NotSupportedException(
                "Xray experimental capability is design-only. " +
                "Runtime activation requires a separately pinned and " +
                "hash-verified Xray build plus isolated config/runtime " +
                "evidence before the capability can become selectable."
            );
        }

        public static void ValidateBoundary()
        {
            XrayExperimentalBoundary boundary = Current;

            if (boundary.State !=
                    XrayExperimentalImplementationState.DesignOnly ||
                boundary.Selectable ||
                boundary.MayReplaceStablePinnedEngine ||
                boundary.MayReuseStableProfilePathWithoutValidation ||
                boundary.MayRelaxStableProfileHardening ||
                !boundary.RequiresSeparateEnginePin ||
                !boundary.RequiresEngineHashVerification ||
                !boundary.RequiresSeparateRuntimeEvidence ||
                !boundary.RequiresExplicitExperimentalOptIn)
            {
                throw new InvalidOperationException(
                    "Xray experimental Protocol Lab boundary is unsafe."
                );
            }

            FeatureCapability capability =
                ProtocolLabFeatureCatalog.Find("xray-experimental")
                ?? throw new InvalidOperationException(
                    "Xray experimental capability is missing from " +
                    "Protocol Lab."
                );

            if (capability.EnabledByDefault ||
                capability.SelectableInProtocolLab ||
                capability.SupportState !=
                    ProtocolLabSupportState.DesignOnly ||
                capability.EngineFamily !=
                    ProtocolLabEngineFamily.Xray)
            {
                throw new InvalidOperationException(
                    "Xray experimental capability catalog state violates " +
                    "the design-only boundary."
                );
            }
        }
    }
}

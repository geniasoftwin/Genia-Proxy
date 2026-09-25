namespace GeniaProxy.Services
{
    public enum WhitelistModeImplementationState
    {
        DesignOnly,
        RuntimeVerified
    }

    public sealed record WhitelistModeBoundary(
        WhitelistModeImplementationState State,
        bool Selectable,
        bool MayModifyStableConnectionPath,
        bool MayModifySystemRoutes,
        bool MayModifySystemDns,
        bool MayUseThirdPartyServiceImpersonation,
        bool RequiresExplicitExperimentalOptIn);

    public static class ProtocolLabWhitelistModeBoundary
    {
        public static WhitelistModeBoundary Current { get; } =
            new(
                WhitelistModeImplementationState.DesignOnly,
                Selectable: false,
                MayModifyStableConnectionPath: false,
                MayModifySystemRoutes: false,
                MayModifySystemDns: false,
                MayUseThirdPartyServiceImpersonation: false,
                RequiresExplicitExperimentalOptIn: true
            );

        public static void ThrowIfRuntimeActivationRequested()
        {
            throw new NotSupportedException(
                "Whitelist-oriented Protocol Lab mode is design-only. " +
                "Runtime activation is blocked until an isolated transport, " +
                "config-validation path and rollback model are implemented " +
                "and verified separately."
            );
        }

        public static void ValidateBoundary()
        {
            WhitelistModeBoundary boundary = Current;

            if (boundary.State !=
                    WhitelistModeImplementationState.DesignOnly ||
                boundary.Selectable ||
                boundary.MayModifyStableConnectionPath ||
                boundary.MayModifySystemRoutes ||
                boundary.MayModifySystemDns ||
                boundary.MayUseThirdPartyServiceImpersonation ||
                !boundary.RequiresExplicitExperimentalOptIn)
            {
                throw new InvalidOperationException(
                    "Whitelist-oriented Protocol Lab boundary is unsafe."
                );
            }

            FeatureCapability capability =
                ProtocolLabFeatureCatalog.Find("whitelist-mode")
                ?? throw new InvalidOperationException(
                    "Whitelist capability is missing from Protocol Lab."
                );

            if (capability.EnabledByDefault ||
                capability.SelectableInProtocolLab ||
                capability.SupportState !=
                    ProtocolLabSupportState.DesignOnly)
            {
                throw new InvalidOperationException(
                    "Whitelist capability catalog state violates " +
                    "the design-only boundary."
                );
            }
        }
    }
}

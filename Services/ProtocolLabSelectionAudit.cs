namespace GeniaProxy.Services
{
    public static class ProtocolLabSelectionAudit
    {
        public static event Action<string>? SelectionLogged;

        public static FeatureCapability RequireSelectableAndLog(
            string capabilityId)
        {
            FeatureCapability capability =
                ProtocolLabFeatureCatalog.RequireSelectable(capabilityId);

            SelectionLogged?.Invoke(CreateLogLine(capability));

            return capability;
        }

        public static string CreateLogLine(string capabilityId)
        {
            FeatureCapability capability =
                ProtocolLabFeatureCatalog.Find(capabilityId)
                ?? throw new NotSupportedException(
                    "Unknown Protocol Lab capability: " + capabilityId
                );

            return CreateLogLine(capability);
        }

        private static string CreateLogLine(
            FeatureCapability capability)
        {
            return string.Join(
                "; ",
                "Protocol Lab selection",
                "id=" + capability.Id,
                "engine=" + capability.EngineFamily,
                "support=" + capability.SupportState,
                "selectable=" + capability.SelectableInProtocolLab,
                "enabledByDefault=" + capability.EnabledByDefault
            );
        }
    }
}

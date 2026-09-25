namespace GeniaProxy.Services
{
    public static class ProtocolLabSelectionAudit
    {
        public static string CreateLogLine(string capabilityId)
        {
            FeatureCapability capability =
                ProtocolLabFeatureCatalog.Find(capabilityId)
                ?? throw new NotSupportedException(
                    "Unknown Protocol Lab capability: " + capabilityId
                );

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

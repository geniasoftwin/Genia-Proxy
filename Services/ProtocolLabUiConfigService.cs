namespace GeniaProxy.Services
{
    public sealed record ProtocolLabUiConnectionInput(
        string CapabilityId,
        string Server,
        int ServerPort,
        int LocalPort,
        string Secret,
        string? TuicUuid = null,
        string? TlsServerName = null);

    public static class ProtocolLabUiConfigService
    {
        public static string CreateConfig(
            ProtocolLabUiConnectionInput input)
        {
            ArgumentNullException.ThrowIfNull(input);

            FeatureCapability capability =
                ProtocolLabFeatureCatalog.RequireSelectable(
                    input.CapabilityId
                );

            if (capability.EngineFamily !=
                    ProtocolLabEngineFamily.SingBox ||
                capability.SupportState !=
                    ProtocolLabSupportState.RuntimeVerified)
            {
                throw new NotSupportedException(
                    "Protocol Lab UI supports only runtime-verified " +
                    "sing-box capabilities."
                );
            }

            return capability.Id.ToLowerInvariant() switch
            {
                "anytls" =>
                    ProtocolLabAnyTlsConfigService
                        .CreateLocalProxyConfig(
                            input.Server,
                            input.ServerPort,
                            input.Secret,
                            input.TlsServerName,
                            input.LocalPort,
                            allowInsecureTls: false
                        ),

                "tuic" =>
                    ProtocolLabTuicConfigService
                        .CreateLocalProxyConfig(
                            input.Server,
                            input.ServerPort,
                            input.TuicUuid
                                ?? throw new FormatException(
                                    "Не указан TUIC UUID."
                                ),
                            input.Secret,
                            input.TlsServerName,
                            input.LocalPort,
                            allowInsecureTls: false
                        ),

                "snell" =>
                    ProtocolLabSnellConfigService
                        .CreateLocalProxyConfig(
                            input.Server,
                            input.ServerPort,
                            input.Secret,
                            input.LocalPort
                        ),

                _ => throw new NotSupportedException(
                    "Unsupported Protocol Lab UI capability: " +
                    capability.Id
                )
            };
        }
    }
}

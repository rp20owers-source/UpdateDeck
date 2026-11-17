namespace UpdateDeck.Core;

public static class ScanEvidence
{
    public static bool CompletedCatalogCheck(DiscoveryResult result,DateTimeOffset started,DateTimeOffset completed)=>
        result.Providers.Any(provider=>provider.Available&&provider.RemoteCheckCompleted&&provider.Capabilities.HasFlag(ProviderCapability.Check)&&
            provider.CheckedAt>=started-TimeSpan.FromSeconds(2)&&provider.CheckedAt<=completed+TimeSpan.FromMinutes(2));
}

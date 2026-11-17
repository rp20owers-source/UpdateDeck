namespace UpdateDeck.Core;

public static class ManagerOwnership
{
    private static bool Established(string provider)=>provider is "WinGet" or "Store" or "MSIX" or "Scoop" or "Chocolatey" or "Steam" or "Epic";
    public static Software PreserveWhenUnverified(Software current,Software? previous)
    {
        if(previous==null||previous.Id!=current.Id||!Established(previous.Provider)||previous.Provider==current.Provider)return current;
        bool sourceLost=current.Provider=="Registry"||(current.Provider=="WinGet"&&previous.Provider is ("Store" or "MSIX" or "Scoop" or "Chocolatey" or "Steam" or "Epic"));
        if(!sourceLost)return current;
        return current with
        {
            Provider=previous.Provider,PackageId=previous.PackageId,AvailableVersion=previous.AvailableVersion,CheckedAt=previous.CheckedAt,CatalogUpdatedAt=previous.CatalogUpdatedAt,
            State=UpdateState.Attention,
            Explanation=$"Previously observed {previous.Provider} ownership could not be confirmed in this scan. The existing owner is preserved; automatic provider changes and direct-installer fallback are disabled. Inspect the source or recheck.",
            Evidence=current.Evidence.Append($"Previous {previous.Provider} ownership retained while fresh source evidence is unavailable").ToArray()
        };
    }
}

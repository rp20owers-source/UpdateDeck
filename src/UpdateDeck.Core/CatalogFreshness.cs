namespace UpdateDeck.Core;

public static class CatalogFreshness
{
    public static bool IsFresh(DateTimeOffset? updated,DateTimeOffset now)=>updated.HasValue&&now-updated.Value is var age&&age>=TimeSpan.FromMinutes(-2)&&age<=TimeSpan.FromMinutes(30);
}

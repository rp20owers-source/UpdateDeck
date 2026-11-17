using UpdateDeck.Core;
using Xunit;
namespace UpdateDeck.Tests;

public class SchedulingTests
{
    private static readonly DateTimeOffset Noon=new(DateTime.Today.AddHours(12));
    private static Preferences Due()=>new(){Schedule="Daily",QuietStart=0,QuietEnd=0,LastCheck=Noon.AddDays(-2)};
    [Theory][InlineData(1,15)][InlineData(2,30)][InlineData(3,60)][InlineData(6,480)]
    public void UnavailableScheduledSourcesUseBoundedBackoffInsteadOfRescanningEveryMinute(int failures,int minutes)
    {
        var preferences=Due();preferences.ScheduledFailureCount=failures;preferences.LastScheduledAttempt=Noon;
        Assert.False(UpdatePolicy.ScheduleDue(preferences,Noon.AddMinutes(1)));Assert.False(UpdatePolicy.ScheduleDue(preferences,Noon.AddMinutes(minutes-1)));Assert.True(UpdatePolicy.ScheduleDue(preferences,Noon.AddMinutes(minutes)));Assert.Equal(TimeSpan.FromMinutes(minutes),UpdatePolicy.ScheduledRetryDelay(preferences));
    }
    [Fact]public void AClockRollbackCannotSuppressChecksIndefinitely()
    {var preferences=Due();preferences.LastCheck=Noon.AddDays(1);preferences.LastScheduledAttempt=Noon.AddDays(1);preferences.ScheduledFailureCount=6;Assert.True(UpdatePolicy.ScheduleDue(preferences,Noon));}
    [Fact]public void AnUnsupportedScheduleNeverBecomesAnImplicitDailySchedule(){var preferences=Due();preferences.Schedule="broken";Assert.False(UpdatePolicy.ScheduleDue(preferences,Noon));}
    [Fact]public void SavingUserPreferencesPreservesLiveAttemptAndFailureMetadata()
    {var preferences=Due();preferences.LastScheduledAttempt=Noon;preferences.ScheduledFailureCount=4;preferences.ApplyUserSettings(new(){Theme="Light",Schedule="Daily"});Assert.Equal(Noon,preferences.LastScheduledAttempt);Assert.Equal(4,preferences.ScheduledFailureCount);}
    [Theory][InlineData("cached")][InlineData("future")][InlineData("unavailable")][InlineData("old-report")]
    public void CachedOrFutureApplicationTimestampsCannotImpersonateACompletedCatalogCheck(string reason)
    {
        var started=DateTimeOffset.UtcNow;var app=Fixture.App() with{CheckedAt=started.AddDays(1)};
        var provider=new ProviderReport("WinGet",reason!="unavailable",ProviderCapability.Check,"fixture",reason=="old-report"?started.AddHours(-1):reason=="future"?started.AddHours(1):started){RemoteCheckCompleted=reason!="cached"};
        Assert.False(ScanEvidence.CompletedCatalogCheck(new([app],[provider]),started,started.AddSeconds(2)));
    }
    [Fact]public void OneCompletedIndependentCatalogCanAdvanceTheSuccessfulCheckTime()
    {
        var now=DateTimeOffset.UtcNow;var unavailable=new ProviderReport("Unavailable source",false,ProviderCapability.None,"fixture",now);
        var completed=new ProviderReport("WinGet",true,ProviderCapability.Check,"fixture",now){RemoteCheckCompleted=true};
        Assert.True(ScanEvidence.CompletedCatalogCheck(new([],[unavailable,completed]),now,now.AddSeconds(1)));
    }
}

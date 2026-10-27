using Ageledger;
using Ageledger.Testing;
using static Ageledger.Tests.Helpers;

namespace Ageledger.Tests;

public class AndroidMappingTests
{
    [Fact]
    public async Task Shared_then_check_returns_the_bucket()
    {
        var (p, fake) = Android();
        fake.SetNextAccessResult(AndroidAccessStatus.Shared);
        fake.SetNextAgeSignalsResult(13, 15, AndroidSignificantChange.Approved);
        var r = await p.RequestAgeSignalsAsync(On());
        Assert.Equal(AgeSignalsState.Shared, r.State);
        Assert.Equal(AgeSignalsPlatform.Android, r.Platform);
        Assert.Equal(new AgeRangeBucket(13, 15), r.Bucket);
        Assert.Equal(SignificantChange.Approved, r.SignificantChange);
        Assert.Equal(1, fake.CheckCalls);
    }

    [Fact]
    public async Task Top_band_has_no_upper_bound()
    {
        var (p, fake) = Android();
        fake.SetNextAgeSignalsResult(18, null);
        var r = await p.RequestAgeSignalsAsync(On());
        Assert.Equal("18+", r.Bucket.Label);
    }

    [Theory]
    [InlineData(AndroidAccessStatus.NotShared, AgeSignalsState.NotShared)]
    [InlineData(AndroidAccessStatus.VerificationRequired, AgeSignalsState.VerificationRequired)]
    public async Task Only_shared_leads_to_a_check(AndroidAccessStatus status, AgeSignalsState expected)
    {
        var (p, fake) = Android();
        fake.SetNextAccessResult(status);
        var r = await p.RequestAgeSignalsAsync(On());
        Assert.Equal(expected, r.State);
        Assert.Equal(0, fake.CheckCalls);
        Assert.False(r.Bucket.IsKnown);
    }

    [Fact]
    public async Task Unspecified_access_status_is_an_error_without_a_check()
    {
        var (p, fake) = Android();
        fake.SetNextAccessResult(AndroidAccessStatus.Unspecified);
        var r = await p.RequestAgeSignalsAsync(On());
        Assert.Equal(AgeSignalsState.Error, r.State);
        Assert.Equal(0, fake.CheckCalls);
    }

    [Fact]
    public async Task Shared_but_no_bounds_means_nothing_was_shared()
    {
        var (p, fake) = Android();
        fake.SetNextAgeSignalsResult(null, null);
        Assert.Equal(AgeSignalsState.NotShared, (await p.RequestAgeSignalsAsync(On())).State);
    }

    [Theory]
    [InlineData(AndroidErrorKind.ApiNotAvailable, AgeSignalsState.Unavailable, AgeSignalsError.NotAvailable)]
    [InlineData(AndroidErrorKind.AppNotOwned, AgeSignalsState.Unavailable, AgeSignalsError.AppNotOwned)]
    [InlineData(AndroidErrorKind.PlayStoreNotFound, AgeSignalsState.Unavailable, AgeSignalsError.PlatformComponentMissing)]
    [InlineData(AndroidErrorKind.PlayServicesVersionOutdated, AgeSignalsState.Unavailable, AgeSignalsError.PlatformComponentMissing)]
    [InlineData(AndroidErrorKind.SdkVersionOutdated, AgeSignalsState.Unavailable, AgeSignalsError.PlatformComponentMissing)]
    [InlineData(AndroidErrorKind.CannotBindToService, AgeSignalsState.Unavailable, AgeSignalsError.PlatformComponentMissing)]
    [InlineData(AndroidErrorKind.NetworkError, AgeSignalsState.Error, AgeSignalsError.Network)]
    [InlineData(AndroidErrorKind.ClientTransientError, AgeSignalsState.Error, AgeSignalsError.Transient)]
    [InlineData(AndroidErrorKind.InternalError, AgeSignalsState.Error, AgeSignalsError.Internal)]
    [InlineData(AndroidErrorKind.Other, AgeSignalsState.Error, AgeSignalsError.Other)]
    public async Task Access_errors_map(AndroidErrorKind kind, AgeSignalsState state, AgeSignalsError error)
    {
        var (p, fake) = Android();
        fake.SetNextAccessError(kind);
        var r = await p.RequestAgeSignalsAsync(On());
        Assert.Equal(state, r.State);
        Assert.Equal(error, r.Error);
        Assert.Equal(0, fake.CheckCalls);
    }

    [Fact]
    public async Task Check_errors_map_too()
    {
        var (p, fake) = Android();
        fake.SetNextAgeSignalsError(AndroidErrorKind.NetworkError);
        var r = await p.RequestAgeSignalsAsync(On());
        Assert.Equal((AgeSignalsState.Error, AgeSignalsError.Network), (r.State, r.Error));
    }

    [Fact]
    public async Task Apple_only_calls_are_unsupported_on_android()
    {
        var (p, _) = Android();
        Assert.Null(await p.IsEligibleForAgeFeaturesAsync());
        Assert.Null(await p.GetRequiredRegulatoryFeaturesAsync());
        Assert.Equal(SignificantUpdateOutcome.Unsupported, await p.ShowSignificantUpdateAcknowledgmentAsync("x"));
    }
}

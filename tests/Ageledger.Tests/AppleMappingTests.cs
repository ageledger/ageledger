using Ageledger;
using static Ageledger.Tests.Helpers;

namespace Ageledger.Tests;

public class AppleMappingTests
{
    [Fact]
    public async Task Sharing_returns_bounds_and_parental_controls()
    {
        var (p, fake) = Apple();
        fake.NextResponse = new(AppleResponseKind.Sharing, 13, 15, ParentalControls.CommunicationLimits);
        var r = await p.RequestAgeSignalsAsync(On());
        Assert.Equal(AgeSignalsState.Shared, r.State);
        Assert.Equal(AgeSignalsPlatform.iOS, r.Platform);
        Assert.Equal("13-15", r.Bucket.Label);
        Assert.Equal(ParentalControls.CommunicationLimits, r.ParentalControls);
    }

    [Fact]
    public async Task Gates_come_from_the_options()
    {
        var (p, fake) = Apple();
        await p.RequestAgeSignalsAsync(On(o => o.AgeGates = [14, 18]));
        Assert.Equal([14, 18], fake.LastGates);
    }

    [Fact]
    public async Task Lower_bound_missing_reads_as_zero_and_upper_missing_as_plus()
    {
        Assert.Equal("0-12", new AgeRangeBucket(null, 12).Label);
        Assert.Equal("18+", new AgeRangeBucket(18, null).Label);
        Assert.Equal("unknown", AgeRangeBucket.Unknown.Label);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Declined_sharing()
    {
        var (p, fake) = Apple();
        fake.NextResponse = new(AppleResponseKind.DeclinedSharing);
        var r = await p.RequestAgeSignalsAsync(On());
        Assert.Equal(AgeSignalsState.Declined, r.State);
        Assert.False(r.Bucket.IsKnown);
    }

    [Theory]
    [InlineData(AppleErrorKind.NotAvailable, AgeSignalsState.Unavailable, AgeSignalsError.NotAvailable)]
    [InlineData(AppleErrorKind.InvalidAccount, AgeSignalsState.Unavailable, AgeSignalsError.InvalidAccount)]
    [InlineData(AppleErrorKind.DeclinedOnboarding, AgeSignalsState.Declined, AgeSignalsError.None)]
    [InlineData(AppleErrorKind.InvalidRequest, AgeSignalsState.Error, AgeSignalsError.InvalidRequest)]
    [InlineData(AppleErrorKind.Network, AgeSignalsState.Error, AgeSignalsError.Network)]
    [InlineData(AppleErrorKind.NoPresenter, AgeSignalsState.Error, AgeSignalsError.NoPresenter)]
    [InlineData(AppleErrorKind.Other, AgeSignalsState.Error, AgeSignalsError.Other)]
    public async Task Errors_map(AppleErrorKind kind, AgeSignalsState state, AgeSignalsError error)
    {
        var (p, fake) = Apple();
        fake.NextResponse = new(AppleResponseKind.Error, Error: kind);
        var r = await p.RequestAgeSignalsAsync(On());
        Assert.Equal((state, error), (r.State, r.Error));
    }

    [Theory]
    [InlineData(new int[0])]
    [InlineData(new[] { 18, 13 })]
    [InlineData(new[] { 13, 13 })]
    [InlineData(new[] { 0, 13 })]
    [InlineData(new[] { 1, 2, 3, 4 })]
    public async Task Bad_gates_are_refused_before_calling_apple(int[] gates)
    {
        var (p, fake) = Apple();
        var r = await p.RequestAgeSignalsAsync(On(o => o.AgeGates = gates));
        Assert.Equal((AgeSignalsState.Error, AgeSignalsError.InvalidRequest), (r.State, r.Error));
        Assert.Equal(0, fake.RequestCalls);
    }

    [Fact]
    public async Task Regulatory_surface_passes_through()
    {
        var (p, fake) = Apple();
        fake.Eligible = true;
        fake.Features = RegulatoryFeatures.DeclaredAgeRangeRequired | RegulatoryFeatures.SignificantAppChangeRequiresParentalConsent;
        Assert.True(await p.IsEligibleForAgeFeaturesAsync());
        Assert.Equal(fake.Features, await p.GetRequiredRegulatoryFeaturesAsync());
        Assert.Equal(SignificantUpdateOutcome.Acknowledged, await p.ShowSignificantUpdateAcknowledgmentAsync("New purchases"));
        Assert.Equal("New purchases", fake.LastUpdateDescription);
    }
}

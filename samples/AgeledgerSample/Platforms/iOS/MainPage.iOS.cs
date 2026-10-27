using Ageledger;

namespace AgeledgerSample;

public sealed partial class MainPage
{
    private partial IAgeSignalsProvider CreateProvider() => new AppleAgeSignalsProvider(new AppleAgeRangeBackend());

    private partial void AddPlatformControls()
    {
        AddButton("IsEligibleForAgeFeaturesAsync", async () =>
        {
            if (service is null) return;
            try { status.Text = "IsEligibleForAgeFeatures: " + ((await service.IsEligibleForAgeFeaturesAsync()) is { } v ? v.ToString() : "n/a"); }
            catch (Exception ex) { status.Text = ex.GetType().Name; }
        });
        AddButton("GetRequiredRegulatoryFeaturesAsync", async () =>
        {
            if (service is null) return;
            try { status.Text = "RegulatoryFeatures: " + ((await service.GetRequiredRegulatoryFeaturesAsync()) is { } v ? v.ToString() : "n/a"); }
            catch (Exception ex) { status.Text = ex.GetType().Name; }
        });
        AddButton("ShowSignificantUpdateAcknowledgmentAsync", async () =>
        {
            if (service is null) return;
            try { status.Text = "Acknowledgment: " + await service.ShowSignificantUpdateAcknowledgmentAsync("Sample update"); }
            catch (Exception ex) { status.Text = ex.GetType().Name; }
        });
    }
}

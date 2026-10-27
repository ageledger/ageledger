#if DEBUG
using Ageledger;
using Ageledger.Testing;

namespace AgeledgerSample;

internal static partial class SelfTestPlatform
{
    public static partial ISelfTestPlatform Create() => new IosSelfTestPlatform();
}

/// <summary>
/// iOS self-test. Every range comes from the Ageledger.Testing package's <see cref="FakeAppleAgeRangeBackend"/>; the real Apple backend is never called, so no row can
/// show Apple's sheet. The rows that need the real sheet are the iPhone rows of the device test plan.
/// </summary>
internal sealed class IosSelfTestPlatform : ISelfTestPlatform
{
    public string Name => "ios";
    public string RangeSource => "fake backend";

    public IAgeSignalsProvider RangeProvider(int? lower, int? upper) =>
        new AppleAgeSignalsProvider(new FakeAppleAgeRangeBackend { NextResponse = new AppleAgeRangeResponse(AppleResponseKind.Sharing, lower, upper) });

    public Task WaitUntilReadyAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task RunPlatformRowsAsync(SelfTestRun run) => Task.CompletedTask;

    public Task RunRealPlayRowsAsync(SelfTestRun run) => Task.CompletedTask;
}
#endif

using Microsoft.Extensions.DependencyInjection;

namespace AgeledgerSample;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder().UseMauiApp<App>();
        SampleServices.Register(builder.Services, SampleServices.ConfigureFromSavedSettings);
        builder.Services.AddAgeledgerFileLog(SampleServices.LogFile);
        return builder.Build();
    }
}

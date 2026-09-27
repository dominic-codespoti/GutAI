using System.Net;
using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GutAI.Api.Tests;

[Collection("WebApi")]
public sealed class AppInsightsStartupSmokeTests(GutAiWebFactory factory)
{
    [Fact]
    public async Task AppInsightsAndOpenTelemetryStartTogether_AndRealStoreRequestSucceeds()
    {
        var (client, _, services, _) = await factory.CreateAuthenticatedRealStoreClientAsync(
            "app-insights",
            builder => builder.UseSetting(
                "APPLICATIONINSIGHTS_CONNECTION_STRING",
                "InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=https://centralus-0.in.applicationinsights.azure.com/;LiveEndpoint=https://centralus.livediagnostics.monitor.azure.com/"));

        Assert.NotNull(services.GetService<TelemetryClient>());

        var response = await client.GetAsync("/api/user/profile");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Data.Tables;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using GutAI.Application.Common.Interfaces;
using GutAI.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GutAI.Api.Tests;

public class GutAiWebFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private IContainer _azurite = default!;
    private string _connectionString = default!;

    public async Task InitializeAsync()
    {
        Environment.SetEnvironmentVariable("TESTCONTAINERS_RYUK_DISABLED", "true");

        _azurite = new ContainerBuilder("mcr.microsoft.com/azure-storage/azurite")
            .WithCommand("azurite-table", "--tableHost", "0.0.0.0", "--tablePort", "10002")
            .WithPortBinding(10002, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Azurite Table service successfully started"))
            .Build();

        await _azurite.StartAsync();
        var port = _azurite.GetMappedPublicPort(10002);
        _connectionString = $"DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;TableEndpoint=http://localhost:{port}/devstoreaccount1;";
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(TableServiceClient));
            if (descriptor != null) services.Remove(descriptor);
            var storeDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ITableStore));
            if (storeDescriptor != null) services.Remove(storeDescriptor);

            var client = new TableServiceClient(_connectionString);
            services.AddSingleton(client);
            services.AddSingleton<ITableStore>(new TableStorageStore(client));
        });
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _azurite.DisposeAsync();
    }

    public async Task<(HttpClient Client, string Token)> CreateAuthenticatedClientAsync()
    {
        var client = CreateClient();
        var email = $"test-{Guid.NewGuid():N}@test.com";
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            password = "TestPass123",
            displayName = "Test User"
        });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var token = json.GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, token);
    }
}

[CollectionDefinition("WebApi")]
public class WebApiCollection : ICollectionFixture<GutAiWebFactory>;

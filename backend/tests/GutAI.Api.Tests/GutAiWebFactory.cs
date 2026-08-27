using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Azure.Data.Tables;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;
using GutAI.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace GutAI.Api.Tests;

public class GutAiWebFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string TestAdminKey = "test-admin-key-for-integration-tests";
    private IContainer _azurite = default!;

    static GutAiWebFactory()
    {
        Environment.SetEnvironmentVariable("TESTCONTAINERS_RYUK_DISABLED", "true");
    }

    /// <summary>Used by tests that need to override IContentUnderstandingService. Set before calling CreateClientWithStubAi.</summary>
    internal static CustomFoodDto? StubDescribeResult { get; set; }

    public async Task InitializeAsync()
    {
        _azurite = new ContainerBuilder("mcr.microsoft.com/azure-storage/azurite")
            .WithCommand("azurite-table", "--tableHost", "0.0.0.0", "--tablePort", "10002")
            .WithPortBinding(10002, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Azurite Table service successfully started"))
            .Build();

        await _azurite.StartAsync();
        var port = _azurite.GetMappedPublicPort(10002);
        var connStr = $"DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;TableEndpoint=http://localhost:{port}/devstoreaccount1;";

        // Store connection string for ConfigureWebHost and as a convenience
        Environment.SetEnvironmentVariable("GUTAI_TEST_AZURITE_CONNECTION", connStr);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var connStr = Environment.GetEnvironmentVariable("GUTAI_TEST_AZURITE_CONNECTION")
            ?? throw new InvalidOperationException("GUTAI_TEST_AZURITE_CONNECTION not set. Ensure InitializeAsync ran.");

        builder.UseEnvironment("Development");
        builder.UseSetting("AdminKey", TestAdminKey);
        builder.UseSetting("APPLICATIONINSIGHTS_CONNECTION_STRING", "InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=https://centralus-0.in.applicationinsights.azure.com/;LiveEndpoint=https://centralus.livediagnostics.monitor.azure.com/");
        builder.ConfigureServices(services =>
        {
            Storage.Replace(services, connStr);
            AiStub.Register(services);
            ChatStub.Register(services);
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

    public async Task<(HttpClient Client, string Token)> CreateAdminClientAsync()
    {
        var (client, token) = await CreateAuthenticatedClientAsync();
        client.DefaultRequestHeaders.Add("X-Admin-Key", TestAdminKey);
        return (client, token);
    }
}

/// <summary>Azurite storage helpers for use in ConfigureWebHost and test lambdas.</summary>
file static class Storage
{
    public static void Replace(IServiceCollection services, string connectionString)
    {
        var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(TableServiceClient));
        if (descriptor != null) services.Remove(descriptor);
        var storeDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ITableStore));
        if (storeDescriptor != null) services.Remove(storeDescriptor);

        var client = new TableServiceClient(connectionString);
        services.AddSingleton(client);
        services.AddSingleton<ITableStore>(sp => new FoodSearchFaultToleranceTests.FaultInjectionTableStore(new TableStorageStore(client)));
    }
}

/// <summary>AI service stub that returns GutAiWebFactory.StubDescribeResult.</summary>
file static class AiStub
{
    public static void Register(IServiceCollection services)
    {
        services.RemoveAll(typeof(IContentUnderstandingService));
        services.AddSingleton<IContentUnderstandingService>(_ => new Stub());
    }

    private sealed class Stub : IContentUnderstandingService
    {
        public Task<CustomFoodDto?> DescribeFoodFromTextAsync(string description, CancellationToken ct = default)
            => Task.FromResult(GutAiWebFactory.StubDescribeResult);

        public Task<CustomFoodDto?> ParseNutritionLabelAsync(Stream imageStream, string contentType, CancellationToken ct = default)
            => Task.FromResult<CustomFoodDto?>(null);
    }
}

/// <summary>Chat service stub for contract tests. Returns empty history and no-ops for clear/stream.</summary>
file static class ChatStub
{
    public static void Register(IServiceCollection services)
    {
        services.RemoveAll(typeof(IChatService));
        services.AddSingleton<IChatService>(_ => new Stub());
    }

    private sealed class Stub : IChatService
    {
        public async IAsyncEnumerable<ChatStreamEvent> StreamResponseAsync(
            Guid userId,
            string message,
            [EnumeratorCancellation] CancellationToken ct = default,
            string? timezoneId = null)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<List<ChatHistoryMessage>> GetHistoryAsync(Guid userId, int limit = 50, CancellationToken ct = default)
            => Task.FromResult(new List<ChatHistoryMessage>());

        public Task ClearHistoryAsync(Guid userId, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}

[CollectionDefinition("WebApi")]
public class WebApiCollection : ICollectionFixture<GutAiWebFactory>;

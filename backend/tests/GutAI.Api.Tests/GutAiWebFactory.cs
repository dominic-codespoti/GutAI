using System.Collections.Concurrent;

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
    private readonly ConcurrentBag<AsyncServiceScope> _realStoreScopes = new();
    private readonly object _derivedHostLock = new();
    private readonly Dictionary<string, WebApplicationFactory<Program>> _derivedHosts = new(StringComparer.Ordinal);

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
        builder.UseSetting("APPLICATIONINSIGHTS_CONNECTION_STRING", "");
        builder.ConfigureServices(services =>
        {
            Storage.Replace(services, connStr);
            AiStub.Register(services);
            ChatStub.Register(services);
        });
    }

    public new async Task DisposeAsync()
    {
        while (_realStoreScopes.TryTake(out var scope))
            await scope.DisposeAsync();
        WebApplicationFactory<Program>[] derivedHosts;
        lock (_derivedHostLock)
            derivedHosts = _derivedHosts.Values.ToArray();
        foreach (var host in derivedHosts)
            await host.DisposeAsync();
        await base.DisposeAsync();
        await _azurite.DisposeAsync();
    }

    /// <summary>
    /// Returns the fixture-cached host for a unique configuration key. A key must uniquely
    /// identify the host configuration; subsequent calls with that key reuse the first host.
    /// </summary>
    public WebApplicationFactory<Program> DerivedHost(string key, Action<IWebHostBuilder> configure)
    {
        lock (_derivedHostLock)
        {
            if (!_derivedHosts.TryGetValue(key, out var host))
            {
                host = WithWebHostBuilder(configure);
                _derivedHosts.Add(key, host);
            }
            return host;
        }
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
    public async Task<(HttpClient Client, Guid UserId, IServiceProvider Services, string Token)> CreateAuthenticatedRealStoreClientAsync(
        string hostKey = "real-store",
        Action<IWebHostBuilder>? configure = null)
    {
        var factory = DerivedHost(hostKey, builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ITableStore>();
                services.AddSingleton<ITableStore>(sp => new TableStorageStore(sp.GetRequiredService<TableServiceClient>()));
            });
            configure?.Invoke(builder);
        });

        var client = factory.CreateClient();
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
        var payload = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        var claims = JsonDocument.Parse(Convert.FromBase64String(payload));
        var userId = Guid.Parse(claims.RootElement.GetProperty("sub").GetString()!);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var scope = factory.Services.CreateAsyncScope();
        _realStoreScopes.Add(scope);
        return (client, userId, scope.ServiceProvider, token);
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

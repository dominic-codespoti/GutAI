using Azure.Data.Tables;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using GutAI.Application.Common.Interfaces;
using GutAI.Infrastructure.Data;
using Xunit;

namespace GutAI.IntegrationTests;

public class AzuriteFixture : IAsyncLifetime
{
    private readonly IContainer _container;

    public ITableStore Store { get; private set; } = default!;
    public TableServiceClient ServiceClient { get; private set; } = default!;

    public AzuriteFixture()
    {
        Environment.SetEnvironmentVariable("TESTCONTAINERS_RYUK_DISABLED", "true");

        _container = new ContainerBuilder("mcr.microsoft.com/azure-storage/azurite")
            .WithCommand("azurite-table", "--tableHost", "0.0.0.0", "--tablePort", "10002")
            .WithPortBinding(10002, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Azurite Table service successfully started"))
            .Build();
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        var port = _container.GetMappedPublicPort(10002);
        var cs = $"DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;TableEndpoint=http://localhost:{port}/devstoreaccount1;";
        ServiceClient = new TableServiceClient(cs);
        Store = new TableStorageStore(ServiceClient);
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();
}

[CollectionDefinition("Azurite")]
public class AzuriteCollection : ICollectionFixture<AzuriteFixture>;

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Azure.Data.Tables;
using FluentAssertions;
using Xunit;

namespace GutAI.IntegrationTests;

[Collection("Azurite")]
public class TableStorageStoreIntegrationTests(AzuriteFixture fx)
{
    [Fact]
    public async Task CanUpsertAndRetrieveRawEntity()
    {
        var entity = new TableEntity("TestPK", "TestRK")
        {
            { "Value", "Hello World" }
        };
        var table = fx.ServiceClient.GetTableClient("gutai");
        await table.CreateIfNotExistsAsync();
        await table.UpsertEntityAsync(entity);

        var result = await table.GetEntityAsync<TableEntity>("TestPK", "TestRK");
        result.Value["Value"].Should().Be("Hello World");
    }
}

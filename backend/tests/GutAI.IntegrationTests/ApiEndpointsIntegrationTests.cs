using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Azure.Data.Tables;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FluentAssertions;
using GutAI.Domain.Entities;
using Xunit;

namespace GutAI.IntegrationTests;

[Collection("Azurite")]
public class ApiEndpointsIntegrationTests(AzuriteFixture fx)
{
    [Fact]
    public async Task TableCreatedAutomatically_OnFirstOperation()
    {
        var userId = Guid.NewGuid();
        await fx.Store.UpsertUserAsync(new User { Id = userId, Email = "auto-create@test.com" });
        var loaded = await fx.Store.GetUserAsync(userId);
        loaded.Should().NotBeNull();
    }
}

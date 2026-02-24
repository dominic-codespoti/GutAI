using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using GutAI.Infrastructure.ExternalApis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GutAI.Infrastructure.Tests;

public class EdamamFoodClientTests
{
    private static EdamamFoodClient CreateClient(HttpMessageHandler handler, string appId = "", string appKey = "")
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ExternalApis:EdamamAppId"] = appId,
                ["ExternalApis:EdamamAppKey"] = appKey,
            })
            .Build();

        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.edamam.com") };
        var logger = Mock.Of<ILogger<EdamamFoodClient>>();
        return new EdamamFoodClient(http, config, logger);
    }

    private static HttpMessageHandler CreateHandler(HttpStatusCode status, object? body = null)
    {
        var handler = new MockHttpHandler(status, body);
        return handler;
    }

    // ─── IsConfigured ───────────────────────────────────────────────────

    [Fact]
    public void IsConfigured_False_WhenNoKeys()
    {
        var client = CreateClient(CreateHandler(HttpStatusCode.OK), "", "");
        client.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public void IsConfigured_True_WhenKeysProvided()
    {
        var client = CreateClient(CreateHandler(HttpStatusCode.OK), "test-id", "test-key");
        client.IsConfigured.Should().BeTrue();
    }

    // ─── ParseNaturalLanguageAsync ──────────────────────────────────────

    [Fact]
    public async Task ParseNatural_ReturnsEmpty_WhenNotConfigured()
    {
        var client = CreateClient(CreateHandler(HttpStatusCode.OK));
        var result = await client.ParseNaturalLanguageAsync("chicken breast");
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ParseNatural_ReturnsParsedItems_FromParsedField()
    {
        var response = new EdamamParserResponse
        {
            Text = "chicken breast",
            Parsed =
            [
                new EdamamParsed
                {
                    Food = new EdamamFood
                    {
                        FoodId = "food_1",
                        Label = "Chicken Breast",
                        Nutrients = new EdamamNutrients
                        {
                            ENERC_KCAL = 165,
                            PROCNT = 31,
                            FAT = 3.6,
                            CHOCDF = 0,
                            FIBTG = 0,
                        }
                    },
                    Quantity = 100,
                }
            ],
            Hints = [],
        };

        var client = CreateClient(CreateHandler(HttpStatusCode.OK, response), "id", "key");
        var result = await client.ParseNaturalLanguageAsync("chicken breast");

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("Chicken Breast");
        result[0].Calories.Should().Be(165m);
        result[0].ProteinG.Should().Be(31m);
    }

    [Fact]
    public async Task ParseNatural_FallsBackToHints_WhenNoParsed()
    {
        var response = new EdamamParserResponse
        {
            Text = "something weird",
            Parsed = [],
            Hints =
            [
                new EdamamHint
                {
                    Food = new EdamamFood
                    {
                        FoodId = "food_2",
                        Label = "Weird Food",
                        Nutrients = new EdamamNutrients
                        {
                            ENERC_KCAL = 200,
                            PROCNT = 10,
                            FAT = 5,
                            CHOCDF = 20,
                            FIBTG = 2,
                        }
                    }
                }
            ],
        };

        var client = CreateClient(CreateHandler(HttpStatusCode.OK, response), "id", "key");
        var result = await client.ParseNaturalLanguageAsync("something weird");

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("Weird Food");
    }

    [Fact]
    public async Task ParseNatural_ReturnsEmpty_OnApiError()
    {
        var client = CreateClient(CreateHandler(HttpStatusCode.InternalServerError), "id", "key");
        var result = await client.ParseNaturalLanguageAsync("test");
        result.Should().BeEmpty();
    }

    // ─── SearchAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task Search_ReturnsEmpty_WhenNotConfigured()
    {
        var client = CreateClient(CreateHandler(HttpStatusCode.OK));
        var result = await client.SearchAsync("chicken");
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Search_ReturnsMappedProducts()
    {
        var response = new EdamamParserResponse
        {
            Hints =
            [
                new EdamamHint
                {
                    Food = new EdamamFood
                    {
                        FoodId = "food_1",
                        Label = "Chicken",
                        Brand = "Farm Fresh",
                        Image = "https://example.com/chicken.jpg",
                        Nutrients = new EdamamNutrients
                        {
                            ENERC_KCAL = 165,
                            PROCNT = 31,
                            FAT = 3.6,
                            CHOCDF = 0,
                            FIBTG = 0,
                        }
                    }
                },
                new EdamamHint
                {
                    Food = new EdamamFood
                    {
                        FoodId = "food_2",
                        Label = "Chicken Wings",
                        Nutrients = new EdamamNutrients
                        {
                            ENERC_KCAL = 203,
                            PROCNT = 30.5,
                            FAT = 8.1,
                            CHOCDF = 0,
                            FIBTG = 0,
                        }
                    }
                },
            ],
        };

        var client = CreateClient(CreateHandler(HttpStatusCode.OK, response), "id", "key");
        var result = await client.SearchAsync("chicken");

        result.Should().HaveCount(2);
        result[0].Name.Should().Be("Chicken");
        result[0].Brand.Should().Be("Farm Fresh");
        result[0].DataSource.Should().Be("Edamam");
        result[0].Calories100g.Should().Be(165m);
    }

    [Fact]
    public async Task Search_DeduplicatesByName()
    {
        var response = new EdamamParserResponse
        {
            Hints =
            [
                new EdamamHint { Food = new EdamamFood { Label = "Chicken", FoodId = "1", Nutrients = new EdamamNutrients { ENERC_KCAL = 165, PROCNT = 31, FAT = 3.6, CHOCDF = 0, FIBTG = 0 } } },
                new EdamamHint { Food = new EdamamFood { Label = "Chicken", FoodId = "2", Nutrients = new EdamamNutrients { ENERC_KCAL = 165, PROCNT = 31, FAT = 3.6, CHOCDF = 0, FIBTG = 0 } } },
            ],
        };

        var client = CreateClient(CreateHandler(HttpStatusCode.OK, response), "id", "key");
        var result = await client.SearchAsync("chicken");
        result.Should().HaveCount(1);
    }

    // ─── LookupBarcodeAsync ─────────────────────────────────────────────

    [Fact]
    public async Task LookupBarcode_ReturnsNull_WhenNotConfigured()
    {
        var client = CreateClient(CreateHandler(HttpStatusCode.OK));
        var result = await client.LookupBarcodeAsync("1234567890");
        result.Should().BeNull();
    }

    [Fact]
    public async Task LookupBarcode_ReturnsMappedProduct()
    {
        var response = new EdamamParserResponse
        {
            Hints =
            [
                new EdamamHint
                {
                    Food = new EdamamFood
                    {
                        FoodId = "food_bc",
                        Label = "Nutella",
                        Brand = "Ferrero",
                        Nutrients = new EdamamNutrients
                        {
                            ENERC_KCAL = 539,
                            PROCNT = 6.3,
                            FAT = 30.9,
                            CHOCDF = 57.5,
                            FIBTG = 3.4,
                        }
                    }
                },
            ],
        };

        var client = CreateClient(CreateHandler(HttpStatusCode.OK, response), "id", "key");
        var result = await client.LookupBarcodeAsync("3017620422003");

        result.Should().NotBeNull();
        result!.Name.Should().Be("Nutella");
        result.Brand.Should().Be("Ferrero");
        result.Barcode.Should().Be("3017620422003");
        result.DataSource.Should().Be("Edamam");
    }

    [Fact]
    public async Task LookupBarcode_ReturnsNull_WhenNoHints()
    {
        var response = new EdamamParserResponse { Hints = [] };
        var client = CreateClient(CreateHandler(HttpStatusCode.OK, response), "id", "key");
        var result = await client.LookupBarcodeAsync("0000000000000");
        result.Should().BeNull();
    }

    // ─── IsFodmapFreeAsync ──────────────────────────────────────────────

    [Fact]
    public async Task IsFodmapFree_ReturnsFalse_WhenNotConfigured()
    {
        var client = CreateClient(CreateHandler(HttpStatusCode.OK));
        var result = await client.IsFodmapFreeAsync("chicken");
        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsFodmapFree_ReturnsTrue_WhenHintsPresent()
    {
        var response = new EdamamParserResponse
        {
            Hints = [new EdamamHint { Food = new EdamamFood { Label = "Chicken", Nutrients = new EdamamNutrients() } }],
        };

        var client = CreateClient(CreateHandler(HttpStatusCode.OK, response), "id", "key");
        var result = await client.IsFodmapFreeAsync("chicken");
        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsFodmapFree_ReturnsFalse_WhenNoHints()
    {
        var response = new EdamamParserResponse { Hints = [] };
        var client = CreateClient(CreateHandler(HttpStatusCode.OK, response), "id", "key");
        var result = await client.IsFodmapFreeAsync("garlic");
        result.Should().BeFalse();
    }

    // ─── Scaling / Quantity ─────────────────────────────────────────────

    [Fact]
    public async Task ParseNatural_ScalesByQuantity()
    {
        var response = new EdamamParserResponse
        {
            Text = "200g chicken",
            Parsed =
            [
                new EdamamParsed
                {
                    Food = new EdamamFood
                    {
                        Label = "Chicken",
                        Nutrients = new EdamamNutrients
                        {
                            ENERC_KCAL = 165, // per 100g
                            PROCNT = 31,
                            FAT = 3.6,
                            CHOCDF = 0,
                            FIBTG = 0,
                        }
                    },
                    Quantity = 200,
                }
            ],
        };

        var client = CreateClient(CreateHandler(HttpStatusCode.OK, response), "id", "key");
        var result = await client.ParseNaturalLanguageAsync("200g chicken");

        result[0].Calories.Should().Be(330m); // 165 * 2
        result[0].ProteinG.Should().Be(62m); // 31 * 2
        result[0].ServingWeightG.Should().Be(200m);
    }
}

internal class MockHttpHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _statusCode;
    private readonly object? _body;

    public MockHttpHandler(HttpStatusCode statusCode, object? body = null)
    {
        _statusCode = statusCode;
        _body = body;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = new HttpResponseMessage(_statusCode);
        if (_body is not null)
        {
            response.Content = JsonContent.Create(_body, options: new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            });
        }
        return Task.FromResult(response);
    }
}

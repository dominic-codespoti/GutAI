using System.Net;
using FluentAssertions;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;
using GutAI.Infrastructure.ExternalApis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GutAI.Infrastructure.Tests;

public class CompositeNutritionServiceTests
{
    private readonly Mock<ILogger<CompositeNutritionService>> _loggerMock = new();
    private readonly Mock<IFoodApiService> _foodApiMock = new();

    private CalorieNinjasClient CreateCalorieNinjas(HttpMessageHandler handler, string apiKey = "")
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ExternalApis:CalorieNinjasApiKey"] = apiKey
            })
            .Build();
        return new CalorieNinjasClient(new HttpClient(handler), config, Mock.Of<ILogger<CalorieNinjasClient>>());
    }

    private NaturalLanguageFallbackService CreateFallback() =>
        new(_foodApiMock.Object, Mock.Of<ITableStore>(), Mock.Of<ILogger<NaturalLanguageFallbackService>>());

    private void SetupFoodApi(string searchTerm, string resultName, decimal cal = 155)
    {
        _foodApiMock.Setup(x => x.SearchAsync(It.Is<string>(s => s.Contains(searchTerm)), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new FoodProductDto { Name = resultName, Calories100g = cal, Protein100g = 13, Carbs100g = 1, Fat100g = 11 }]);
    }

    private EdamamFoodClient CreateEdamam(string appId = "", string appKey = "")
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ExternalApis:EdamamAppId"] = appId,
                ["ExternalApis:EdamamAppKey"] = appKey,
            })
            .Build();
        return new EdamamFoodClient(new HttpClient(), config, Mock.Of<ILogger<EdamamFoodClient>>());
    }

    [Fact]
    public async Task FallsBack_WhenApiKeyIsEmpty()
    {
        SetupFoodApi("eggs", "Egg");

        var client = CreateCalorieNinjas(new FakeHandler(HttpStatusCode.OK), apiKey: "");
        var svc = new CompositeNutritionService(CreateEdamam(), client, CreateFallback(), _loggerMock.Object);

        var result = await svc.ParseNaturalLanguageAsync("2 eggs");

        result.Should().NotBeEmpty();
        result[0].Name.Should().Be("Egg");
    }

    [Fact]
    public async Task FallsBack_On401()
    {
        SetupFoodApi("eggs", "Egg");

        var client = CreateCalorieNinjas(new FakeHandler(HttpStatusCode.Unauthorized), apiKey: "bad-key");
        var svc = new CompositeNutritionService(CreateEdamam(), client, CreateFallback(), _loggerMock.Object);

        var result = await svc.ParseNaturalLanguageAsync("eggs");
        result.Should().NotBeEmpty();
    }

    [Fact]
    public async Task FallsBack_On429()
    {
        SetupFoodApi("eggs", "Egg");

        var client = CreateCalorieNinjas(new FakeHandler(HttpStatusCode.TooManyRequests), apiKey: "valid-key");
        var svc = new CompositeNutritionService(CreateEdamam(), client, CreateFallback(), _loggerMock.Object);

        var result = await svc.ParseNaturalLanguageAsync("eggs");
        result.Should().NotBeEmpty();
    }

    [Fact]
    public async Task FallsBack_OnNetworkError()
    {
        SetupFoodApi("eggs", "Egg");

        var client = CreateCalorieNinjas(new FakeHandler(new HttpRequestException("DNS failure")), apiKey: "valid-key");
        var svc = new CompositeNutritionService(CreateEdamam(), client, CreateFallback(), _loggerMock.Object);

        var result = await svc.ParseNaturalLanguageAsync("eggs");
        result.Should().NotBeEmpty();
    }

    [Fact]
    public async Task FallsBack_On500()
    {
        SetupFoodApi("eggs", "Egg");

        var client = CreateCalorieNinjas(new FakeHandler(HttpStatusCode.InternalServerError), apiKey: "valid-key");
        var svc = new CompositeNutritionService(CreateEdamam(), client, CreateFallback(), _loggerMock.Object);

        var result = await svc.ParseNaturalLanguageAsync("eggs");
        result.Should().NotBeEmpty();
    }

    [Fact]
    public async Task UsesCalorieNinjas_WhenSuccessful()
    {
        var json = """{"items":[{"name":"Egg","calories":78,"serving_size_g":50,"fat_total_g":5,"fat_saturated_g":1.6,"protein_g":6,"sodium_mg":62,"potassium_mg":63,"cholesterol_mg":186,"carbohydrates_total_g":0.6,"fiber_g":0,"sugar_g":0.6}]}""";
        var client = CreateCalorieNinjas(new FakeHandler(HttpStatusCode.OK, json), apiKey: "real-key");
        var svc = new CompositeNutritionService(CreateEdamam(), client, CreateFallback(), _loggerMock.Object);

        var result = await svc.ParseNaturalLanguageAsync("1 egg");

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("Egg");
        result[0].Calories.Should().Be(78);
        _foodApiMock.Verify(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private class FakeHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode? _statusCode;
        private readonly string? _content;
        private readonly Exception? _exception;

        public FakeHandler(HttpStatusCode statusCode, string? content = null)
        {
            _statusCode = statusCode;
            _content = content;
        }

        public FakeHandler(Exception exception) => _exception = exception;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (_exception is not null) throw _exception;
            var response = new HttpResponseMessage(_statusCode!.Value);
            if (_content is not null)
                response.Content = new StringContent(_content, System.Text.Encoding.UTF8, "application/json");
            return Task.FromResult(response);
        }
    }
}

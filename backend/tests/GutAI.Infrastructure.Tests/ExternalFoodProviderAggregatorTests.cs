using FluentAssertions;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;
using GutAI.Domain.Enums;
using GutAI.Infrastructure.ExternalApis;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GutAI.Infrastructure.Tests;

public class ExternalFoodProviderAggregatorTests
{
    private static FoodProductDto MakeFood(string name, string source = "Test") =>
        new() { Id = Guid.NewGuid(), Name = name, DataSource = source };

    private static Mock<IFoodProvider> MakeProvider(
        string source, FoodProviderCapabilities capabilities,
        Func<CancellationToken, Task<IReadOnlyList<FoodProductDto>>>? search = null,
        Func<CancellationToken, Task<FoodProductDto?>>? barcode = null)
    {
        var mock = new Mock<IFoodProvider>();
        mock.Setup(p => p.SourceName).Returns(source);
        mock.Setup(p => p.Capabilities).Returns(capabilities);
        if (search is not null)
            mock.Setup(p => p.SearchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string _, CancellationToken ct) => search(ct));
        if (barcode is not null)
            mock.Setup(p => p.LookupBarcodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string _, CancellationToken ct) => barcode(ct));
        return mock;
    }

    private static ExternalFoodProviderAggregator MakeAggregator(IEnumerable<IFoodProvider> providers) =>
        new(providers, NullLogger<ExternalFoodProviderAggregator>.Instance);

    [Fact]
    public async Task SearchAsync_OneProviderFails_OthersStillReturn()
    {
        var good = MakeProvider("Good", FoodProviderCapabilities.Search,
            search: _ => Task.FromResult<IReadOnlyList<FoodProductDto>>([MakeFood("Banana")]));
        var bad = MakeProvider("Bad", FoodProviderCapabilities.Search,
            search: _ => throw new HttpRequestException("boom"));

        var outcome = await MakeAggregator([good.Object, bad.Object]).SearchAsync("banana");

        outcome.Candidates.Should().ContainSingle(c => c.Name == "Banana");
        outcome.ProviderOutcomes.Should().Contain(o => o.Source == "Good" && o.Status == ProviderSearchStatus.Success);
        outcome.ProviderOutcomes.Should().Contain(o => o.Source == "Bad" && o.Status == ProviderSearchStatus.Failed);
    }

    [Fact]
    public async Task SearchAsync_AllProvidersFail_ReturnsEmptyWithFailedOutcomes()
    {
        var bad1 = MakeProvider("Bad1", FoodProviderCapabilities.Search, search: _ => throw new Exception("x"));
        var bad2 = MakeProvider("Bad2", FoodProviderCapabilities.Search, search: _ => throw new Exception("y"));

        var outcome = await MakeAggregator([bad1.Object, bad2.Object]).SearchAsync("anything");

        outcome.Candidates.Should().BeEmpty();
        outcome.ProviderOutcomes.Should().OnlyContain(o => o.Status == ProviderSearchStatus.Failed);
    }

    [Fact]
    public async Task SearchAsync_SkipsProvidersWithoutSearchCapability()
    {
        var searchable = MakeProvider("Searchable", FoodProviderCapabilities.Search,
            search: _ => Task.FromResult<IReadOnlyList<FoodProductDto>>([MakeFood("Rice")]));
        var barcodeOnly = MakeProvider("BarcodeOnly", FoodProviderCapabilities.Barcode);

        var outcome = await MakeAggregator([searchable.Object, barcodeOnly.Object]).SearchAsync("rice");

        outcome.ProviderOutcomes.Should().ContainSingle();
        outcome.ProviderOutcomes[0].Source.Should().Be("Searchable");
        barcodeOnly.Verify(p => p.SearchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SearchAsync_CallerCancellation_PropagatesInsteadOfBeingSwallowed()
    {
        using var cts = new CancellationTokenSource();
        var provider = MakeProvider("Slow", FoodProviderCapabilities.Search, search: async ct =>
        {
            cts.Cancel();
            ct.ThrowIfCancellationRequested();
            return [];
        });

        var act = () => MakeAggregator([provider.Object]).SearchAsync("query", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task LookupBarcodeAsync_OnlyCallsBarcodeCapableProviders()
    {
        var searchOnly = MakeProvider("SearchOnly", FoodProviderCapabilities.Search);
        var barcodeCapable = MakeProvider("BarcodeCapable", FoodProviderCapabilities.Barcode,
            barcode: _ => Task.FromResult<FoodProductDto?>(MakeFood("Diet Coke")));

        var result = await MakeAggregator([searchOnly.Object, barcodeCapable.Object]).LookupBarcodeAsync("049000028911");

        result!.Name.Should().Be("Diet Coke");
        searchOnly.Verify(p => p.LookupBarcodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LookupBarcodeAsync_FirstProviderFails_FallsThroughToNext()
    {
        var failing = MakeProvider("Failing", FoodProviderCapabilities.Barcode,
            barcode: _ => throw new HttpRequestException("down"));
        var working = MakeProvider("Working", FoodProviderCapabilities.Barcode,
            barcode: _ => Task.FromResult<FoodProductDto?>(MakeFood("Found")));

        var result = await MakeAggregator([failing.Object, working.Object]).LookupBarcodeAsync("123");

        result!.Name.Should().Be("Found");
    }

    [Fact]
    public async Task LookupBarcodeAsync_NoProviderHasMatch_ReturnsNull()
    {
        var provider = MakeProvider("None", FoodProviderCapabilities.Barcode,
            barcode: _ => Task.FromResult<FoodProductDto?>(null));

        var result = await MakeAggregator([provider.Object]).LookupBarcodeAsync("000");

        result.Should().BeNull();
    }
}

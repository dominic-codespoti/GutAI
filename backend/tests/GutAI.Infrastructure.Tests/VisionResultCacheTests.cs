using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;
using GutAI.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace GutAI.Infrastructure.Tests;

public sealed class VisionResultCacheTests
{
    [Fact]
    public void BuildKey_is_stable_and_changes_for_every_cache_dimension()
    {
        var user = Guid.NewGuid();
        var image = new byte[] { 1, 2, 3 };
        var key = VisionResultCache.BuildKey(user, image, "p1", "model", "low", "variant");

        Assert.Equal(key, VisionResultCache.BuildKey(user, [1, 2, 3], "p1", "model", "low", "variant"));
        Assert.NotEqual(key, VisionResultCache.BuildKey(Guid.NewGuid(), image, "p1", "model", "low", "variant"));
        Assert.NotEqual(key, VisionResultCache.BuildKey(user, [1, 2, 4], "p1", "model", "low", "variant"));
        Assert.NotEqual(key, VisionResultCache.BuildKey(user, image, "p2", "model", "low", "variant"));
        Assert.NotEqual(key, VisionResultCache.BuildKey(user, image, "p1", "other", "low", "variant"));
        Assert.NotEqual(key, VisionResultCache.BuildKey(user, image, "p1", "model", "high", "variant"));
        Assert.NotEqual(key, VisionResultCache.BuildKey(user, image, "p1", "model", "low", "other"));
    }

    [Fact]
    public async Task Cache_roundtrips_with_default_and_configured_expiry()
    {
        var cacheService = new CapturingCacheService();
        var cache = new VisionResultCache(cacheService, Configuration());
        var value = ExampleVisionResult();

        await cache.SetAsync("key", value);
        var loaded = await cache.GetAsync("key");

        Assert.Same(value, loaded);
        Assert.Equal(TimeSpan.FromHours(24), cacheService.Expiry);
        Assert.Equal("key", cacheService.LastKey);

        var configuredService = new CapturingCacheService();
        var configuredCache = new VisionResultCache(configuredService, Configuration("MealScan:VisionCacheHours", "3.5"));
        await configuredCache.SetAsync("another", value);
        Assert.Equal(TimeSpan.FromHours(3.5), configuredService.Expiry);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public async Task Disabled_cache_never_calls_underlying_cache(string hours)
    {
        var cacheService = new CapturingCacheService();
        var cache = new VisionResultCache(cacheService, Configuration("MealScan:VisionCacheHours", hours));

        Assert.Null(await cache.GetAsync("key"));
        await cache.SetAsync("key", ExampleVisionResult());

        Assert.Equal(0, cacheService.GetCalls);
        Assert.Equal(0, cacheService.SetCalls);
    }

    private static VisionDecomposition ExampleVisionResult() => new(
        [new ScannedComponent { Name = "rice", EstimatedGramsMidpoint = 150 }],
        false,
        "",
        0.8m,
        [],
        "{}",
        "prompt-v1",
        10,
        5);

    private static IConfiguration Configuration(string? key = null, string? value = null)
    {
        var values = new Dictionary<string, string?>();
        if (key is not null)
            values[key] = value;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private sealed class CapturingCacheService : ICacheService
    {
        private readonly Dictionary<string, object> _values = [];
        public int GetCalls { get; private set; }
        public int SetCalls { get; private set; }
        public string? LastKey { get; private set; }
        public TimeSpan? Expiry { get; private set; }

        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
        {
            GetCalls++;
            LastKey = key;
            return Task.FromResult(_values.TryGetValue(key, out var value) ? (T?)value : default);
        }

        public Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default)
        {
            SetCalls++;
            LastKey = key;
            Expiry = expiry;
            _values[key] = value!;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken ct = default)
        {
            _values.Remove(key);
            return Task.CompletedTask;
        }
    }
}

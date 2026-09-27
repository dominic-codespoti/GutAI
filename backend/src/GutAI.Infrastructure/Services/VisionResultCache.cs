using System.Security.Cryptography;
using GutAI.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;

namespace GutAI.Infrastructure.Services;

public sealed class VisionResultCache
{
    private readonly ICacheService _cache;
    private readonly TimeSpan _ttl;

    public VisionResultCache(ICacheService cache, IConfiguration config)
    {
        _cache = cache;
        _ttl = TimeSpan.FromHours(config.GetValue("MealScan:VisionCacheHours", 24d));
    }

    public static string BuildKey(Guid userId, byte[] image, string promptVersion, string deployment, string effort, string variant)
    {
        var imageHash = Convert.ToHexString(SHA256.HashData(image)).ToLowerInvariant();
        var variantHash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(variant))).ToLowerInvariant();
        return $"vision:{userId:N}:{imageHash}:{promptVersion}:{deployment}:{effort}:{variantHash[..16]}";
    }

    public Task<VisionDecomposition?> GetAsync(string key, CancellationToken ct = default) =>
        _ttl <= TimeSpan.Zero ? Task.FromResult<VisionDecomposition?>(null) : _cache.GetAsync<VisionDecomposition>(key, ct);

    public Task SetAsync(string key, VisionDecomposition value, CancellationToken ct = default) =>
        _ttl <= TimeSpan.Zero ? Task.CompletedTask : _cache.SetAsync(key, value, _ttl, ct);
}

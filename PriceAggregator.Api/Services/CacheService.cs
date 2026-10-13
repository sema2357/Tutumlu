using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace PriceAggregator.Api.Services;

public interface ICacheService
{
    Task<T?> GetOrSetAsync<T>(string key, TimeSpan expiration, Func<Task<T>> factory);
    Task RemoveAsync(string key);
}

public class CacheService : ICacheService
{
    private readonly IDistributedCache _cache;

    public CacheService(IDistributedCache cache)
    {
        _cache = cache;
    }

    public async Task<T?> GetOrSetAsync<T>(string key, TimeSpan expiration, Func<Task<T>> factory)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            var cached = await _cache.GetStringAsync(key, cts.Token);
            if (!string.IsNullOrEmpty(cached))
            {
                var deserialized = JsonSerializer.Deserialize<T>(cached);
                if (deserialized != null) return deserialized;
            }
        }
        catch
        {
        }

        var data = await factory();

        if (data != null)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                await _cache.SetStringAsync(key, JsonSerializer.Serialize(data), new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = expiration
                }, cts.Token);
            }
            catch
            {
            }
        }

        return data;
    }

    public async Task RemoveAsync(string key)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            await _cache.RemoveAsync(key, cts.Token);
        }
        catch
        {
        }
    }
}

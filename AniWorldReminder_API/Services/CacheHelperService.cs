using Microsoft.Extensions.Caching.Distributed;
using System.Text;
using System.Text.Json;

namespace AniWorldReminder_API.Services
{
    public class CacheHelperService(ILogger<CacheHelperService> logger, IDistributedCache cache) : ICacheHelperService
    {
        public async Task<T?> GetCacheAsync<T>(string cachePath) where T : class
        {
            byte[]? cachedData = await cache.GetAsync(cachePath);

            if (cachedData is null || cachedData.Length == 0)
            {
                logger.LogWarning($"{DateTime.Now} | [{nameof(CacheHelperService)}.{nameof(GetCacheAsync)}] no cache data found at path: {cachePath}.");
                return null;
            }
                        
            logger.LogInformation($"{DateTime.Now} | [{nameof(CacheHelperService)}.{nameof(GetCacheAsync)}] cache data retrieved at: {cachePath}. (size: {cachedData.Length:00.####} bytes)");
            
            return JsonSerializer.Deserialize<T>(cachedData);
        }

        public async Task SetCacheAsync<T>(string cachePath, T data, int durationInMinutes) where T : class
        {
            await cache.SetAsync(cachePath, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data)), new DistributedCacheEntryOptions()
            {
                SlidingExpiration = TimeSpan.FromMinutes(durationInMinutes)
            });

            logger.LogInformation($"{DateTime.Now} | [{nameof(CacheHelperService)}.{nameof(SetCacheAsync)}] cache data set at: {cachePath}.");
        }
    }
}

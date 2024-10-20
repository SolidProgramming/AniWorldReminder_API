namespace AniWorldReminder_API.Interfaces
{
    public interface ICacheHelperService
    {
        public Task<T?> GetCacheAsync<T>(string cachePath) where T : class;
        public Task SetCacheAsync<T>(string cachePath, T data, int durationInMinutes) where T : class;
    }
}

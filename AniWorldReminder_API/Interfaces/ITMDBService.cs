namespace AniWorldReminder_API.Interfaces
{
    public interface ITMDBService
    {
        Task<TMDBSearchTVModel?> SearchTVShow(string tvShowName);
        Task<TMDBSearchTVByIdModel?> SearchTVShowById(int? tvShowId);
    }
}

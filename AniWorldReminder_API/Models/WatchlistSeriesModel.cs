namespace AniWorldReminder_API.Models
{
    public class WatchlistSeriesModel
    {
        public WatchlistModel? Watchlist { get; set; }
        public List<SeriesModel> Series { get; set; } = [];
    }
}

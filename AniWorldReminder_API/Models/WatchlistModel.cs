namespace AniWorldReminder_API.Models
{
    public class WatchlistModel
    {
        public string? Id { get; set; }
        public string? Ident { get; set; }
        public SeriesModel Series { get; set; } = new();
    }
}

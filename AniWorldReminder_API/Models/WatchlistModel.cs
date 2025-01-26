namespace AniWorldReminder_API.Models
{
    public class WatchlistModel
    {
        public string? Name { get; set; }
        public string? Id { get; set; }
        public string? Ident { get; set; }
        public List<SeriesModel> Series { get; set; } = [];
    }
}

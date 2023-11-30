namespace AniWorldReminder_API.Models
{
    public class SeriesInfoModel
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public int SeasonCount { get; set; }
        public string? CoverArtUrl { get; set; }
        public string? DirectLink { get; set; }
        public List<SeasonModel> Seasons { get; set; } = new List<SeasonModel>();
    }
}

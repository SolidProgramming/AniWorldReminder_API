namespace AniWorldReminder_API.Models
{
    public class AddDownloadsRequestModel
    {
        public string? SeriesId { get; set; }
        public List<EpisodeModel>? Episodes { get; set; }
    }
}

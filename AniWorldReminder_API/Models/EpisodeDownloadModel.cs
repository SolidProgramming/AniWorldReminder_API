namespace AniWorldReminder_API.Models
{
    public class EpisodeDownloadModel
    {
        public DownloadModel Download { get; set; } = default!;
        public StreamingPortalModel StreamingPortal { get; set; } = default!;
    }
}

namespace AniWorldReminder_API.Models
{
    public class SeriesModel
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? CoverArtUrl { get; set; }
        public string? Path { get; set; }
        public StreamingPortal StreamingPortal { get; set; }
        public Language LanguageFlag { get; set; }
    }
}

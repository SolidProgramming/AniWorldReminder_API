namespace AniWorldReminder_API.Models
{
    public class EpisodeModel
    {
        public int Id { get; set; }
        public int SeriesId { get; set; }
        public int Season { get; set; }
        public int Episode { get; set; }
        public string? Name { get; set; }
        public Language Languages { get; set; }
        public Language LanguageFlag
        {
            get => Languages;
            set => Languages = value;
        }
        public IEnumerable<Language>? UpdatedLanguageFlags { get; set; }
        public string? M3U8DirectLink { get; set; }
        public List<DirectViewLinkModel>? DirectViewLinks { get; set; }
    }
}


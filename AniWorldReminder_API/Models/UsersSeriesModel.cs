namespace AniWorldReminder_API.Models
{
    public class UsersSeriesModel
    {
        public int Id { get; set; }
        public UserModel? Users { get; set; }
        public SeriesModel? Series { get; set; }
        public Language LanguageFlag { get; set; }
        public DateTime? Added { get; set; }
        public DateTime? Updated { get; set; }
    }
}

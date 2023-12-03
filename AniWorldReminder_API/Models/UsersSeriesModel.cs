namespace AniWorldReminder_API.Models
{
    public class UsersSeriesModel
    {
        public int Id { get; set; }
        public UserModel? Users { get; set; }
        public SeriesModel? Series { get; set; }
        public Language LanguageFlag { get; set; }
    }
}

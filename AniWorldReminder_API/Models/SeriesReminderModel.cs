namespace AniWorldReminder_API.Models
{
    public class SeriesReminderModel
    {
        public SeriesModel? Series { get; set; }
        public UserModel? User { get; set; }
        public Language Language { get; set; }
    }
}

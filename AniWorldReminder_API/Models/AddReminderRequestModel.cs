namespace AniWorldReminder_API.Models
{
    public class AddReminderRequestModel
    {
        public string Username { get; set; }
        public string SeriesName { get; set; }
        public StreamingPortal StreamingPortal { get; set; }
        public Language Language { get; set; }
    }
}

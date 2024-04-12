
namespace AniWorldReminder_API.Models
{
    public class DownloaderPreferencesModel
    {
        public int Interval { get; set; }

        public bool AutoStart { get; set; }

        public bool TelegramCaptchaNotification { get; set; }
    }
}


using System.Text.Json.Serialization;

namespace AniWorldReminder_API.Models
{
    public class DownloaderPreferencesModel
    {
        public int Interval { get; set; } = 15;
        public bool AutoStart { get; set; } = true;
        public bool TelegramCaptchaNotification { get; set; } = true;
        public bool UseProxy { get; set; }
        public string? ProxyUri { get; set; }
        public string? ProxyUsername { get; set; }
        public string? ProxyPassword { get; set; }
    }
}

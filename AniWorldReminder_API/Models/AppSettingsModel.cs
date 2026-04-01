using System.Text.Json.Serialization;

namespace AniWorldReminder_API.Models
{
    public class AppSettingsModel
    {
        [JsonPropertyName("AddSwagger")]
        public bool AddSwagger { get; set; } = default!;

        [JsonPropertyName("EnableEpisodeReminderJob")]
        public bool EnableEpisodeReminderJob { get; set; } = true;

        [JsonPropertyName("EpisodeReminderCron")]
        public string EpisodeReminderCron { get; set; } = "0 * * * *";

        [JsonPropertyName("EnableHangfireDashboard")]
        public bool EnableHangfireDashboard { get; set; }

        [JsonPropertyName("HangfireDashboardPath")]
        public string HangfireDashboardPath { get; set; } = "/hangfire";

        [JsonPropertyName("EpisodeReminderSeriesDelayMs")]
        public int EpisodeReminderSeriesDelayMs { get; set; } = 1500;

        [JsonPropertyName("EpisodeReminderNotificationDelayMs")]
        public int EpisodeReminderNotificationDelayMs { get; set; } = 500;
    }
}

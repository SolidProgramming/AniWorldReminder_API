using AniWorldReminder_API.Models;
using Xunit;

namespace AniWorldReminder_API.Tests.Models
{
    public class AppSettingsModelTests
    {
        [Fact]
        public void AppSettingsModel_HasConservativeEpisodeReminderDelayDefaults()
        {
            AppSettingsModel settings = new();

            Assert.Equal(1500, settings.EpisodeReminderSeriesDelayMs);
            Assert.Equal(500, settings.EpisodeReminderNotificationDelayMs);
        }
    }
}

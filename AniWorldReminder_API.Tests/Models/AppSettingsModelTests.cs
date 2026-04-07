using AniWorldReminder_API.Models;
using NUnit.Framework;

namespace AniWorldReminder_API.Tests.Models
{
    public class AppSettingsModelTests
    {
        [Test]
        public void AppSettingsModel_HasConservativeEpisodeReminderDelayDefaults()
        {
            AppSettingsModel settings = new();

            Assert.AreEqual(1500, settings.EpisodeReminderSeriesDelayMs);
            Assert.AreEqual(500, settings.EpisodeReminderNotificationDelayMs);
        }
    }
}

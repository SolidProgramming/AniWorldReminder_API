namespace AniWorldReminder_API.Services
{
    public class EpisodeReminderDelayService(ILogger<EpisodeReminderDelayService> logger, IDelayExecutor delayExecutor) : IEpisodeReminderDelayService
    {
        private const int MinimumDelayMs = 1000;
        private const int DefaultSeriesDelayMs = 1500;
        private const int DefaultNotificationDelayMs = 500;

        public Task DelayAfterSeriesScanAsync(AppSettingsModel? appSettings, CancellationToken cancellationToken = default)
        {
            return DelayAsync(
                GetDelayOrDefault(appSettings?.EpisodeReminderSeriesDelayMs, DefaultSeriesDelayMs),
                "series scan",
                cancellationToken);
        }

        public Task DelayAfterNotificationAsync(AppSettingsModel? appSettings, CancellationToken cancellationToken = default)
        {
            return DelayAsync(
                GetDelayOrDefault(appSettings?.EpisodeReminderNotificationDelayMs, DefaultNotificationDelayMs),
                "notification",
                cancellationToken);
        }

        private async Task DelayAsync(int delayMs, string reason, CancellationToken cancellationToken)
        {
            if (delayMs <= 0)
                return;

            int effectiveDelayMs = GetEffectiveDelay(delayMs);

            logger.LogInformation("{Timestamp} | Waiting {DelayMs} ms before next {Reason}.", DateTime.Now, effectiveDelayMs, reason);
            await delayExecutor.DelayAsync(TimeSpan.FromMilliseconds(effectiveDelayMs), cancellationToken);
        }

        private static int GetDelayOrDefault(int? configuredDelayMs, int defaultDelayMs)
        {
            return configuredDelayMs > 0 ? configuredDelayMs.Value : defaultDelayMs;
        }

        private static int GetEffectiveDelay(int maxDelayMs)
        {
            if (maxDelayMs <= 0)
                return 0;

            if (maxDelayMs <= MinimumDelayMs)
                return maxDelayMs;

            return Random.Shared.Next(MinimumDelayMs, maxDelayMs + 1);
        }
    }
}

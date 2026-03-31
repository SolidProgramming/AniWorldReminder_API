namespace AniWorldReminder_API.Interfaces
{
    public interface IEpisodeReminderDelayService
    {
        Task DelayAfterSeriesScanAsync(AppSettingsModel? appSettings, CancellationToken cancellationToken = default);
        Task DelayAfterNotificationAsync(AppSettingsModel? appSettings, CancellationToken cancellationToken = default);
    }
}

namespace AniWorldReminder_API.Interfaces
{
    public interface IDelayExecutor
    {
        Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default);
    }
}

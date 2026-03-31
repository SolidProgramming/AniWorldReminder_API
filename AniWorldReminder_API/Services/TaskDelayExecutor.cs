namespace AniWorldReminder_API.Services
{
    public class TaskDelayExecutor : IDelayExecutor
    {
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default)
        {
            return Task.Delay(delay, cancellationToken);
        }
    }
}

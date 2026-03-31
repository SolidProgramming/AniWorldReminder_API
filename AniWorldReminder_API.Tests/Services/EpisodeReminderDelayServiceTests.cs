using AniWorldReminder_API.Interfaces;
using AniWorldReminder_API.Models;
using AniWorldReminder_API.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AniWorldReminder_API.Tests.Services
{
    public class EpisodeReminderDelayServiceTests
    {
        [Fact]
        public async Task DelayAfterSeriesScanAsync_UsesDefaultDelay_WhenSettingsAreMissing()
        {
            FakeDelayExecutor delayExecutor = new();
            EpisodeReminderDelayService service = new(NullLogger<EpisodeReminderDelayService>.Instance, delayExecutor);

            await service.DelayAfterSeriesScanAsync(null);

            Assert.Single(delayExecutor.Delays);
            Assert.InRange(delayExecutor.Delays[0], TimeSpan.FromMilliseconds(1000), TimeSpan.FromMilliseconds(1500));
        }

        [Fact]
        public async Task DelayAfterSeriesScanAsync_UsesDefaultDelay_WhenConfiguredDelayIsInvalid()
        {
            FakeDelayExecutor delayExecutor = new();
            EpisodeReminderDelayService service = new(NullLogger<EpisodeReminderDelayService>.Instance, delayExecutor);
            AppSettingsModel appSettings = new()
            {
                EpisodeReminderSeriesDelayMs = 0
            };

            await service.DelayAfterSeriesScanAsync(appSettings);

            Assert.Single(delayExecutor.Delays);
            Assert.InRange(delayExecutor.Delays[0], TimeSpan.FromMilliseconds(1000), TimeSpan.FromMilliseconds(1500));
        }

        [Fact]
        public async Task DelayAfterNotificationAsync_UsesConfiguredDelay()
        {
            FakeDelayExecutor delayExecutor = new();
            EpisodeReminderDelayService service = new(NullLogger<EpisodeReminderDelayService>.Instance, delayExecutor);
            AppSettingsModel appSettings = new()
            {
                EpisodeReminderNotificationDelayMs = 750
            };

            await service.DelayAfterNotificationAsync(appSettings);

            Assert.Single(delayExecutor.Delays);
            Assert.Equal(TimeSpan.FromMilliseconds(750), delayExecutor.Delays[0]);
        }

        [Fact]
        public async Task DelayAfterNotificationAsync_UsesConfiguredRange_WhenDelayExceedsMinimum()
        {
            FakeDelayExecutor delayExecutor = new();
            EpisodeReminderDelayService service = new(NullLogger<EpisodeReminderDelayService>.Instance, delayExecutor);
            AppSettingsModel appSettings = new()
            {
                EpisodeReminderNotificationDelayMs = 2000
            };

            await service.DelayAfterNotificationAsync(appSettings);

            Assert.Single(delayExecutor.Delays);
            Assert.InRange(delayExecutor.Delays[0], TimeSpan.FromMilliseconds(1000), TimeSpan.FromMilliseconds(2000));
        }

        private sealed class FakeDelayExecutor : IDelayExecutor
        {
            public List<TimeSpan> Delays { get; } = [];

            public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default)
            {
                Delays.Add(delay);
                return Task.CompletedTask;
            }
        }
    }
}

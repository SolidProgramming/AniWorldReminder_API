using AniWorldReminder_API.Interfaces;
using AniWorldReminder_API.Models;
using AniWorldReminder_API.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace AniWorldReminder_API.Tests.Services
{
    public class EpisodeReminderDelayServiceTests
    {
        [Test]
        public async Task DelayAfterSeriesScanAsync_UsesDefaultDelay_WhenSettingsAreMissing()
        {
            FakeDelayExecutor delayExecutor = new();
            EpisodeReminderDelayService service = new(NullLogger<EpisodeReminderDelayService>.Instance, delayExecutor);

            await service.DelayAfterSeriesScanAsync(null);

            Assert.That(delayExecutor.Delays, Has.Count.EqualTo(1));
            Assert.That(delayExecutor.Delays[0], Is.InRange(TimeSpan.FromMilliseconds(1000), TimeSpan.FromMilliseconds(1500)));
        }

        [Test]
        public async Task DelayAfterSeriesScanAsync_UsesDefaultDelay_WhenConfiguredDelayIsInvalid()
        {
            FakeDelayExecutor delayExecutor = new();
            EpisodeReminderDelayService service = new(NullLogger<EpisodeReminderDelayService>.Instance, delayExecutor);
            AppSettingsModel appSettings = new()
            {
                EpisodeReminderSeriesDelayMs = 0
            };

            await service.DelayAfterSeriesScanAsync(appSettings);

            Assert.That(delayExecutor.Delays, Has.Count.EqualTo(1));
            Assert.That(delayExecutor.Delays[0], Is.InRange(TimeSpan.FromMilliseconds(1000), TimeSpan.FromMilliseconds(1500)));
        }

        [Test]
        public async Task DelayAfterNotificationAsync_UsesConfiguredDelay()
        {
            FakeDelayExecutor delayExecutor = new();
            EpisodeReminderDelayService service = new(NullLogger<EpisodeReminderDelayService>.Instance, delayExecutor);
            AppSettingsModel appSettings = new()
            {
                EpisodeReminderNotificationDelayMs = 750
            };

            await service.DelayAfterNotificationAsync(appSettings);

            Assert.That(delayExecutor.Delays, Has.Count.EqualTo(1));
            Assert.That(delayExecutor.Delays[0], Is.EqualTo(TimeSpan.FromMilliseconds(750)));
        }

        [Test]
        public async Task DelayAfterNotificationAsync_UsesConfiguredRange_WhenDelayExceedsMinimum()
        {
            FakeDelayExecutor delayExecutor = new();
            EpisodeReminderDelayService service = new(NullLogger<EpisodeReminderDelayService>.Instance, delayExecutor);
            AppSettingsModel appSettings = new()
            {
                EpisodeReminderNotificationDelayMs = 2000
            };

            await service.DelayAfterNotificationAsync(appSettings);

            Assert.That(delayExecutor.Delays, Has.Count.EqualTo(1));
            Assert.That(delayExecutor.Delays[0], Is.InRange(TimeSpan.FromMilliseconds(1000), TimeSpan.FromMilliseconds(2000)));
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

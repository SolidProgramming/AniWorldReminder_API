using AniWorldReminder_API.Interfaces;
using AniWorldReminder_API.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using System.Net;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace AniWorldReminder_API.Tests.Services
{
    public class AdminHttpFailureNotificationHandlerTests
    {
        [Test]
        public async Task SendAsync_SendsAdminTelegramMessage_OnTransientHttpFailureResponse()
        {
            using CurrentDirectoryScope _ = CurrentDirectoryScope.CreateWithSettingsFile(adminChat: "123456");
            FakeTelegramBotService telegramBotService = new();
            using HttpMessageInvoker invoker = CreateInvoker(
                telegramBotService,
                new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)));

            HttpResponseMessage response = await invoker.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "https://example.com/episodes"),
                CancellationToken.None);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
            Assert.That(telegramBotService.Messages, Has.Count.EqualTo(1));
            Assert.That(telegramBotService.Messages[0].ChatId, Is.EqualTo(123456L));
            Assert.That(telegramBotService.Messages[0].Text, Does.Contain("HTTP request failed after all retries"));
            Assert.That(telegramBotService.Messages[0].Text, Does.Contain("GET https://example.com/episodes"));
            Assert.That(telegramBotService.Messages[0].Text, Does.Contain("HTTP 500 InternalServerError"));
        }

        [Test]
        public async Task SendAsync_DoesNotSendAdminTelegramMessage_OnSuccessfulResponse()
        {
            using CurrentDirectoryScope _ = CurrentDirectoryScope.CreateWithSettingsFile(adminChat: "123456");
            FakeTelegramBotService telegramBotService = new();
            using HttpMessageInvoker invoker = CreateInvoker(
                telegramBotService,
                new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));

            HttpResponseMessage response = await invoker.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "https://example.com/episodes"),
                CancellationToken.None);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(telegramBotService.Messages, Is.Empty);
        }

        [Test]
        public async Task SendAsync_SendsAdminTelegramMessage_OnTransientHttpException()
        {
            using CurrentDirectoryScope _ = CurrentDirectoryScope.CreateWithSettingsFile(adminChat: "123456");
            FakeTelegramBotService telegramBotService = new();
            using HttpMessageInvoker invoker = CreateInvoker(
                telegramBotService,
                new StubHttpMessageHandler(_ => throw new HttpRequestException("network down")));

            HttpRequestException exception = Assert.ThrowsAsync<HttpRequestException>(() => invoker.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "https://example.com/episodes"),
                CancellationToken.None))!;

            Assert.That(exception.Message, Is.EqualTo("network down"));
            Assert.That(telegramBotService.Messages, Has.Count.EqualTo(1));
            Assert.That(telegramBotService.Messages[0].Text, Does.Contain("network down"));
        }

        private static HttpMessageInvoker CreateInvoker(ITelegramBotService telegramBotService, HttpMessageHandler innerHandler)
        {
            AdminHttpFailureNotificationHandler handler = new(NullLogger<AdminHttpFailureNotificationHandler>.Instance, telegramBotService)
            {
                InnerHandler = innerHandler
            };

            return new HttpMessageInvoker(handler);
        }

        private sealed record SentTelegramMessage(long ChatId, string Text);

        private sealed class FakeTelegramBotService : ITelegramBotService
        {
            public List<SentTelegramMessage> Messages { get; } = [];

            public Task<bool> Init()
            {
                return Task.FromResult(true);
            }

            public Task SendChatAction(long chatId, ChatAction chatAction)
            {
                return Task.CompletedTask;
            }

            public Task<Message?> SendMessageAsync(long chatId, string text, int? replyId = null, bool showLinkPreview = true, ParseMode parseMode = ParseMode.Html, bool silentMessage = false, ReplyKeyboardMarkup? rkm = null)
            {
                Messages.Add(new SentTelegramMessage(chatId, text));
                return Task.FromResult<Message?>(null);
            }

            public Task<Message?> SendPhotoAsync(long chatId, string photoUrl, string? text = null, ParseMode parseMode = ParseMode.Html, bool silentMessage = false)
            {
                return Task.FromResult<Message?>(null);
            }
        }

        private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.FromResult(responseFactory(request));
            }
        }

        private sealed class CurrentDirectoryScope : IDisposable
        {
            private readonly string originalDirectory;
            private readonly string tempDirectory;

            private CurrentDirectoryScope(string originalDirectory, string tempDirectory)
            {
                this.originalDirectory = originalDirectory;
                this.tempDirectory = tempDirectory;
            }

            public static CurrentDirectoryScope CreateWithSettingsFile(string adminChat)
            {
                string originalDirectory = Directory.GetCurrentDirectory();
                string tempDirectory = Path.Combine(Path.GetTempPath(), $"AniWorldReminder_API.Tests.{Guid.NewGuid():N}");
                Directory.CreateDirectory(tempDirectory);

                string settingsJson =
                    $$"""
                    {
                      "TelegramBot": {
                        "Token": "test-token",
                        "AdminChat": "{{adminChat}}"
                      },
                      "Database": {
                        "IP": "127.0.0.1",
                        "Database": "test",
                        "Username": "test",
                        "Password": "test"
                      },
                      "Proxy": {
                        "URI": "",
                        "Username": "",
                        "Password": ""
                      },
                      "AppSettings": {
                        "AddSwagger": false,
                        "EnableEpisodeReminderJob": true,
                        "EpisodeReminderCron": "*/15 * * * *",
                        "EnableHangfireDashboard": false,
                        "HangfireDashboardPath": "/hangfire",
                        "EpisodeReminderSeriesDelayMs": 1500,
                        "EpisodeReminderNotificationDelayMs": 500
                      },
                      "Jwt": {
                        "Key": "test-key",
                        "Issuer": "test-issuer"
                      },
                      "TMDB": {
                        "AccessToken": "test-token"
                      }
                    }
                    """;

                File.WriteAllText(Path.Combine(tempDirectory, "settings.json"), settingsJson);
                Directory.SetCurrentDirectory(tempDirectory);

                return new CurrentDirectoryScope(originalDirectory, tempDirectory);
            }

            public void Dispose()
            {
                Directory.SetCurrentDirectory(originalDirectory);

                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, true);
                }
            }
        }
    }
}

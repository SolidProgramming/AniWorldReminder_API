using Microsoft.Extensions.Http.Resilience;
using Polly.Timeout;

namespace AniWorldReminder_API.Services
{
    public class AdminHttpFailureNotificationHandler(
        ILogger<AdminHttpFailureNotificationHandler> logger,
        ITelegramBotService telegramBotService) : DelegatingHandler
    {
        private readonly TelegramBotSettingsModel? telegramBotSettings = SettingsHelper.ReadSettings<TelegramBotSettingsModel>();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            try
            {
                HttpResponseMessage response = await base.SendAsync(request, cancellationToken);

                if (IsTransientHttpFailure(response))
                {
                    await NotifyAdminAsync(request, $"HTTP {(int)response.StatusCode} {response.StatusCode}", cancellationToken);
                }

                return response;
            }
            catch (Exception exception) when (IsTransientHttpException(exception))
            {
                await NotifyAdminAsync(request, exception.Message, cancellationToken);
                throw;
            }
        }

        private async Task NotifyAdminAsync(HttpRequestMessage request, string reason, CancellationToken cancellationToken)
        {
            if (!TryGetAdminChatId(out long adminChatId))
                return;

            string requestUri = request.RequestUri?.ToString() ?? "unknown";
            string message = $"{Emoji.ExclamationmarkRed} <b>HTTP request failed after all retries.</b>\n" +
                             $"Client request: <b>{request.Method} {requestUri}</b>\n" +
                             $"Reason: <b>{reason}</b>";

            try
            {
                await telegramBotService.SendMessageAsync(adminChatId, message, silentMessage: true);
                logger.LogWarning(
                    "Sent HTTP failure admin notification for {Method} {RequestUri}. Reason: {Reason}",
                    request.Method,
                    requestUri,
                    reason);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send HTTP failure admin notification for {Method} {RequestUri}", request.Method, requestUri);
            }
        }

        private bool TryGetAdminChatId(out long adminChatId)
        {
            adminChatId = default;

            return telegramBotSettings is not null
                   && !string.IsNullOrWhiteSpace(telegramBotSettings.AdminChat)
                   && long.TryParse(telegramBotSettings.AdminChat, out adminChatId);
        }

        private static bool IsTransientHttpFailure(HttpResponseMessage response)
        {
            int statusCode = (int)response.StatusCode;
            return statusCode == 408 || statusCode == 429 || statusCode >= 500;
        }

        private static bool IsTransientHttpException(Exception exception)
        {
            return exception is HttpRequestException or TimeoutRejectedException;
        }
    }
}

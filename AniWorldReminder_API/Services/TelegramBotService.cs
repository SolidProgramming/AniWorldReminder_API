using System.Text;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace AniWorldReminder_API.Services
{
    public class TelegramBotService(ILogger<TelegramBotService> logger, IDBService dbService) : ITelegramBotService
    {
        private TelegramBotClient? BotClient;

        public async Task<bool> Init()
        {
            TelegramBotSettingsModel? settings = SettingsHelper.ReadSettings<TelegramBotSettingsModel>();

            if (settings is null)
            {
                logger.LogError($"{DateTime.Now} | {ErrorMessage.ReadSettings}");
                return false;
            }

            BotClient = new TelegramBotClient(settings.Token);

            try
            {
                User? botMe = await BotClient.GetMe();

                if (botMe is null)
                {
                    logger.LogError($"{DateTime.Now} | {ErrorMessage.RetrieveBotInfo}");
                    return false;
                }

                BotClient.OnError += HandleError;
                BotClient.OnMessage += HandleMessage;
            }
            catch (Exception ex)
            {
                logger.LogError($"{DateTime.Now} | {ex}");
                return false;
            }

            logger.LogInformation($"{DateTime.Now} | Telegram Bot Service initialized");

            return true;
        }

        private async Task HandleMessage(Message message, UpdateType updateType)
        {
            if (updateType != UpdateType.Message || string.IsNullOrWhiteSpace(message.Text))
                return;

            const int maxMessageAgeInSeconds = 30;

            if ((DateTime.UtcNow - message.Date).TotalSeconds > maxMessageAgeInSeconds)
            {
                logger.LogInformation($"{DateTime.Now} | Received a message older than {maxMessageAgeInSeconds} seconds. Message is ignored.");
                return;
            }

            if (!IsVerifyCommand(message.Text))
                return;

            await HandleVerifyAsync(message);
        }

        private Task HandleError(Exception exception, HandleErrorSource source)
        {
            string errorMessage = exception switch
            {
                ApiRequestException apiRequestException => $"Telegram API Error: [{apiRequestException.ErrorCode}] {apiRequestException.Message}",
                _ => exception.ToString()
            };

            logger.LogError($"{DateTime.Now} | {errorMessage}");
            return Task.CompletedTask;
        }

        private static bool IsVerifyCommand(string text)
        {
            string[] commandParts = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (commandParts.Length == 0)
                return false;

            string command = commandParts[0];

            return command.Equals("/verify", StringComparison.OrdinalIgnoreCase)
                || command.StartsWith("/verify@", StringComparison.OrdinalIgnoreCase);
        }

        private async Task HandleVerifyAsync(Message message)
        {
            string telegramChatId = message.Chat.Id.ToString();
            UserModel user = await dbService.GetUserByTelegramIdAsync(telegramChatId) ?? await dbService.InsertUserAsync(telegramChatId);

            if (user.Verified == VerificationStatus.Verified)
            {
                string alreadyVerifiedMessage = $"{Emoji.ExclamationmarkRed} <b>Du bist schon verifiziert.</b> {Emoji.ExclamationmarkRed}\nBenutzername: <b>{user.Username}</b>";
                await SendMessageAsync(message.Chat.Id, alreadyVerifiedMessage);
                return;
            }

            string token = Helper.GenerateToken(telegramChatId);
            TokenValidationModel tokenValidation = Helper.ValidateToken(token);

            if (!tokenValidation.Validated || tokenValidation.ExpireDate is null)
            {
                string errorMessage = $"{Emoji.ExclamationmarkRed} <b>Verifikations-Code konnte nicht erstellt werden.</b> {Emoji.ExclamationmarkRed}";
                await SendMessageAsync(message.Chat.Id, errorMessage);
                return;
            }

            await dbService.UpdateVerifyTokenAsync(telegramChatId, token);

            StringBuilder sb = new();
            sb.AppendLine($"{Emoji.Confetti} <b>Dein Verifikations-Token:</b> {Emoji.Confetti}\n");
            sb.AppendLine($"{Emoji.Checkmark} Token: <b>{token}</b>\n");
            sb.AppendLine($"{Emoji.AlarmClock} Gültig bis: <b>{tokenValidation.ExpireDate:dd.MM.yyyy HH:mm:ss}</b>\n");
            sb.AppendLine($"{Emoji.Exclamationmark} Nutze diesen Token bei der Registrierung auf der Webseite, damit dein Account mit dieser Telegram-ID verknüpft wird.");

            await SendMessageAsync(message.Chat.Id, sb.ToString());
        }

        public async Task SendChatAction(long chatId, ChatAction chatAction)
        {
            if (BotClient is null)
                return;

            await BotClient.SendChatAction(chatId, chatAction);
        }

        public async Task<Message?> SendMessageAsync(long chatId, string text, int? replyId = null, bool showLinkPreview = true, ParseMode parseMode = ParseMode.Html, bool silentMessage = false, ReplyKeyboardMarkup? rkm = null)
        {
            if (BotClient is null)
                return null;

            try
            {
                return await BotClient.SendMessage(
                    chatId: chatId,
                    text: (text ?? string.Empty).HtmlDecode(),
                    parseMode: parseMode,
                    linkPreviewOptions: new() { IsDisabled = !showLinkPreview },
                    replyParameters: replyId is int messageId ? new ReplyParameters { MessageId = messageId } : null,
                    replyMarkup: rkm,
                    disableNotification: silentMessage);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public async Task<Message?> SendPhotoAsync(long chatId, string photoUrl, string? text = default, ParseMode parseMode = ParseMode.Html, bool silentMessage = false)
        {
            const string base64Prefix = "data:image/png;base64,";

            if (BotClient is null)
                return null;

            try
            {
                if (photoUrl.Trim().StartsWith(base64Prefix))
                {
                    photoUrl = photoUrl.Replace(base64Prefix, "");

                    return await BotClient.SendPhoto(
                               chatId,
                         new InputFileStream(new MemoryStream(Convert.FromBase64String(photoUrl))),
                               caption: (text ?? string.Empty).HtmlDecode(),
                               parseMode: parseMode,
                               disableNotification: silentMessage);
                }

                return await BotClient.SendPhoto(
                           chatId,
                     new InputFileUrl(photoUrl),
                           caption: (text ?? string.Empty).HtmlDecode(),
                           parseMode: parseMode,
                           disableNotification: silentMessage);
            }
            catch (Exception)
            {
                return await SendMessageAsync(chatId, text ?? string.Empty, parseMode: parseMode);
            }
        }
    }
}

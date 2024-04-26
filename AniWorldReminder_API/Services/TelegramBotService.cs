using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace AniWorldReminder_API.Services
{
    public class TelegramBotService(ILogger<TelegramBotService> logger) : ITelegramBotService
    {
        private TelegramBotClient BotClient = default!;

        public async Task<bool> Init()
        {
            TelegramBotSettingsModel? settings = SettingsHelper.ReadSettings<TelegramBotSettingsModel>();

            if (settings is null)
            {
                logger.LogError($"{DateTime.Now} | {ErrorMessage.ReadSettings}");
                return false;
            }

            BotClient = new(settings.Token);

            try
            {
                User? bot_me = await BotClient.GetMeAsync();

                if (bot_me is null)
                {
                    logger.LogError($"{DateTime.Now} | {ErrorMessage.RetrieveBotInfo}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                logger.LogError($"{DateTime.Now} | {ex}");
                return false;

                throw;
            }


            logger.LogInformation($"{DateTime.Now} | Telegram Bot Service initialized");

            return true;
        }
        public async Task SendChatAction(long chatId, ChatAction chatAction)
        {
            await BotClient.SendChatActionAsync(chatId, chatAction);
        }
        public async Task<Message?> SendMessageAsync(long chatId, string text, int? replyId = null, bool showLinkPreview = true, ParseMode parseMode = ParseMode.Html, bool silentMessage = false, ReplyKeyboardMarkup? rkm = null)
        {
            try
            {
                return await BotClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: text,
                    replyToMessageId: replyId,
                    parseMode: parseMode,
                    disableWebPagePreview: !showLinkPreview,
                    replyMarkup: rkm,
                    disableNotification: silentMessage);
            }
            catch (Exception)
            {
                return null;
            }
        }
        public async Task<Message?> SendPhotoAsync(long chatId, string photoUrl, string? text = null, ParseMode parseMode = ParseMode.Html, bool silentMessage = false)
        {
            try
            {
                return await BotClient.SendPhotoAsync(
                               chatId,
                         new InputFileUrl(photoUrl),
                               caption: text,
                               parseMode: parseMode,
                               disableNotification: silentMessage);
            }
            catch (Exception)
            {
                return await SendMessageAsync(chatId, text, parseMode: parseMode);
            }

        }
    }
}

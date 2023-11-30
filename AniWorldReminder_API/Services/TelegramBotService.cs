using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace AniWorldReminder_API.Services
{
    public class TelegramBotService : ITelegramBotService
    {
        private readonly ILogger<TelegramBotService> Logger;
        private TelegramBotClient BotClient = default!;

        public TelegramBotService(ILogger<TelegramBotService> logger)
        {
            Logger = logger;
        }

        public async Task<bool> Init()
        {
            TelegramBotSettingsModel? settings = SettingsHelper.ReadSettings<TelegramBotSettingsModel>();

            if (settings is null)
            {
                Logger.LogError($"{DateTime.Now} | {ErrorMessage.ReadSettings}");
                return false;
            }

            BotClient = new(settings.Token);

            User? bot_me = await BotClient.GetMeAsync();

            if (bot_me is null)
            {
                Logger.LogError($"{DateTime.Now} | {ErrorMessage.RetrieveBotInfo}");
                return false;
            }

            Logger.LogInformation($"{DateTime.Now} | Telegram Bot Service initialized");

            return true;
        }
        public async Task SendChatAction(long chatId, ChatAction chatAction)
        {
            await BotClient.SendChatActionAsync(chatId, chatAction);
        }
        public async Task<Message> SendMessageAsync(long chatId, string text, int? replyId = null, bool showLinkPreview = true, ParseMode parseMode = ParseMode.Html, ReplyKeyboardMarkup? rkm = null)
        {
            return await BotClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: text,
                    replyToMessageId: replyId,
                    parseMode: parseMode,
                    disableWebPagePreview: !showLinkPreview,
                    replyMarkup: rkm);
        }
        public async Task<Message> SendPhotoAsync(long chatId, string photoUrl, string? text = null, ParseMode parseMode = ParseMode.Html)
        {
            return await BotClient.SendPhotoAsync(
                chatId,
          new InputFileUrl(photoUrl),
                caption: text,
                parseMode: parseMode);
        }
    }
}

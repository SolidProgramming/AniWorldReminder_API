﻿using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace AniWorldReminder_API.Interfaces
{
    public interface ITelegramBotService
    {
        Task<bool> Init();
        Task<Message?> SendMessageAsync(long chatId, string text, bool showLinkPreview = true, ParseMode parseMode = ParseMode.Html, bool silentMessage = false, ReplyKeyboardMarkup? rkm = null);
        Task<Message?> SendPhotoAsync(long chatId, string photoUrl, string? text = null, ParseMode parseMode = ParseMode.Html, bool silentMessage = false);
    }
}

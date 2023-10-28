using System.Text.Json.Serialization;

namespace AniWorldReminder_API.Models
{
    public class TelegramBotSettingsModel
    {
        [JsonPropertyName("Token")]
        public string Token { get; set; } = default!;
    }
}

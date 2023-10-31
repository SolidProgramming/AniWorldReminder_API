namespace AniWorldReminder_API.Models
{
    public class VerifyRequestModel
    {
        public string? TelegramChatId { get; set; }
        public string? VerifyToken { get; set; }
    }
}

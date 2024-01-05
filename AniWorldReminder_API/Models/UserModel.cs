namespace AniWorldReminder_API.Models
{
    public class UserModel
    {
        public int Id { get; set; }
        public string? TelegramChatId { get; set; }
        public UserState StateId { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }
        public VerificationStatus Verified { get; set; }
    }
}

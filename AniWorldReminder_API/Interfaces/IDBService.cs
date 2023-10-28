namespace AniWorldReminder_API.Interfaces
{
    public interface IDBService
    {
        Task<bool> InitAsync();
        Task<UserModel?> GetUserAsync(string telegramChatId);
        Task DeleteVerifyTokenAsync(string telegramChatId);
        Task UpdateVerificationStatusAsync(string telegramChatId, VerificationStatus verificationStatus);
    }
}

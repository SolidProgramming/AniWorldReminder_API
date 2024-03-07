namespace AniWorldReminder_API.Interfaces
{
    public interface IAuthService
    {
        Task<UserModel?> Authenticate(string username, string password);
        string? GenerateJSONWebToken(UserModel user);
        Task<string?> GenerateAPIKey(string userId);
        Task<string?> GetAPIKey(string userId);
    }
}

namespace AniWorldReminder_API.Interfaces
{
    public interface IAuthService
    {
        Task<UserModel?> Authenticate(string username, string password);
        string? GenerateJSONWebToken();
    }
}

namespace AniWorldReminder_API.Interfaces
{
    public interface IAuthService
    {
        Task<UserModel?> Connect(UserModel login);
    }
}

namespace AniWorldReminder_API.Models
{
    public class AuthResponseModel(string token)
    {
        public string Token { get; init; } = token;
    }
}

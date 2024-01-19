namespace AniWorldReminder_API.Models
{
    public class JwtResponseModel(string token)
    {
        public string Token { get; init; } = token;
    }
}

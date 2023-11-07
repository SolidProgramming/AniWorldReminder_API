namespace AniWorldReminder_API.Models
{
    public class VerifyRequestModel
    {
        public string? Password { get; set; }
        public string? Username { get; set; }
        public string? VerifyToken { get; set; }
    }
}

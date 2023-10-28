namespace AniWorldReminder_API.Models
{
    public class TokenValidationModel
    {
        public bool Validated { get { return Errors.Count == 0; } }
        public readonly List<TokenValidationStatus> Errors = new();
    }
}

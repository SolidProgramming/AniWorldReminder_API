using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace AniWorldReminder_API.Services
{
    public class AuthService : IAuthService
    {
        private readonly IDBService DBService;
        private readonly ILogger<AuthService> Logger;

        public AuthService(ILogger<AuthService> logger, IDBService dbService)
        {
            Logger = logger;
            DBService = dbService;

            Logger.LogInformation($"{DateTime.Now} Auth Service initialized");
        }

        public async Task<UserModel?> Authenticate(string username, string password)
        {
            UserModel? user = await DBService.GetAuthUserAsync(username);

            if (user is null || string.IsNullOrEmpty(user.Password))
                return null;

            bool verify = SecretHasher.Verify(password, user.Password);

            if (!verify)
                return null;

            return user;
        }

        public string? GenerateJSONWebToken(UserModel user)
        {
            JwtSettingsModel? jwtSettings = SettingsHelper.ReadSettings<JwtSettingsModel>();

            if (jwtSettings is null || string.IsNullOrEmpty(jwtSettings.Key) || string.IsNullOrEmpty(jwtSettings.Issuer))
                return null;

            SymmetricSecurityKey? securityKey = new(Encoding.UTF8.GetBytes(jwtSettings.Key));
            SigningCredentials? credentials = new(securityKey, SecurityAlgorithms.HmacSha256);

            Claim[]? claims = [
                new Claim(JwtRegisteredClaimNames.Sub, user.Username)
            ];

            JwtSecurityToken? token = new(jwtSettings.Issuer, jwtSettings.Issuer, claims, expires: DateTime.Now.AddDays(1), signingCredentials: credentials);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}

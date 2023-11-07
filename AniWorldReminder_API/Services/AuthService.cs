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

            if(!verify)
                return null;

            return user;
        }
    }
}

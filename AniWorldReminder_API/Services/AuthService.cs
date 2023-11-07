using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace AniWorldReminder_API.Services
{
    public class AuthService : IAuthService
    {
        public async Task<UserModel?> Connect(UserModel login)
        {
            UserModel? user = null;
            return await Task.FromResult(user);
        }
    }
}

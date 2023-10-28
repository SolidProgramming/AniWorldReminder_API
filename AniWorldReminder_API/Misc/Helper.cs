using AniWorldReminder_API.Models;
using System.Text;

namespace AniWorldReminder_API.Misc
{
    public static class Helper
    {
        public static string GetString(byte[] reason) => Encoding.ASCII.GetString(reason);
        public static byte[] GetBytes(string reason) => Encoding.ASCII.GetBytes(reason);
        public static TokenValidationModel ValidateToken(UserModel user)
        {
            TokenValidationModel result = new();

            if (string.IsNullOrEmpty(user.TelegramChatId))
            {
                result.Errors.Add(TokenValidationStatus.TelegramChatIdInvalid);
                return result;
            }

            if (string.IsNullOrEmpty(user.VerifyToken))
            {
                result.Errors.Add(TokenValidationStatus.VerifyTokenInvalid);
                return result;
            }

            if(!IsBase64String(user.VerifyToken))
            {
                result.Errors.Add(TokenValidationStatus.VerifyTokenInvalid);
                return result;
            }

            byte[] data = Convert.FromBase64String(user.VerifyToken);
            byte[] _time = data.Take(8).ToArray();
            byte[] _key = data.Skip(8).Take(9).ToArray();
            byte[] _Id = data.Skip(17).ToArray();

            DateTime when = DateTime.FromBinary(BitConverter.ToInt64(_time, 0));
            if (when < DateTime.UtcNow.AddHours(-24))
            {
                result.Errors.Add(TokenValidationStatus.TokenExpired);
            }

            if ((GetString(_key) != user.TelegramChatId) || (user.Id.ToString() != GetString(_Id)))
            {
                result.Errors.Add(TokenValidationStatus.TelegramChatIdInvalid);
            }

            return result;
        }
        public static string GenerateToken(UserModel user)
        {
            byte[] _time = BitConverter.GetBytes(DateTime.UtcNow.ToBinary());
            byte[] _key = GetBytes(user.TelegramChatId);
            byte[] _Id = GetBytes(user.Id.ToString());
            byte[] data = new byte[_time.Length + _key.Length + _Id.Length];

            Buffer.BlockCopy(_time, 0, data, 0, _time.Length);
            Buffer.BlockCopy(_key, 0, data, _time.Length, _key.Length);
            Buffer.BlockCopy(_Id, 0, data, _time.Length + _key.Length, _Id.Length);

            return Convert.ToBase64String(data.ToArray());
        }

        public static bool IsBase64String(string base64)
        {
            Span<byte> buffer = new Span<byte>(new byte[base64.Length]);
            return Convert.TryFromBase64String(base64, buffer, out int bytesParsed);
        }
    }
}

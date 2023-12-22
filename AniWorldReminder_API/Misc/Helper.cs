using AniWorldReminder_API.Models;
using System.Linq;
using System.Text;

namespace AniWorldReminder_API.Misc
{
    public static class Helper
    {
        public static string GetString(byte[] reason) => Encoding.ASCII.GetString(reason);
        public static byte[] GetBytes(string reason) => Encoding.ASCII.GetBytes(reason);
        public static TokenValidationModel ValidateToken(string token)
        {
            TokenValidationModel result = new();

            if (string.IsNullOrEmpty(token))
            {
                result.Errors.Add(TokenValidationStatus.VerifyTokenInvalid);
                return result;
            }

            if(!IsBase64String(token))
            {
                result.Errors.Add(TokenValidationStatus.VerifyTokenInvalid);
                return result;
            }

            byte[] data = Convert.FromBase64String(token);
            byte[] _time = data.Take(8).ToArray();
            byte[] _key = data.Skip(8).TakeLast(data.Length - 8).ToArray();

            DateTime when = DateTime.FromBinary(BitConverter.ToInt64(_time, 0));
            if (when < DateTime.Now)
            {
                result.Errors.Add(TokenValidationStatus.TokenExpired);
            }

            result.TelegramChatId = GetString(_key);

            return result;
        }

        public static bool IsBase64String(string base64)
        {
            Span<byte> buffer = new Span<byte>(new byte[base64.Length]);
            return Convert.TryFromBase64String(base64, buffer, out int bytesParsed);
        }
    }
}

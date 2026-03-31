using System.Text;

namespace AniWorldReminder_API.Misc
{
    public static class Helper
    {
        private const int VerifyTokenLifetimeInMinutes = 10;

        public static string GetString(byte[] reason) => Encoding.ASCII.GetString(reason);
        public static byte[] GetBytes(string reason) => Encoding.ASCII.GetBytes(reason);
        public static string GenerateToken(string telegramChatId)
        {
            byte[] time = BitConverter.GetBytes(DateTime.Now.AddMinutes(VerifyTokenLifetimeInMinutes).ToBinary());
            byte[] key = GetBytes(telegramChatId);
            byte[] data = new byte[time.Length + key.Length];

            Buffer.BlockCopy(time, 0, data, 0, time.Length);
            Buffer.BlockCopy(key, 0, data, time.Length, key.Length);

            return Convert.ToBase64String(data);
        }

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
            result.ExpireDate = when;
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

using System.Text.RegularExpressions;
using System.Web;

namespace AniWorldReminder_API.Misc
{
    public static class Extensions
    {       
        public static string StripHtmlTags(this string text)
        {
            return Regex.Replace(text, "<.*?>", string.Empty); //|&.*?;
        }
        public static string HtmlDecode(this string text)
        {
            return HttpUtility.HtmlDecode(text);
        }
        public static string SearchSanitize(this string text)
        {
            return text.Replace(' ', '+')
                .Replace("'", "");
        }
        public static string UrlSanitize(this string text)
        {
            return text.Replace(' ', '-')
                .Replace(":", "")
                .Replace("~", "")
                .Replace("'", "")
                .Replace(",", "")
                .Replace("’", "");
        }        
        public static bool HasItems<T>(this IEnumerable<T> source) => source != null && source.Any();
        public static async Task<(bool success, string? ipv4)> GetIPv4(this HttpClient httpClient)
        {
            string result = await httpClient.GetStringAsync("https://api.ipify.org/");

            return (!string.IsNullOrEmpty(result), result);
        }
    }
}

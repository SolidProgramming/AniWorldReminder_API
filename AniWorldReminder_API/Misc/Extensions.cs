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
            return text
                .Replace("+", "%2B")
                .Replace(' ', '+')
                .Replace("'", "");
        }
        public static string UrlSanitize(this string text)
        {
            return text.Replace(' ', '-')
               .Replace(":", "")
               .Replace("~", "")
               .Replace("'", "")
               .Replace(",", "")
               .Replace("’", "")
               .Replace("+", "")
               .Replace(".", "")
               .Replace("!", "")
               .Replace("--", "-")
               .Replace("ä", "ae")
               .Replace("ö", "oe")
               .Replace("ü", "ue");
        }
        public static bool HasItems<T>(this IEnumerable<T> source) => source != null && source.Any();
        public static async Task<(bool success, string? ipv4)> GetIPv4(this HttpClient httpClient)
        {
            string result = await httpClient.GetStringAsync("https://api.ipify.org/");

            return (!string.IsNullOrEmpty(result), result);
        }

        private static Dictionary<Language, string> VOELanguageKeyCollection = new()
        {
            { Language.GerDub, "1"},
            { Language.GerSub, "3"},
            { Language.EngDub, "2"},
            { Language.EngSub, "4"},
        };

        public static string? ToVOELanguageKey(this Language language)
        {
            if (VOELanguageKeyCollection.ContainsKey(language))
            {
                return VOELanguageKeyCollection[language];
            }

            return null;
        }

        public static string? GetClaimUsername(this HttpContext httpContext)
        {
            return httpContext.User.Claims
                          .Where(_ => _.Type == "Username")
                              .Select(_ => _.Value)
                                  .SingleOrDefault();
        }
    }
}

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
        public static string UrlEncode(this string text)
        {
            return HttpUtility.UrlEncode(text);
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

        private static Dictionary<CustomClaimType, string> CustomClaimTypeCollection = new()
        {
            { CustomClaimType.UserId, "UserId" },
            { CustomClaimType.Username, "Username" }
        };

        public static string? ToVOELanguageKey(this Language language)
        {
            if (VOELanguageKeyCollection.TryGetValue(language, out string? value))
            {
                return value;
            }

            return default;
        }

        public static string? ToCustomClaimTypeValue(this CustomClaimType customClaimType)
        {
            if (CustomClaimTypeCollection.TryGetValue(customClaimType, out string? value))
            {
                return value;
            }

            return default;
        }

        public static string? GetClaim(this HttpContext httpContext, CustomClaimType claimType)
        {
            return httpContext.User.Claims
                          .Where(_ => _.Type == claimType.ToCustomClaimTypeValue())
                              .Select(_ => _.Value)
                                  .SingleOrDefault();
        }

        private static Dictionary<string, StreamingPortal> StreamingPortalCollection = new()
        {
            { "AniWorld", StreamingPortal.AniWorld },
            { "STO", StreamingPortal.STO },
            { "MegaKino", StreamingPortal.MegaKino },
        };

        public static StreamingPortal ToStreamingPortal(this string streamingPortal)
        {
            if (StreamingPortalCollection.TryGetValue(streamingPortal, out StreamingPortal value))
            {
                return value;
            }

            return StreamingPortal.Undefined;
        }
    }
}

using System;

namespace StrmAssistant
{
    public static class CommonUtility
    {
        public static bool IsValidHttpUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;

            if (Uri.TryCreate(url, UriKind.Absolute, out var uriResult))
            {
                return uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps;
            }

            return false;
        }
    }
}

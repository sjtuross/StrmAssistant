using Microsoft.International.Converters.TraditionalChineseToSimplifiedConverter;
using System.Text.RegularExpressions;

namespace StrmAssistant
{
    public static class LanguageUtility
    {
        public static bool IsChinese(string input) => new Regex(@"[\u4E00-\u9FFF]").IsMatch(input);

        public static bool IsJapanese(string input) => new Regex(@"[\u3040-\u309F\u30A0-\u30FF]").IsMatch(input);

        public static bool IsDefaultChineseEpisodeName(string name) => new Regex(@"第\s*\d+\s*集").IsMatch(name);

        public static string ConvertTraditionalToSimplified(string input)
        {
            return ChineseConverter.Convert(input, ChineseConversionDirection.TraditionalToSimplified);
        }
    }
}

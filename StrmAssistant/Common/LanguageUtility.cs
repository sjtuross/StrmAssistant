using Microsoft.International.Converters.TraditionalChineseToSimplifiedConverter;
using System.Text.RegularExpressions;
using TinyPinyin;

namespace StrmAssistant
{
    public static class LanguageUtility
    {
        public static bool IsChinese(string input) =>
            !string.IsNullOrEmpty(input) && new Regex(@"[\u4E00-\u9FFF]").IsMatch(input);

        public static bool IsJapanese(string input) =>
            !string.IsNullOrEmpty(input) && new Regex(@"[\u3040-\u30FF]").IsMatch(input);

        public static bool IsKorean(string input) =>
            !string.IsNullOrEmpty(input) && new Regex(@"[\uAC00-\uD7A3]").IsMatch(input);

        public static bool IsDefaultChineseEpisodeName(string input) =>
            !string.IsNullOrEmpty(input) && new Regex(@"第\s*\d+\s*集").IsMatch(input);

        public static bool IsDefaultJapaneseEpisodeName(string input) =>
            !string.IsNullOrEmpty(input) && new Regex(@"第\s*\d+\s*話").IsMatch(input);

        public static string ConvertTraditionalToSimplified(string input)
        {
            return ChineseConverter.Convert(input, ChineseConversionDirection.TraditionalToSimplified);
        }

        public static string GetLanguageByTitle(string input)
        {
            if (string.IsNullOrEmpty(input)) return null;

            return IsChinese(input) ? "zh" : IsJapanese(input) ? "jp" : IsKorean(input) ? "ko" : "en";
        }

        public static string ConvertToPinyinInitials(string input)
        {
            return PinyinHelper.GetPinyinInitials(input);
        }

        public static string RemoveDefaultCollectionName(string input)
        {
            return Regex.Replace(input, "（系列）$", "").Trim();
        }
    }
}

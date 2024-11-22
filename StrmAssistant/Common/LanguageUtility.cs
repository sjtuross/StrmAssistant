using Microsoft.International.Converters.TraditionalChineseToSimplifiedConverter;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TinyPinyin;

namespace StrmAssistant.Common
{
    public static class LanguageUtility
    {
        private static readonly Regex ChineseRegex = new Regex(@"[\u4E00-\u9FFF]", RegexOptions.Compiled);
        private static readonly Regex JapaneseRegex = new Regex(@"[\u3040-\u30FF]", RegexOptions.Compiled);
        private static readonly Regex KoreanRegex = new Regex(@"[\uAC00-\uD7A3]", RegexOptions.Compiled);
        private static readonly Regex DefaultChineseEpisodeNameRegex = new Regex(@"第\s*\d+\s*集", RegexOptions.Compiled);
        private static readonly Regex DefaultJapaneseEpisodeNameRegex = new Regex(@"第\s*\d+\s*話", RegexOptions.Compiled);
        private static readonly Regex DefaultChineseCollectionNameRegex = new Regex(@"（系列）$", RegexOptions.Compiled);
        private static readonly Regex CleanPersonNameRegex = new Regex(@"\s+", RegexOptions.Compiled);

        public static bool IsChinese(string input) => !string.IsNullOrEmpty(input) && ChineseRegex.IsMatch(input);

        public static bool IsJapanese(string input) => !string.IsNullOrEmpty(input) && JapaneseRegex.IsMatch(input);

        public static bool IsKorean(string input) => !string.IsNullOrEmpty(input) && KoreanRegex.IsMatch(input);

        public static bool IsDefaultChineseEpisodeName(string input) =>
            !string.IsNullOrEmpty(input) && DefaultChineseEpisodeNameRegex.IsMatch(input);

        public static bool IsDefaultJapaneseEpisodeName(string input) =>
            !string.IsNullOrEmpty(input) && DefaultJapaneseEpisodeNameRegex.IsMatch(input);

        public static string ConvertTraditionalToSimplified(string input)
        {
            return ChineseConverter.Convert(input, ChineseConversionDirection.TraditionalToSimplified);
        }

        public static string GetLanguageByTitle(string input)
        {
            if (string.IsNullOrEmpty(input)) return null;

            return IsChinese(input) ? "zh" : IsJapanese(input) ? "jp" : IsKorean(input) ? "ko" : "en";
        }

        //https://github.com/hstarorg/TinyPinyin.Net/issues/5
        //can't use this library directly because it doesn't provide netstandard2.0 dll
        public static string ConvertToPinyinInitials(string input, string separator = "")
        {
            var result = PinyinHelper.GetPinyin(input, "|");

            return string.Join(separator,
                result.Split('|')
                    .Select(x => !string.IsNullOrWhiteSpace(x) && x.Length > 0 ? x.Substring(0, 1) : x)
                    .ToArray());
        }

        public static string RemoveDefaultCollectionName(string input)
        {
            return string.IsNullOrEmpty(input) ? input : DefaultChineseCollectionNameRegex.Replace(input, "").Trim();
        }

        public static string CleanPersonName(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            if (IsChinese(input) || IsJapanese(input) || IsKorean(input))
            {
                return CleanPersonNameRegex.Replace(input, "");
            }

            return input.Trim();
        }

        public static List<string> GetFallbackLanguages()
        {
            var currentFallbackLanguages = Plugin.Instance.MetadataEnhanceStore.GetOptions().FallbackLanguages
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();

            return currentFallbackLanguages;
        }
    }
}

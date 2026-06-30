namespace HPD.Extract.Pdf
{
    internal static class PdfGarbledTextDetector
    {
        public static bool IsLikelyGarbled(string text)
        {
            var (letters, vowels) = CountLettersAndVowels(text);
            return letters >= 10 && vowels * 10 < letters;
        }

        public static float ScorePage(IReadOnlyList<PdfTextItem> items)
        {
            var totalLetters = 0;
            var totalVowels = 0;

            for (var i = 0; i < items.Count; i++)
            {
                if (items[i].Font?.LooksCorrupt == true)
                {
                    totalLetters += Math.Max(10, items[i].Text.Length);
                    continue;
                }

                var (letters, vowels) = CountLettersAndVowels(items[i].Text);
                totalLetters += letters;
                totalVowels += vowels;
            }

            if (totalLetters < 30)
            {
                return 0;
            }

            var vowelRatio = (float)totalVowels / totalLetters;
            return Math.Clamp((0.30f - vowelRatio) / 0.30f, 0, 1);
        }

        private static (int Letters, int Vowels) CountLettersAndVowels(string text)
        {
            var letters = 0;
            var vowels = 0;

            foreach (var ch in text)
            {
                if (!char.IsAsciiLetter(ch))
                {
                    continue;
                }

                letters++;
                if (char.ToLowerInvariant(ch) is 'a' or 'e' or 'i' or 'o' or 'u')
                {
                    vowels++;
                }
            }

            return (letters, vowels);
        }
    }
}

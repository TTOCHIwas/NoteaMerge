using System;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

public static class MarkdownParser
{
    public static ObservableCollection<Inline> Parse(string content, double fontSize = 14)
    {
        var inlines = new ObservableCollection<Inline>();
        var font = new FontFamily(new Uri("pack://application:,,,/"), "./Resources/Fonts/#Pretendard Variable");

        if (string.IsNullOrEmpty(content))
            return inlines;

        var pattern = @"(\*\*(.*?)\*\*|\*(.*?)\*)";
        var matches = Regex.Matches(content, pattern);

        int lastIndex = 0;

        foreach (Match match in matches)
        {
            if (match.Index > lastIndex)
            {
                string plain = content.Substring(lastIndex, match.Index - lastIndex);
                inlines.Add(new Run(plain) { FontFamily = font, FontSize = fontSize });
            }

            if (match.Value.StartsWith("**"))
            {
                string boldText = match.Groups[2].Value;
                var boldRun = new Run(boldText) { FontFamily = font, FontSize = fontSize };
                inlines.Add(new Bold(boldRun) { FontFamily = font });
            }
            else if (match.Value.StartsWith("*"))
            {
                string italicText = match.Groups[3].Value;
                var italicRun = new Run(italicText) { FontFamily = font, FontSize = fontSize };
                inlines.Add(new Italic(italicRun) { FontFamily = font });
            }

            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < content.Length)
        {
            inlines.Add(new Run(content.Substring(lastIndex)) { FontFamily = font, FontSize = fontSize });
        }

        return inlines;
    }
}
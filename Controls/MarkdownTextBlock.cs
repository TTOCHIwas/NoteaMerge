using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace Notea.Controls
{
    public class MarkdownTextBlock : TextBlock
    {
        public static readonly DependencyProperty InlinesSourceProperty =
            DependencyProperty.Register(
                "InlinesSource",
                typeof(ObservableCollection<Inline>),
                typeof(MarkdownTextBlock),
                new PropertyMetadata(null, OnInlinesSourceChanged));

        public ObservableCollection<Inline> InlinesSource
        {
            get { return (ObservableCollection<Inline>)GetValue(InlinesSourceProperty); }
            set { SetValue(InlinesSourceProperty, value); }
        }

        public static readonly DependencyProperty ContentProperty =
            DependencyProperty.Register(
                "Content",
                typeof(string),
                typeof(MarkdownTextBlock),
                new PropertyMetadata(string.Empty, OnContentChanged));

        public string Content
        {
            get { return (string)GetValue(ContentProperty); }
            set { SetValue(ContentProperty, value); }
        }

        private static void OnInlinesSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var textBlock = d as MarkdownTextBlock;
            if (textBlock == null) return;

            textBlock.Inlines.Clear();

            if (e.NewValue is ObservableCollection<Inline> inlines)
            {
                foreach (var inline in inlines)
                {
                    var cloned = CloneInline(inline);
                    textBlock.Inlines.Add(cloned);
                }
            }

            // Inlines가 비어있으면 Content를 표시
            if (textBlock.Inlines.Count == 0 && !string.IsNullOrEmpty(textBlock.Content))
            {
                textBlock.Text = textBlock.Content;
            }
        }

        private static void OnContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var textBlock = d as MarkdownTextBlock;
            if (textBlock == null) return;

            // Inlines가 비어있고 Content가 있으면 임시로 표시
            if (textBlock.Inlines.Count == 0 && !string.IsNullOrEmpty(e.NewValue as string))
            {
                textBlock.Text = e.NewValue as string;
            }
        }

        private static Inline CloneInline(Inline inline)
        {
            if (inline is Run run)
            {
                var newRun = new Run(run.Text)
                {
                    FontFamily = run.FontFamily,
                    FontSize = run.FontSize > 0 ? run.FontSize : 14,
                    FontWeight = run.FontWeight,
                    FontStyle = run.FontStyle,
                    TextDecorations = run.TextDecorations,
                    Foreground = run.Foreground
                };

                return newRun;
            }
            else if (inline is Bold bold && bold.Inlines.FirstInline is Run boldRun)
            {
                var newRun = new Run(boldRun.Text)
                {
                    FontFamily = boldRun.FontFamily,
                    FontSize = boldRun.FontSize > 0 ? boldRun.FontSize : 14,
                    FontWeight = FontWeights.Bold,
                    FontStyle = boldRun.FontStyle,
                    Foreground = boldRun.Foreground
                };
                return newRun;
            }
            else if (inline is Italic italic && italic.Inlines.FirstInline is Run italicRun)
            {
                var newRun = new Run(italicRun.Text)
                {
                    FontFamily = italicRun.FontFamily,
                    FontSize = italicRun.FontSize > 0 ? italicRun.FontSize : 14,
                    FontWeight = italicRun.FontWeight,
                    FontStyle = FontStyles.Italic,
                    Foreground = italicRun.Foreground
                };
                return newRun;
            }

            // 기본값으로 원본 반환 (복제 실패 시)
            return inline;
        }
    }
}
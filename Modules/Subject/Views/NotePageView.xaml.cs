// NotePageView.xaml.cs
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Notea.Modules.Subject.ViewModels;

namespace Notea.Modules.Subject.Views
{
    /// <summary>
    /// NotePage.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class NotePageView : UserControl
    {
        public NotePageView()
        {
            InitializeComponent();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is NotePageViewModel vm)
            {
                vm.SearchHighlightRequested += OnSearchHighlightRequested;
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is NotePageViewModel vm)
            {
                vm.SearchHighlightRequested -= OnSearchHighlightRequested;
                vm.SaveChanges();
            }
        }

        private void OnSearchHighlightRequested(object sender, SearchHighlightEventArgs e)
        {
            noteEditor?.HighlightSearchResult(e.LineIndex, e.StartIndex, e.Length);
        }

        private T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T target)
                    return target;

                var result = FindVisualChild<T>(child);
                if (result != null)
                    return result;
            }
            return null;
        }
    }
}
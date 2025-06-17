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

                try
                {
                    string subjectName = GetCurrentSubjectName(vm);
                    if (!string.IsNullOrEmpty(subjectName))
                    {
                        var dbHelper = Notea.Modules.Common.Helpers.DatabaseHelper.Instance;
                        dbHelper.StartSubjectFocusSession(subjectName);

                        System.Diagnostics.Debug.WriteLine($"[페이지] 과목 포커스 세션 시작: {subjectName}");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[페이지 오류] 과목 포커스 세션 시작 실패: {ex.Message}");
                }
            }
            
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is NotePageViewModel vm)
            {
                vm.SearchHighlightRequested -= OnSearchHighlightRequested;
                vm.SaveChanges();

                try
                {
                    string subjectName = GetCurrentSubjectName(vm);
                    if (!string.IsNullOrEmpty(subjectName))
                    {
                        var dbHelper = Notea.Modules.Common.Helpers.DatabaseHelper.Instance;
                        dbHelper.EndSubjectFocusSession(subjectName);

                        System.Diagnostics.Debug.WriteLine($"[페이지] 과목 포커스 세션 종료: {subjectName}");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[페이지 오류] 과목 포커스 세션 종료 실패: {ex.Message}");
                }
            }
        }

        private string GetCurrentSubjectName(NotePageViewModel vm)
        {
            try
            {
                // SubjectTitle 속성이나 SubjectId를 통해 과목명 조회
                if (!string.IsNullOrEmpty(vm.SubjectTitle))
                {
                    return vm.SubjectTitle;
                }

                // 또는 EditorViewModel의 SubjectId를 통해 조회
                if (vm.EditorViewModel?.SubjectId > 0)
                {
                    string query = $"SELECT title FROM subject WHERE subJectId = {vm.EditorViewModel.SubjectId}";
                    var result = Notea.Helpers.DatabaseHelper.ExecuteSelect(query);

                    if (result.Rows.Count > 0)
                    {
                        return result.Rows[0]["title"].ToString();
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DB 오류] 과목명 조회 실패: {ex.Message}");
                return null;
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
using System;
using System.Windows;
using System.Windows.Controls;
using Notea.Modules.Subject.ViewModels;

namespace Notea.Modules.Subject.Views
{
    /// <summary>
    /// NotePageBodyView.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class NotePageBodyView : UserControl
    {
        public NotePageBodyView()
        {
            InitializeComponent();
        }

        // 올바른 WPF 이벤트 핸들러 시그니처
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (DataContext is NotePageViewModel vm)
                {
                    // SearchHighlightRequested 이벤트 구독
                    vm.SearchHighlightRequested += OnSearchHighlightRequested;

                    // 과목 포커스 세션 시작
                    string subjectName = GetCurrentSubjectName(vm);
                    if (!string.IsNullOrEmpty(subjectName))
                    {
                        var dbHelper = Notea.Modules.Common.Helpers.DatabaseHelper.Instance;
                        dbHelper.StartSubjectFocusSession(subjectName);

                        System.Diagnostics.Debug.WriteLine($"[NotePageBodyView] 과목 포커스 세션 시작: {subjectName}");
                    }

                    System.Diagnostics.Debug.WriteLine("[NotePageBodyView] 노트 편집기 로드 완료");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NotePageBodyView] 로드 오류: {ex.Message}");
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (DataContext is NotePageViewModel vm)
                {
                    // SearchHighlightRequested 이벤트 구독 해제
                    vm.SearchHighlightRequested -= OnSearchHighlightRequested;

                    // 변경사항 저장
                    vm.SaveChanges();

                    // 과목 포커스 세션 종료
                    string subjectName = GetCurrentSubjectName(vm);
                    if (!string.IsNullOrEmpty(subjectName))
                    {
                        var dbHelper = Notea.Modules.Common.Helpers.DatabaseHelper.Instance;
                        dbHelper.EndSubjectFocusSession(subjectName);

                        System.Diagnostics.Debug.WriteLine($"[NotePageBodyView] 과목 포커스 세션 종료: {subjectName}");
                    }

                    System.Diagnostics.Debug.WriteLine("[NotePageBodyView] 노트 편집기 언로드 및 저장 완료");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NotePageBodyView] 언로드 오류: {ex.Message}");
            }
        }

        // SearchHighlightRequested 이벤트 핸들러 (올바른 시그니처)
        private void OnSearchHighlightRequested(object sender, SearchHighlightEventArgs e)
        {
            try
            {
                // NoteEditorView에서 해당 라인으로 스크롤하고 하이라이트
                noteEditor.HighlightSearchResult(e.LineIndex, e.StartIndex, e.Length);
                System.Diagnostics.Debug.WriteLine($"[NotePageBodyView] 검색 하이라이트 요청 처리: Line {e.LineIndex}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NotePageBodyView] 검색 하이라이트 오류: {ex.Message}");
            }
        }

        // 현재 과목명 조회 헬퍼 메서드 (기존 NotePageView에서 이동)
        private string GetCurrentSubjectName(NotePageViewModel vm)
        {
            try
            {
                if (vm?.EditorViewModel?.SubjectId > 0)
                {
                    // ✅ 올바른 테이블명과 컬럼명 사용
                    return Notea.Modules.Subject.Models.NoteRepository.GetSubjectNameById(vm.EditorViewModel.SubjectId);
                }
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DB 오류] 과목명 조회 실패: {ex.Message}");
                return null;
            }
        }

    }
}
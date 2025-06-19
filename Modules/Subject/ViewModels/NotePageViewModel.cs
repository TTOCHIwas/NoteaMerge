using Notea.Modules.Subject.Models;
using Notea.ViewModels;
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

namespace Notea.Modules.Subject.ViewModels
{
    public class NotePageViewModel : INotifyPropertyChanged
    {
        private NoteEditorViewModel _editorViewModel;
        private SearchViewModel _searchViewModel;
        private string _subjectTitle;
        public ICommand NavigateBackCommand { get; }

        public NoteEditorViewModel EditorViewModel
        {
            get => _editorViewModel;
            set
            {
                _editorViewModel = value;
                OnPropertyChanged(nameof(EditorViewModel));
            }
        }

        public SearchViewModel SearchViewModel
        {
            get => _searchViewModel;
            set
            {
                _searchViewModel = value;
                OnPropertyChanged(nameof(SearchViewModel));
            }
        }

        public string SubjectTitle
        {
            get => _subjectTitle;
            set
            {
                _subjectTitle = value;
                OnPropertyChanged(nameof(SubjectTitle));
            }
        }

        public NotePageViewModel()
        {
            SubjectTitle = "";
            NavigateBackCommand = new Notea.ViewModels.RelayCommand(NavigateBack);
        }

        private void NavigateBack()
        {
            try
            {
                // 변경사항 저장
                SaveChanges();

                // MainViewModel의 뒤로가기 명령 실행
                var mainWindow = Application.Current.MainWindow;
                if (mainWindow?.DataContext is MainViewModel mainViewModel)
                {
                    if (mainViewModel.NavigateBackToSubjectListCommand.CanExecute(null))
                    {
                        mainViewModel.NavigateBackToSubjectListCommand.Execute(null);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NotePageViewModel] 뒤로가기 오류: {ex.Message}");
            }
        }

        private void LoadNote(int subjectId)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[NotePageViewModel] LoadNote 시작 - SubjectId: {subjectId}");

                // ✅ 새로운 방식: 평면적 DisplayOrder 기반 로딩만 사용
                var noteData = NoteRepository.LoadNotesBySubjectWithHierarchy(subjectId);
                System.Diagnostics.Debug.WriteLine($"[NotePageViewModel] 로드 결과 - 카테고리 수: {noteData?.Count ?? 0}");

                // 로딩된 데이터 상세 정보 출력 (디버깅용)
                if (noteData != null && noteData.Count > 0)
                {
                    foreach (var category in noteData)
                    {
                        System.Diagnostics.Debug.WriteLine($"[NotePageViewModel] 카테고리: '{category.Title}' (ID: {category.CategoryId}), 라인 수: {category.Lines?.Count ?? 0}");

                        if (category.Lines != null && category.Lines.Count > 0)
                        {
                            // 처음 5개 라인만 출력 (너무 많으면 로그가 길어짐)
                            var linesToShow = category.Lines.Take(5);
                            foreach (var line in linesToShow)
                            {
                                var preview = line.Content?.Length > 50 ?
                                    line.Content.Substring(0, 50) + "..." :
                                    line.Content ?? "";
                                System.Diagnostics.Debug.WriteLine($"  - 라인 (DisplayOrder: {line.DisplayOrder}): '{preview}'");
                            }

                            if (category.Lines.Count > 5)
                            {
                                System.Diagnostics.Debug.WriteLine($"  - ... 외 {category.Lines.Count - 5}개 라인");
                            }
                        }
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[NotePageViewModel] SubjectId {subjectId}에 대한 필기 데이터 없음");
                }

                // EditorViewModel 생성
                System.Diagnostics.Debug.WriteLine($"[NotePageViewModel] NoteEditorViewModel 생성 중...");
                EditorViewModel = new NoteEditorViewModel(noteData);

                // ✅ SubjectId 설정
                if (EditorViewModel != null)
                {
                    EditorViewModel.SetSubjectId(subjectId);
                    System.Diagnostics.Debug.WriteLine($"[NotePageViewModel] EditorViewModel SubjectId 설정 완료: {subjectId}");
                }

                // ✅ 최종 검증 로그
                if (EditorViewModel != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[NotePageViewModel] EditorViewModel 검증:");
                    System.Diagnostics.Debug.WriteLine($"  - SubjectId: {EditorViewModel.SubjectId}");
                    System.Diagnostics.Debug.WriteLine($"  - Lines.Count: {EditorViewModel.Lines?.Count ?? 0}");

                    if (EditorViewModel.Lines != null && EditorViewModel.Lines.Count > 0)
                    {
                        var firstLine = EditorViewModel.Lines[0];
                        System.Diagnostics.Debug.WriteLine($"  - 첫 번째 라인: SubjectId={firstLine.SubjectId}, Content='{firstLine.Content}', CategoryId={firstLine.CategoryId}");
                    }
                }

                // SearchViewModel 초기화
                SearchViewModel = new SearchViewModel(EditorViewModel);
                SearchViewModel.SearchHighlightRequested += OnSearchHighlightRequested;

                System.Diagnostics.Debug.WriteLine($"[NotePageViewModel] LoadNote 완료");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NotePageViewModel] LoadNote 오류: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[NotePageViewModel] 스택 트레이스: {ex.StackTrace}");

                // 오류 발생 시 빈 EditorViewModel 생성
                try
                {
                    System.Diagnostics.Debug.WriteLine($"[NotePageViewModel] 오류 복구 시도");
                    EditorViewModel = new NoteEditorViewModel();
                    EditorViewModel.SetSubjectId(subjectId);
                    SearchViewModel = new SearchViewModel(EditorViewModel);
                    SearchViewModel.SearchHighlightRequested += OnSearchHighlightRequested;
                    System.Diagnostics.Debug.WriteLine($"[NotePageViewModel] 오류 복구 완료");
                }
                catch (Exception recoveryEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[NotePageViewModel] 오류 복구 실패: {recoveryEx.Message}");
                }
            }
        }

        public void SetSubject(int subjectId, string subjectName)
        {
            try
            {
                SubjectTitle = subjectName;
                System.Diagnostics.Debug.WriteLine($"[NotePageViewModel] SetSubject 시작 - SubjectId: {subjectId}, SubjectName: '{subjectName}'");

                // 새로운 과목 데이터로 EditorViewModel과 SearchViewModel 다시 로드
                LoadNote(subjectId);

                // ✅ 추가 검증: EditorViewModel이 제대로 생성되었는지 확인
                if (EditorViewModel == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[NotePageViewModel] ERROR: EditorViewModel이 null입니다!");

                    // 강제로 빈 EditorViewModel 생성
                    EditorViewModel = new NoteEditorViewModel();
                    EditorViewModel.SetSubjectId(subjectId);

                    if (SearchViewModel == null)
                    {
                        SearchViewModel = new SearchViewModel(EditorViewModel);
                        SearchViewModel.SearchHighlightRequested += OnSearchHighlightRequested;
                    }
                }

                // ✅ SubjectId가 제대로 설정되었는지 최종 확인
                if (EditorViewModel.SubjectId != subjectId)
                {
                    System.Diagnostics.Debug.WriteLine($"[NotePageViewModel] SubjectId 불일치 감지! Expected: {subjectId}, Actual: {EditorViewModel.SubjectId}");
                    EditorViewModel.SubjectId = subjectId;
                    System.Diagnostics.Debug.WriteLine($"[NotePageViewModel] SubjectId 강제 수정 완료");
                }

                // ✅ 최종 상태 출력
                System.Diagnostics.Debug.WriteLine($"[NotePageViewModel] SetSubject 완료");
                System.Diagnostics.Debug.WriteLine($"  - SubjectTitle: '{SubjectTitle}'");
                System.Diagnostics.Debug.WriteLine($"  - EditorViewModel.SubjectId: {EditorViewModel?.SubjectId ?? -1}");
                System.Diagnostics.Debug.WriteLine($"  - EditorViewModel.Lines.Count: {EditorViewModel?.Lines?.Count ?? 0}");

                if (EditorViewModel?.Lines?.Count > 0)
                {
                    var firstLine = EditorViewModel.Lines[0];
                    System.Diagnostics.Debug.WriteLine($"  - 첫 번째 라인: SubjectId={firstLine.SubjectId}, Content='{firstLine.Content}', CategoryId={firstLine.CategoryId}");
                }

                OnPropertyChanged(nameof(EditorViewModel));
                OnPropertyChanged(nameof(SearchViewModel));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NotePageViewModel] SetSubject 오류: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[NotePageViewModel] 스택 트레이스: {ex.StackTrace}");

                // 오류 발생 시 최소한의 기능 제공
                try
                {
                    EditorViewModel = new NoteEditorViewModel();
                    EditorViewModel.SetSubjectId(subjectId);
                    SearchViewModel = new SearchViewModel(EditorViewModel);
                    SearchViewModel.SearchHighlightRequested += OnSearchHighlightRequested;

                    OnPropertyChanged(nameof(EditorViewModel));
                    OnPropertyChanged(nameof(SearchViewModel));

                    System.Diagnostics.Debug.WriteLine($"[NotePageViewModel] 오류 복구 완료");
                }
                catch (Exception recoveryEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[NotePageViewModel] 오류 복구 실패: {recoveryEx.Message}");
                }
            }
        }

        public void ScrollToCategory(int categoryId)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[NotePageViewModel] Category {categoryId}로 스크롤 요청");

                if (EditorViewModel != null)
                {
                    EditorViewModel.ScrollToCategory(categoryId);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[NotePageViewModel] EditorViewModel이 null이어서 스크롤 불가");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NotePageViewModel] Category 스크롤 오류: {ex.Message}");
            }
        }

        // View가 닫힐 때 호출
        public void SaveChanges()
        {
            try
            {
                EditorViewModel?.OnViewClosing();
                System.Diagnostics.Debug.WriteLine("[NotePageViewModel] 변경사항 저장 완료");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NotePageViewModel] 저장 오류: {ex.Message}");
            }
        }

        private void OnSearchHighlightRequested(object sender, SearchHighlightEventArgs e)
        {
            // 검색 결과 하이라이트 처리
            // View에서 처리하도록 이벤트 전파
            SearchHighlightRequested?.Invoke(this, e);
        }

        public event EventHandler<SearchHighlightEventArgs> SearchHighlightRequested;

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
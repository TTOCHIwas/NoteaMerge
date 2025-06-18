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

                // 계층 구조를 지원하는 로드 메서드 사용
                var noteData = NoteRepository.LoadNotesBySubjectWithHierarchy(subjectId);

                System.Diagnostics.Debug.WriteLine($"[NotePageViewModel] LoadNotesBySubjectWithHierarchy 결과 - 카테고리 수: {noteData?.Count ?? 0}");

                // 데이터가 없으면 기본 로드 메서드 시도
                if (noteData == null || noteData.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[NotePageViewModel] 계층 구조 로딩 실패, 기본 메소드 시도");
                    noteData = NoteRepository.LoadNotesBySubject(subjectId);
                    System.Diagnostics.Debug.WriteLine($"[NotePageViewModel] LoadNotesBySubject 결과 - 카테고리 수: {noteData?.Count ?? 0}");
                }

                // EditorViewModel 생성
                System.Diagnostics.Debug.WriteLine($"[NotePageViewModel] NoteEditorViewModel 생성 중...");
                EditorViewModel = new NoteEditorViewModel(noteData);

                // ✅ 중요: SubjectId 설정
                if (EditorViewModel != null)
                {
                    EditorViewModel.SetSubjectId(subjectId); // 새로 추가된 메소드 사용
                    System.Diagnostics.Debug.WriteLine($"[NotePageViewModel] EditorViewModel SubjectId 설정 완료: {subjectId}");
                }

                // ❌ 기본 카테고리 생성 로직 완전 제거
                // 새 과목인 경우 빈 상태에서 시작하도록 함

                // 로딩된 데이터 상세 정보 출력
                if (noteData != null && noteData.Count > 0)
                {
                    foreach (var category in noteData)
                    {
                        System.Diagnostics.Debug.WriteLine($"[NotePageViewModel] 카테고리: '{category.Title}' (ID: {category.CategoryId}), 라인 수: {category.Lines?.Count ?? 0}");
                        if (category.Lines != null)
                        {
                            foreach (var line in category.Lines.Take(3)) // 처음 3개만 출력
                            {
                                System.Diagnostics.Debug.WriteLine($"  - 라인: '{line.Content?.Substring(0, Math.Min(50, line.Content?.Length ?? 0))}'");
                            }
                        }
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[NotePageViewModel] SubjectId {subjectId}에 대한 필기 데이터 없음 - 새 과목으로 시작");
                }

                // EditorViewModel 생성
                System.Diagnostics.Debug.WriteLine($"[NotePageViewModel] NoteEditorViewModel 생성 중...");
                EditorViewModel = new NoteEditorViewModel(noteData);

                // ✅ SubjectId 강제 설정 (중요!)
                if (EditorViewModel != null)
                {
                    EditorViewModel.SubjectId = subjectId;
                    System.Diagnostics.Debug.WriteLine($"[NotePageViewModel] EditorViewModel SubjectId 설정: {subjectId}");
                }

                System.Diagnostics.Debug.WriteLine($"[NotePageViewModel] EditorViewModel 생성 완료 - Lines 수: {EditorViewModel?.Lines?.Count ?? 0}");

                // SearchViewModel 초기화
                SearchViewModel = new SearchViewModel(EditorViewModel);
                SearchViewModel.SearchHighlightRequested += OnSearchHighlightRequested;

                System.Diagnostics.Debug.WriteLine($"[NotePageViewModel] LoadNote 완료");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NotePageViewModel] LoadNote 오류: {ex.Message}");

                // 오류 발생 시 빈 EditorViewModel 생성
                EditorViewModel = new NoteEditorViewModel();
                EditorViewModel.SetSubjectId(subjectId);
                SearchViewModel = new SearchViewModel(EditorViewModel);
                SearchViewModel.SearchHighlightRequested += OnSearchHighlightRequested;
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
                }

                // ✅ SubjectId가 제대로 설정되었는지 최종 확인
                if (EditorViewModel.SubjectId != subjectId)
                {
                    System.Diagnostics.Debug.WriteLine($"[NotePageViewModel] SubjectId 불일치 감지! 재설정 중...");
                    EditorViewModel.SetSubjectId(subjectId);
                }

                // SearchViewModel 확인
                if (SearchViewModel == null)
                {
                    SearchViewModel = new SearchViewModel(EditorViewModel);
                    SearchViewModel.SearchHighlightRequested += OnSearchHighlightRequested;
                }

                // PropertyChanged 이벤트 강제 발생
                OnPropertyChanged(nameof(EditorViewModel));
                OnPropertyChanged(nameof(SearchViewModel));

                System.Diagnostics.Debug.WriteLine($"[NotePageViewModel] SetSubject 완료");
                System.Diagnostics.Debug.WriteLine($"  - SubjectTitle: '{SubjectTitle}'");
                System.Diagnostics.Debug.WriteLine($"  - EditorViewModel.SubjectId: {EditorViewModel?.SubjectId}");
                System.Diagnostics.Debug.WriteLine($"  - EditorViewModel.Lines.Count: {EditorViewModel?.Lines?.Count}");

                // ✅ 각 라인의 SubjectId도 확인
                if (EditorViewModel?.Lines != null)
                {
                    foreach (var line in EditorViewModel.Lines.Take(3)) // 처음 3개만
                    {
                        System.Diagnostics.Debug.WriteLine($"  - Line SubjectId: {line.SubjectId}, Content: '{line.Content?.Substring(0, Math.Min(15, line.Content?.Length ?? 0))}'");
                    }
                }
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
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
            // 계층 구조를 지원하는 로드 메서드 사용
            var noteData = NoteRepository.LoadNotesBySubjectWithHierarchy(subjectId);

            // 데이터가 없으면 기본 로드 메서드 시도
            if (noteData == null || noteData.Count == 0)
            {
                noteData = NoteRepository.LoadNotesBySubject(subjectId);
            }

            EditorViewModel = new NoteEditorViewModel(noteData);

            // SearchViewModel 초기화
            SearchViewModel = new SearchViewModel(EditorViewModel);

            // 검색 하이라이트 이벤트 처리
            SearchViewModel.SearchHighlightRequested += OnSearchHighlightRequested;
        }

        public void SetSubject(int subjectId, string subjectName)
        {
            try
            {
                SubjectTitle = subjectName;

                // 새로운 과목 데이터로 EditorViewModel과 SearchViewModel 다시 로드
                LoadNote(subjectId);

                System.Diagnostics.Debug.WriteLine($"[NotePageViewModel] 과목 설정: {subjectName} (ID: {subjectId})");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NotePageViewModel] 과목 설정 오류: {ex.Message}");
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
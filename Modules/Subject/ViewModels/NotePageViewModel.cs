using Notea.Modules.Subject.Models;
using System;
using System.ComponentModel;

namespace Notea.Modules.Subject.ViewModels
{
    public class NotePageViewModel : INotifyPropertyChanged
    {
        private NoteEditorViewModel _editorViewModel;
        private SearchViewModel _searchViewModel;
        private string _subjectTitle;

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
            SubjectTitle = "윈도우즈 프로그래밍";
            LoadNote(1);
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

        // View가 닫힐 때 호출
        public void SaveChanges()
        {
            EditorViewModel?.OnViewClosing();
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
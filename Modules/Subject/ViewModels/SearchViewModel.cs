// 1. SearchViewModel.cs - 완전한 구현
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Text.RegularExpressions;

namespace Notea.Modules.Subject.ViewModels
{
    public class SearchViewModel : INotifyPropertyChanged
    {
        private string _searchQuery = "";
        private ObservableCollection<SearchResult> _searchResults = new();
        private NoteEditorViewModel _editorViewModel;
        private bool _isSearchPanelVisible = false;
        private int _currentResultIndex = -1;

        public SearchViewModel(NoteEditorViewModel editorViewModel)
        {
            _editorViewModel = editorViewModel;
            SearchCommand = new RelayCommand(ExecuteSearch);
            NextResultCommand = new RelayCommand(GoToNextResult);
            PreviousResultCommand = new RelayCommand(GoToPreviousResult);
            CloseSearchCommand = new RelayCommand(CloseSearch);
        }

        public string SearchQuery
        {
            get => _searchQuery;
            set
            {
                if (_searchQuery != value)
                {
                    _searchQuery = value;
                    OnPropertyChanged();
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        SearchResults.Clear();
                        CurrentResultIndex = -1;
                    }
                }
            }
        }

        public ObservableCollection<SearchResult> SearchResults
        {
            get => _searchResults;
            set
            {
                _searchResults = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasResults));
                OnPropertyChanged(nameof(ResultCountText));
            }
        }

        public bool IsSearchPanelVisible
        {
            get => _isSearchPanelVisible;
            set
            {
                if (_isSearchPanelVisible != value)
                {
                    _isSearchPanelVisible = value;
                    OnPropertyChanged();
                }
            }
        }

        public int CurrentResultIndex
        {
            get => _currentResultIndex;
            set
            {
                if (_currentResultIndex != value)
                {
                    _currentResultIndex = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CurrentResultText));
                }
            }
        }

        public bool HasResults => SearchResults.Count > 0;

        public string ResultCountText =>
            HasResults ? $"{SearchResults.Count}개 결과" : "결과 없음";

        public string CurrentResultText =>
            HasResults && CurrentResultIndex >= 0
                ? $"{CurrentResultIndex + 1} / {SearchResults.Count}"
                : "";

        // Commands
        public ICommand SearchCommand { get; }
        public ICommand NextResultCommand { get; }
        public ICommand PreviousResultCommand { get; }
        public ICommand CloseSearchCommand { get; }

        private void ExecuteSearch(object parameter)
        {
            if (string.IsNullOrWhiteSpace(SearchQuery))
                return;

            SearchResults.Clear();
            CurrentResultIndex = -1;

            var query = SearchQuery.ToLower();
            var lines = _editorViewModel.Lines;

            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                var content = line.Content?.ToLower() ?? "";

                if (content.Contains(query))
                {
                    // 모든 일치 위치 찾기
                    int index = 0;
                    while ((index = content.IndexOf(query, index)) != -1)
                    {
                        SearchResults.Add(new SearchResult
                        {
                            LineIndex = i,
                            Line = line,
                            MatchStartIndex = index,
                            MatchLength = query.Length,
                            Context = GetContext(line.Content, index, query.Length)
                        });
                        index += query.Length;
                    }
                }
            }

            if (HasResults)
            {
                CurrentResultIndex = 0;
                HighlightResult(SearchResults[0]);
            }
        }

        private string GetContext(string content, int matchIndex, int matchLength)
        {
            int contextLength = 30; // 전후 30자
            int start = Math.Max(0, matchIndex - contextLength);
            int end = Math.Min(content.Length, matchIndex + matchLength + contextLength);

            string context = content.Substring(start, end - start);

            if (start > 0) context = "..." + context;
            if (end < content.Length) context = context + "...";

            return context;
        }

        private void GoToNextResult(object parameter)
        {
            if (!HasResults) return;

            CurrentResultIndex = (CurrentResultIndex + 1) % SearchResults.Count;
            HighlightResult(SearchResults[CurrentResultIndex]);
        }

        private void GoToPreviousResult(object parameter)
        {
            if (!HasResults) return;

            CurrentResultIndex = CurrentResultIndex > 0
                ? CurrentResultIndex - 1
                : SearchResults.Count - 1;
            HighlightResult(SearchResults[CurrentResultIndex]);
        }

        private void HighlightResult(SearchResult result)
        {
            // 해당 라인으로 스크롤 및 하이라이트
            result.Line.IsEditing = true;

            // SearchHighlightRequested 이벤트 발생
            OnSearchHighlightRequested(new SearchHighlightEventArgs
            {
                LineIndex = result.LineIndex,
                StartIndex = result.MatchStartIndex,
                Length = result.MatchLength
            });
        }

        private void CloseSearch(object parameter)
        {
            IsSearchPanelVisible = false;
            SearchQuery = "";
            SearchResults.Clear();
            CurrentResultIndex = -1;
        }

        public event EventHandler<SearchHighlightEventArgs> SearchHighlightRequested;

        protected virtual void OnSearchHighlightRequested(SearchHighlightEventArgs e)
        {
            SearchHighlightRequested?.Invoke(this, e);
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public class SearchResult
    {
        public int LineIndex { get; set; }
        public MarkdownLineViewModel Line { get; set; }
        public int MatchStartIndex { get; set; }
        public int MatchLength { get; set; }
        public string Context { get; set; }
    }

    public class SearchHighlightEventArgs : EventArgs
    {
        public int LineIndex { get; set; }
        public int StartIndex { get; set; }
        public int Length { get; set; }
    }

    // RelayCommand 구현
    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Predicate<object> _canExecute;

        public RelayCommand(Action<object> execute, Predicate<object> canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object parameter) => _canExecute?.Invoke(parameter) ?? true;
        public void Execute(object parameter) => _execute(parameter);
    }
}
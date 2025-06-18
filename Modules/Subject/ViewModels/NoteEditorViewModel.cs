using Microsoft.Data.Sqlite;
using Notea.Helpers;
using Notea.Modules.Common.ViewModels;
using Notea.Modules.Subject.Models;
using Notea.ViewModels;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Transactions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using static Notea.Modules.Subject.ViewModels.MarkdownLineViewModel;

namespace Notea.Modules.Subject.ViewModels
{
    public class NoteEditorViewModel : INotifyPropertyChanged
    {

        private RightSidebarViewModel _rightSidebarViewModel;

        private readonly UndoRedoManager<NoteState> _undoRedoManager = new();
        public ObservableCollection<MarkdownLineViewModel> Lines { get; set; }
        private int _nextDisplayOrder = 1;
        public int SubjectId { get; set; } = 1;
        public int CurrentCategoryId { get; set; } = 1;

        private Stack<(int categoryId, int level)> _categoryStack = new();


        private DispatcherTimer _idleTimer;
        private DateTime _lastActivityTime;
        private const int IDLE_TIMEOUT_SECONDS = 2; // 5초간 입력이 없으면 저장

        public NoteEditorViewModel()
        {
            Lines = new ObservableCollection<MarkdownLineViewModel>();
            InitializeIdleTimer();
            InitializeTimerIntegration();

            // ✅ 빈 라인 하나 추가 (SubjectId는 나중에 설정됨)
            var emptyLine = new MarkdownLineViewModel
            {
                IsEditing = true,
                SubjectId = 0, // 초기값, 나중에 SetSubjectId에서 설정됨
                CategoryId = 0, // 초기값, 실제 저장시 자동 할당
                Content = "",
                DisplayOrder = 1,
                TextId = 0,
                Index = 0
            };

            emptyLine.SetOriginalContent("");
            Lines.Add(emptyLine);
            RegisterLineEvents(emptyLine);

            _nextDisplayOrder = 2;
            CurrentCategoryId = 0; // 초기값

            Lines.CollectionChanged += Lines_CollectionChanged;

            Debug.WriteLine($"[NoteEditor] 기본 생성자 완료 - 빈 라인 1개 추가");
        }

        public NoteEditorViewModel(List<NoteCategory> loadedNotes)
        {
            Lines = new ObservableCollection<MarkdownLineViewModel>();
            InitializeIdleTimer();
            InitializeTimerIntegration();
            int currentDisplayOrder = 1;

            Debug.WriteLine($"[LOAD] NoteEditorViewModel 생성 시작. 카테고리 수: {loadedNotes?.Count ?? 0}");

            if (loadedNotes != null && loadedNotes.Count > 0)
            {
                // 재귀적으로 카테고리와 라인 추가
                foreach (var category in loadedNotes)
                {
                    Debug.WriteLine($"[LOAD] 카테고리 처리: '{category.Title}' (ID: {category.CategoryId})");
                    currentDisplayOrder = AddCategoryWithHierarchy(category, currentDisplayOrder);
                }

                _nextDisplayOrder = currentDisplayOrder;
                Debug.WriteLine($"[LOAD] 로드 완료. 총 라인 수: {Lines.Count}");

                // ✅ 첫 번째 카테고리의 ID를 CurrentCategoryId로 설정
                var firstCategory = Lines.FirstOrDefault(l => l.IsHeadingLine);
                if (firstCategory != null)
                {
                    CurrentCategoryId = firstCategory.CategoryId;
                    Debug.WriteLine($"[LOAD] CurrentCategoryId 설정: {CurrentCategoryId}");
                }
            }

            // 라인이 없으면 빈 라인 추가
            if (Lines.Count == 0)
            {
                Debug.WriteLine("[LOAD] 로드된 데이터 없음. 빈 라인 추가.");
                AddInitialEmptyLine();
            }

            Lines.CollectionChanged += Lines_CollectionChanged;
        }

        public void SetSubjectId(int subjectId)
        {
            SubjectId = subjectId;

            // 모든 라인의 SubjectId 업데이트
            foreach (var line in Lines)
            {
                if (line.SubjectId != subjectId)
                {
                    line.SubjectId = subjectId;
                }
            }

            Debug.WriteLine($"[NoteEditor] SubjectId 설정 완료: {subjectId} (총 {Lines.Count}개 라인)");
        }

        private void InitializeTimerIntegration()
        {
            try
            {
                _rightSidebarViewModel = FindRightSidebarViewModel();
                if (_rightSidebarViewModel != null && CurrentCategoryId > 0)
                {
                    var subjectName = GetSubjectNameById(SubjectId);
                    _rightSidebarViewModel.SetActiveCategory(CurrentCategoryId, subjectName);
                    System.Diagnostics.Debug.WriteLine($"[NoteEditor] 활성 카테고리 설정: CategoryId={CurrentCategoryId}, Subject={subjectName}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NoteEditor] 타이머 연동 초기화 오류: {ex.Message}");
            }
        }

        private RightSidebarViewModel FindRightSidebarViewModel()
        {
            try
            {
                // MainWindow에서 RightSidebarViewModel 찾기
                if (Application.Current?.MainWindow?.DataContext is MainViewModel mainViewModel)
                {
                    var rightSidebarProp = mainViewModel.GetType().GetProperty("RightSidebarViewModel");
                    if (rightSidebarProp != null)
                    {
                        return rightSidebarProp.GetValue(mainViewModel) as RightSidebarViewModel;
                    }
                }

                // 다른 방법들...
                return null;
            }
            catch
            {
                return null;
            }
        }

        private string GetSubjectNameById(int subjectId)
        {
            try
            {
                using var conn = Notea.Modules.Common.Helpers.DatabaseHelper.Instance.GetConnection();
                conn.Open();
                using var cmd = conn.CreateCommand();

                cmd.CommandText = "SELECT Name FROM Subject WHERE subjectId = @subjectId";
                cmd.Parameters.AddWithValue("@subjectId", subjectId);

                var result = cmd.ExecuteScalar();
                return result?.ToString() ?? "";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NoteEditor] 과목명 조회 오류: {ex.Message}");
                return "";
            }
        }

        private void AddInitialEmptyLine()
        {
            var emptyLine = new MarkdownLineViewModel
            {
                IsEditing = true,
                SubjectId = this.SubjectId, // 현재 설정된 SubjectId 사용
                CategoryId = 0, // 초기값, 실제 저장시 자동 할당
                Content = "",
                DisplayOrder = 1,
                TextId = 0,
                Index = 0
            };

            emptyLine.SetOriginalContent("");
            Lines.Add(emptyLine);
            RegisterLineEvents(emptyLine);

            CurrentCategoryId = 0; // 초기값
            Debug.WriteLine($"[LOAD] 빈 라인 추가됨. SubjectId: {this.SubjectId}");
        }
        private int AddCategoryWithHierarchy(NoteCategory category, int displayOrder)
        {
            CurrentCategoryId = category.CategoryId;

            Debug.WriteLine($"[LOAD] 카테고리 '{category.Title}' 추가 중...");

            // 카테고리 제목 추가
            var categoryLine = new MarkdownLineViewModel
            {
                Content = category.Title,
                IsEditing = false,
                SubjectId = this.SubjectId,
                CategoryId = category.CategoryId,
                TextId = 0,
                IsHeadingLine = true,
                Level = category.Level,
                DisplayOrder = displayOrder++
            };

            categoryLine.SetOriginalContent(category.Title);
            Lines.Add(categoryLine);
            RegisterLineEvents(categoryLine);

            Debug.WriteLine($"[LOAD] 카테고리 제목 라인 추가됨. 텍스트 수: {category.Lines.Count}");

            // 카테고리의 라인들 추가 (이미지 포함)
            foreach (var line in category.Lines)
            {
                var contentLine = new MarkdownLineViewModel
                {
                    Content = line.Content,
                    ContentType = line.ContentType ?? "text",
                    ImageUrl = line.ImageUrl,
                    IsEditing = false,
                    SubjectId = this.SubjectId,
                    CategoryId = category.CategoryId,
                    TextId = line.Index,
                    Index = Lines.Count,
                    DisplayOrder = displayOrder++
                };

                contentLine.SetOriginalContent(line.Content, line.ImageUrl);
                Lines.Add(contentLine);
                RegisterLineEvents(contentLine);

                if (line.ContentType == "image")
                {
                    Debug.WriteLine($"[LOAD] 이미지 라인 추가: URL={line.ImageUrl}");

                    // 이미지 파일 존재 확인
                    if (!string.IsNullOrEmpty(line.ImageUrl))
                    {
                        string fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, line.ImageUrl);
                        if (File.Exists(fullPath))
                        {
                            Debug.WriteLine($"[LOAD] 이미지 파일 확인됨: {fullPath}");
                        }
                        else
                        {
                            Debug.WriteLine($"[LOAD ERROR] 이미지 파일 없음: {fullPath}");
                        }
                    }
                }
                else
                {
                    Debug.WriteLine($"[LOAD] 텍스트 라인 추가: '{line.Content.Substring(0, Math.Min(30, line.Content.Length))}'...");
                }
            }

            // 하위 카테고리들 재귀적으로 추가
            foreach (var subCategory in category.SubCategories)
            {
                Debug.WriteLine($"[LOAD] 하위 카테고리 처리: '{subCategory.Title}'");
                displayOrder = AddCategoryWithHierarchy(subCategory, displayOrder);
            }

            return displayOrder;
        }

        private void InitializeIdleTimer()
        {
            _idleTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _idleTimer.Tick += CheckIdleAndSave;
            _idleTimer.Start();
            _lastActivityTime = DateTime.Now;
        }

        public void UpdateActivity()
        {
            try
            {
                _lastActivityTime = DateTime.Now;

                // 타이머가 실행 중이고 활성 카테고리가 설정되어 있으면 학습 활동으로 간주
                if (_rightSidebarViewModel?.IsTimerRunning == true && CurrentCategoryId > 0)
                {
                    // 현재 편집 중인 카테고리가 타이머의 활성 카테고리와 다르면 업데이트
                    var subjectName = GetSubjectNameById(SubjectId);
                    _rightSidebarViewModel.SetActiveCategory(CurrentCategoryId, subjectName);
                }

                System.Diagnostics.Debug.WriteLine($"[NoteEditor] 활동 업데이트: CategoryId={CurrentCategoryId}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NoteEditor] 활동 업데이트 오류: {ex.Message}");
            }
        }



        private void CheckIdleAndSave(object sender, EventArgs e)
        {
            if ((DateTime.Now - _lastActivityTime).TotalSeconds >= IDLE_TIMEOUT_SECONDS)
            {
                Debug.WriteLine($"[IDLE] {IDLE_TIMEOUT_SECONDS}초간 유휴 상태 감지. 자동 저장 시작.");
                DebugPrintCurrentState();
                UpdateActivity();
                SaveAllChanges();
            }
        }

        public class NoteState
        {
            public List<LineState> Lines { get; set; }
            public DateTime Timestamp { get; set; }
        }

        public class LineState
        {
            public string Content { get; set; }
            public int CategoryId { get; set; }
            public int TextId { get; set; }
            public bool IsHeadingLine { get; set; }
        }

        // 현재 상태 저장
        private void SaveCurrentState()
        {
            var state = new NoteState
            {
                Timestamp = DateTime.Now,
                Lines = Lines.Select(l => new LineState
                {
                    Content = l.Content,
                    CategoryId = l.CategoryId,
                    TextId = l.TextId,
                    IsHeadingLine = l.IsHeadingLine
                }).ToList()
            };

            _undoRedoManager.AddState(state);
        }

        // Ctrl+Z 처리
        public void Undo()
        {
            var previousState = _undoRedoManager.Undo();
            if (previousState != null)
            {
                RestoreState(previousState);
            }
        }

        // Ctrl+Y 처리
        public void Redo()
        {
            var nextState = _undoRedoManager.Redo();
            if (nextState != null)
            {
                RestoreState(nextState);
            }
        }

        private void RestoreState(NoteState state)
        {
            // 상태 복원 로직
            Lines.Clear();
            foreach (var lineState in state.Lines)
            {
                var line = new MarkdownLineViewModel
                {
                    Content = lineState.Content,
                    CategoryId = lineState.CategoryId,
                    TextId = lineState.TextId,
                    IsHeadingLine = lineState.IsHeadingLine,
                    SubjectId = this.SubjectId
                };
                Lines.Add(line);
                RegisterLineEvents(line);
            }
        }

        private void RegisterLineEvents(MarkdownLineViewModel line)
        {
            line.PropertyChanged += OnLinePropertyChanged;
            line.RequestFindPreviousCategory += OnRequestFindPreviousCategory;
        }
        private void UnregisterLineEvents(MarkdownLineViewModel line)
        {
            line.PropertyChanged -= OnLinePropertyChanged;
            line.RequestFindPreviousCategory -= OnRequestFindPreviousCategory;
        }

        private void Lines_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (Lines.Count == 0)
            {
                var newLine = new MarkdownLineViewModel
                {
                    IsEditing = true,
                    SubjectId = this.SubjectId,
                    CategoryId = this.CurrentCategoryId,
                    Content = "",
                    DisplayOrder = 1
                };
                Lines.Add(newLine);
                RegisterLineEvents(newLine);
            }

            // 라인이 제거된 경우
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Remove)
            {
                foreach (MarkdownLineViewModel removedLine in e.OldItems)
                {
                    if (removedLine.TextId > 0)
                    {
                        NoteRepository.DeleteLine(removedLine.TextId);
                        Debug.WriteLine($"[DEBUG] 라인 삭제됨. TextId: {removedLine.TextId}");
                    }
                    UnregisterLineEvents(removedLine);
                }
            }

            // 인덱스 재정렬
            UpdateLineIndices();
        }



        /// <summary>
        /// 새로운 라인 추가
        /// </summary>
        public void AddNewLine()
        {
            int categoryIdForNewLine = GetCurrentCategoryIdForNewLine();
            int displayOrder = Lines.Count > 0 ? Lines.Last().DisplayOrder + 1 : 1;

            Debug.WriteLine($"[ADD LINE] 새 라인 추가 시작. CategoryId: {categoryIdForNewLine}, DisplayOrder: {displayOrder}");

            var newLine = new MarkdownLineViewModel
            {
                IsEditing = true,
                Content = "",
                SubjectId = this.SubjectId,
                CategoryId = categoryIdForNewLine > 0 ? categoryIdForNewLine : 1,
                Index = Lines.Count,
                DisplayOrder = displayOrder,
                TextId = 0
            };

            Lines.Add(newLine);
            RegisterLineEvents(newLine);

            Debug.WriteLine($"[ADD LINE] 새 라인 추가 완료. Index: {newLine.Index}");
        }

        private int GetCurrentCategoryIdForNewLine()
        {
            // 현재 커서 위치나 마지막 활성 카테고리 찾기
            if (CurrentCategoryId > 0)
            {
                return CurrentCategoryId;
            }

            // 마지막 제목 라인의 CategoryId 찾기
            var lastHeading = Lines.LastOrDefault(l => l.IsHeadingLine && l.CategoryId > 0);
            if (lastHeading != null)
            {
                CurrentCategoryId = lastHeading.CategoryId;
                return CurrentCategoryId;
            }

            // 첫 번째 제목이 생성될 때까지는 0 반환 (저장시 자동 할당됨)
            return 0;
        }

        private void OnLinePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (sender is MarkdownLineViewModel line)
            {
                if (e.PropertyName == nameof(MarkdownLineViewModel.Content))
                {
                    if (line != null && line.HasChanges)
                    {
                        UpdateActivity();
                    }

                    // 제목 상태 변경 감지 및 처리
                    bool wasHeading = line.IsHeadingLine;
                    bool isHeading = NoteRepository.IsCategoryHeading(line.Content);

                    if (wasHeading != isHeading)
                    {
                        HandleHeadingStatusChange(line, wasHeading, isHeading);
                    }
                    else if (isHeading && wasHeading)
                    {
                        // 제목 레벨 변경 감지
                        int oldLevel = line.Level;
                        int newLevel = NoteRepository.GetHeadingLevel(line.Content);

                        if (oldLevel != newLevel && newLevel > 0)
                        {
                            line.Level = newLevel;
                            Debug.WriteLine($"[DEBUG] 제목 레벨 변경: {oldLevel} → {newLevel}");

                            // 레벨 변경시 즉시 하위 요소 재구성 예약
                            ScheduleHierarchyUpdate(line);
                        }
                    }
                }
                else if (e.PropertyName == nameof(MarkdownLineViewModel.CategoryId))
                {
                    if (line.IsHeadingLine && line.CategoryId > 0)
                    {
                        UpdateCurrentCategory(line);
                    }
                }
            }
        }

        private void HandleHeadingStatusChange(MarkdownLineViewModel line, bool wasHeading, bool isHeading)
        {
            try
            {
                if (wasHeading && !isHeading)
                {
                    // 제목에서 일반 텍스트로 변경
                    Debug.WriteLine($"[DEBUG] 제목에서 일반 텍스트로 변경됨: {line.Content}");

                    // 기본 카테고리(1)인 경우 특별 처리
                    if (line.CategoryId <= 1)
                    {
                        Debug.WriteLine("[WARNING] 기본 카테고리는 삭제할 수 없습니다.");
                        line.IsHeadingLine = false;
                        line.Level = 0;
                        line.TextId = 0; // 새로운 텍스트로 생성
                        return;
                    }

                    int previousCategoryId = FindPreviousCategoryId(line);

                    // 현재 카테고리의 하위 텍스트들을 이전 카테고리로 재할당
                    if (previousCategoryId > 0)
                    {
                        NoteRepository.ReassignTextsToCategory(line.CategoryId, previousCategoryId);
                    }

                    // 카테고리 삭제
                    NoteRepository.DeleteCategory(line.CategoryId);

                    line.IsHeadingLine = false;
                    line.CategoryId = previousCategoryId > 0 ? previousCategoryId : 1;
                    line.TextId = 0; // 새로운 텍스트로 생성
                    line.Level = 0;

                    // 현재 카테고리 업데이트
                    if (CurrentCategoryId == line.CategoryId)
                    {
                        CurrentCategoryId = previousCategoryId > 0 ? previousCategoryId : 1;
                    }

                    // 하위 라인들의 카테고리 재할당
                    ReassignSubsequentLines(line, previousCategoryId > 0 ? previousCategoryId : 1);
                }
                else if (!wasHeading && isHeading)
                {
                    // 일반 텍스트에서 제목으로 변경
                    Debug.WriteLine($"[DEBUG] 일반 텍스트에서 제목으로 변경됨: {line.Content}");

                    // 기존 텍스트 삭제
                    if (line.TextId > 0)
                    {
                        NoteRepository.DeleteLine(line.TextId);
                        line.TextId = 0;
                    }

                    line.IsHeadingLine = true;
                    line.Level = NoteRepository.GetHeadingLevel(line.Content);
                    line.CategoryId = 0; // 새로운 카테고리로 생성 (저장 시점에 처리)

                    // 하위 라인들의 카테고리 업데이트 예약
                    ScheduleHierarchyUpdate(line);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERROR] HandleHeadingStatusChange 실패: {ex.Message}");
                throw;
            }
        }

        private void ReassignSubsequentLines(MarkdownLineViewModel changedLine, int newCategoryId)
        {
            try
            {
                int lineIndex = Lines.IndexOf(changedLine);

                for (int i = lineIndex + 1; i < Lines.Count; i++)
                {
                    var subsequentLine = Lines[i];

                    // 다음 제목을 만나면 중단
                    if (subsequentLine.IsHeadingLine && subsequentLine.Level <= changedLine.Level)
                    {
                        break;
                    }

                    // 일반 텍스트라면 카테고리 재할당
                    if (!subsequentLine.IsHeadingLine)
                    {
                        subsequentLine.CategoryId = newCategoryId;
                        Debug.WriteLine($"[DEBUG] 라인 재할당: TextId={subsequentLine.TextId}, 새 CategoryId={newCategoryId}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERROR] ReassignSubsequentLines 실패: {ex.Message}");
            }
        }

        private int FindPreviousCategoryId(MarkdownLineViewModel currentLine)
        {
            try
            {
                int lineIndex = Lines.IndexOf(currentLine);

                // 현재 라인 이전에 있는 가장 가까운 제목 찾기
                for (int i = lineIndex - 1; i >= 0; i--)
                {
                    if (Lines[i].IsHeadingLine && Lines[i].CategoryId > 0)
                    {
                        return Lines[i].CategoryId;
                    }
                }

                // 이전 제목이 없으면 데이터베이스에서 찾기
                return NoteRepository.FindPreviousCategoryIdByDisplayOrder(SubjectId, currentLine.DisplayOrder);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERROR] FindPreviousCategoryId 실패: {ex.Message}");
                return 1;
            }
        }

        private void ScheduleHierarchyUpdate(MarkdownLineViewModel line)
        {
            // 실제 구현에서는 펜딩 업데이트 큐에 추가
            // 여기서는 즉시 표시만 업데이트
            line.OnPropertyChanged(nameof(line.HasChanges));
        }

        private void ScheduleCategoryDeletion(MarkdownLineViewModel line)
        {
            // 삭제 예약 처리 (저장 시점에 실제 삭제)
            line.OnPropertyChanged(nameof(line.HasChanges));
        }
        private void ScheduleNewCategoryCreation(MarkdownLineViewModel line)
{
    // 생성 예약 처리 (저장 시점에 실제 생성)
    line.OnPropertyChanged(nameof(line.HasChanges));
}

        private void OnCategoryCreated(object sender, CategoryCreatedEventArgs e)
        {
            try
            {
                if (sender is MarkdownLineViewModel line)
                {
                    CurrentCategoryId = e.NewCategoryId;
                    Debug.WriteLine($"[DEBUG] 새 카테고리 생성됨. CurrentCategoryId 업데이트: {CurrentCategoryId}");

                    if (_rightSidebarViewModel != null)
                    {
                        var subjectName = GetSubjectNameById(SubjectId);
                        _rightSidebarViewModel.SetActiveCategory(CurrentCategoryId, subjectName);
                        System.Diagnostics.Debug.WriteLine($"[NoteEditor] 카테고리 변경 알림: CategoryId={CurrentCategoryId}");
                    }

                    // 이 제목 이후의 모든 라인들의 CategoryId 업데이트
                    int headingIndex = Lines.IndexOf(line);
                    UpdateSubsequentLinesCategoryId(headingIndex + 1, CurrentCategoryId);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NoteEditor] 카테고리 생성 처리 오류: {ex.Message}");
            }
        }

        private void UpdateSubsequentLinesCategoryId(int startIndex, int categoryId)
        {
            for (int i = startIndex; i < Lines.Count; i++)
            {
                if (!Lines[i].IsHeadingLine)
                {
                    if (Lines[i].CategoryId != categoryId)
                    {
                        Lines[i].CategoryId = categoryId;
                        Debug.WriteLine($"[DEBUG] 라인 {i}의 CategoryId 업데이트: {categoryId}");
                    }
                }
                else
                {
                    break; // 다음 제목을 만나면 중단
                }
            }
        }


        private void OnRequestFindPreviousCategory(object sender, MarkdownLineViewModel.FindPreviousCategoryEventArgs e)
        {
            try
            {
                if (e.CurrentLine != null)
                {
                    e.PreviousCategoryId = FindPreviousCategoryIdForLine(e.CurrentLine);
                }
                else
                {
                    e.PreviousCategoryId = 1; // 기본값
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERROR] OnRequestFindPreviousCategory 실패: {ex.Message}");
                e.PreviousCategoryId = 1; // 기본값
            }
        }

        private int FindPreviousCategoryIdForLine(MarkdownLineViewModel currentLine)
        {
            try
            {
                int lineIndex = Lines.IndexOf(currentLine);

                // 현재 라인 이전에 있는 가장 가까운 제목 찾기
                for (int i = lineIndex - 1; i >= 0; i--)
                {
                    if (Lines[i].IsHeadingLine && Lines[i].CategoryId > 0)
                    {
                        return Lines[i].CategoryId;
                    }
                }

                // 이전 제목이 없으면 데이터베이스에서 찾기
                return NoteRepository.FindPreviousCategoryIdByDisplayOrder(SubjectId, currentLine.DisplayOrder);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERROR] FindPreviousCategoryIdForLine 실패: {ex.Message}");
                return 1;
            }
        }



        private void UpdateCurrentCategory(MarkdownLineViewModel headingLine)
        {
            // 제목 라인이 저장된 후 CategoryId가 설정되면 현재 카테고리로 설정
            if (headingLine.CategoryId > 0)
            {
                CurrentCategoryId = headingLine.CategoryId;
                Debug.WriteLine($"[DEBUG] 현재 카테고리 변경됨: {CurrentCategoryId}");

                // 이 제목 이후의 모든 라인들의 CategoryId 업데이트
                int headingIndex = Lines.IndexOf(headingLine);
                for (int i = headingIndex + 1; i < Lines.Count; i++)
                {
                    if (!Lines[i].IsHeadingLine) // 다음 제목이 나올 때까지
                    {
                        Lines[i].CategoryId = CurrentCategoryId;
                    }
                    else
                    {
                        break; // 다음 제목을 만나면 중단
                    }
                }
            }
        }

        public void RemoveLine(MarkdownLineViewModel line)
        {
            if (Lines.Contains(line))
            {
                if (line.IsHeadingLine && line.CategoryId > 0)
                {
                    // 하위 텍스트들을 이전 카테고리로 재할당
                    int previousCategoryId = GetPreviousCategoryId(Lines.IndexOf(line));
                    if (previousCategoryId > 0)
                    {
                        NoteRepository.ReassignTextsToCategory(line.CategoryId, previousCategoryId);
                    }

                    // 카테고리만 삭제 (텍스트는 재할당됨)
                    NoteRepository.DeleteCategory(line.CategoryId, false);

                    // 현재 카테고리가 삭제되는 경우
                    if (CurrentCategoryId == line.CategoryId)
                    {
                        CurrentCategoryId = previousCategoryId > 0 ? previousCategoryId : 1;
                    }
                }

                Lines.Remove(line);
                UnregisterLineEvents(line); // 이벤트 해제
            }
        }

        private int GetPreviousCategoryId(int currentIndex)
        {
            for (int i = currentIndex - 1; i >= 0; i--)
            {
                if (Lines[i].IsHeadingLine && Lines[i].CategoryId > 0)
                {
                    return Lines[i].CategoryId;
                }
            }
            return 1; // 기본 카테고리
        }

        private void UpdateCurrentCategoryAfterDeletion()
        {
            // 가장 마지막 제목의 CategoryId를 현재 카테고리로 설정
            var lastHeading = Lines.LastOrDefault(l => l.IsHeadingLine && l.CategoryId > 0);
            if (lastHeading != null)
            {
                CurrentCategoryId = lastHeading.CategoryId;
            }
            else
            {
                // 제목이 없으면 기본 카테고리 사용
                CurrentCategoryId = 1;
            }

            Debug.WriteLine($"[DEBUG] 삭제 후 현재 카테고리: {CurrentCategoryId}");
        }

        public void InsertNewLineAt(int index)
        {
            if (index < 0 || index > Lines.Count)
                index = Lines.Count;

            int categoryId = DetermineCategoryIdForIndex(index);
            int displayOrder = GetDisplayOrderForIndex(index);

            var newLine = new MarkdownLineViewModel
            {
                IsEditing = true,
                SubjectId = this.SubjectId, // ✅ 현재 SubjectId 사용
                CategoryId = categoryId,
                Content = "",
                Index = index,
                DisplayOrder = displayOrder,
                TextId = 0
            };

            Debug.WriteLine($"[INSERT] 새 라인 삽입. Index: {index}, SubjectId: {this.SubjectId}, CategoryId: {categoryId}, DisplayOrder: {displayOrder}");

            Lines.Insert(index, newLine);
            RegisterLineEvents(newLine);
            UpdateLineIndicesFrom(index + 1);

            Debug.WriteLine($"[INSERT] 새 라인 삽입 완료");
        }

        private int DetermineCategoryIdForIndex(int index)
        {
            // 인덱스 이전의 가장 가까운 카테고리 찾기
            for (int i = index - 1; i >= 0; i--)
            {
                if (Lines[i].IsHeadingLine && Lines[i].CategoryId > 0)
                {
                    return Lines[i].CategoryId;
                }
                else if (!Lines[i].IsHeadingLine && Lines[i].CategoryId > 0)
                {
                    return Lines[i].CategoryId;
                }
            }

            // 찾을 수 없으면 이후 라인에서 찾기
            for (int i = index; i < Lines.Count; i++)
            {
                if (Lines[i].CategoryId > 0)
                {
                    return Lines[i].CategoryId;
                }
            }

            // 그래도 없으면 기본 카테고리
            return 1;
        }


        public void InsertNewLineAfter(MarkdownLineViewModel afterLine)
        {
            int insertIndex = Lines.IndexOf(afterLine) + 1;
            int insertDisplayOrder = afterLine.DisplayOrder;

            // 이후 라인들의 displayOrder 증가
            ShiftDisplayOrdersFrom(insertDisplayOrder + 1);

            // 새 라인 생성
            var newLine = new MarkdownLineViewModel
            {
                IsEditing = true,
                Content = "",
                SubjectId = this.SubjectId,
                CategoryId = afterLine.CategoryId,
                Index = insertIndex,
                DisplayOrder = insertDisplayOrder + 1,
                TextId = 0
            };

            Lines.Insert(insertIndex, newLine);

            // ✅ 수정: RegisterLineEvents 메서드 사용 (CategoryCreated 이벤트 제거)
            RegisterLineEvents(newLine);

            Debug.WriteLine($"[DEBUG] 새 라인 삽입 완료. Index: {insertIndex}, CategoryId: {afterLine.CategoryId}");
        }

        private void ShiftDisplayOrdersFrom(int fromOrder)
        {
            // 메모리에서 먼저 업데이트
            var linesToShift = Lines.Where(l => l.DisplayOrder >= fromOrder).ToList();
            foreach (var line in linesToShift)
            {
                line.DisplayOrder++;
                Debug.WriteLine($"[DEBUG] 라인 시프트: Content='{line.Content}', NewOrder={line.DisplayOrder}");
            }
        }



        /// <summary>
        /// 모든 라인의 인덱스를 재정렬
        /// </summary>
        private void UpdateLineIndices()
        {
            for (int i = 0; i < Lines.Count; i++)
            {
                Lines[i].Index = i;
            }
        }

        public void UpdateAllCategoryIds()
        {
            int currentCategoryId = 1; // 기본 카테고리

            foreach (var line in Lines)
            {
                if (line.IsHeadingLine && line.CategoryId > 0)
                {
                    currentCategoryId = line.CategoryId;
                }
                else if (!line.IsHeadingLine)
                {
                    line.CategoryId = currentCategoryId;
                }
            }

            CurrentCategoryId = currentCategoryId;
            Debug.WriteLine($"[DEBUG] 모든 라인의 CategoryId 업데이트 완료. 현재 카테고리: {CurrentCategoryId}");
        }

        // 변경된 라인만 저장
        public void SaveAllChanges()
        {
            try
            {
                // DisplayOrder가 변경된 라인도 포함
                var changedLines = Lines.Where(l =>
                    l.HasChanges ||
                    l.DisplayOrder != l.Index + 1 ||
                    (l.TextId > 0 && !l.IsHeadingLine) // 기존 텍스트도 확인
                ).ToList();

                if (!changedLines.Any())
                {
                    Debug.WriteLine("[SAVE] 변경사항 없음");
                    return;
                }

                Debug.WriteLine($"[SAVE] {changedLines.Count}개 라인 저장 시작");
                DebugPrintCurrentState();

                using var transaction = NoteRepository.BeginTransaction();
                try
                {
                    // 먼저 모든 DisplayOrder 업데이트
                    UpdateAllDisplayOrders(transaction);

                    foreach (var line in changedLines)
                    {
                        SaveLine(line, transaction);
                    }

                    transaction.Commit();
                    Debug.WriteLine($"[SAVE] 트랜잭션 커밋 완료");

                    // 저장 후 원본 상태 업데이트
                    foreach (var line in changedLines)
                    {
                        line.ResetChanges();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SAVE ERROR] 트랜잭션 실패, 롤백: {ex.Message}");
                    transaction.Rollback();
                    throw;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SAVE ERROR] 저장 실패: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void UpdateAllDisplayOrders(NoteRepository.Transaction transaction)
        {
            foreach (var line in Lines)
            {
                if (line.IsHeadingLine && line.CategoryId > 0)
                {
                    NoteRepository.UpdateCategoryDisplayOrder(line.CategoryId, line.DisplayOrder, transaction);
                }
                else if (!line.IsHeadingLine && line.TextId > 0)
                {
                    NoteRepository.UpdateLineDisplayOrder(line.TextId, line.DisplayOrder, transaction);
                }
            }
        }

        

        private void SaveLine(MarkdownLineViewModel line, NoteRepository.Transaction transaction)
        {
            Debug.WriteLine($"[SAVE] 라인 처리 - Content: {line.Content?.Substring(0, Math.Min(30, line.Content?.Length ?? 0))}, " +
                           $"IsHeading: {line.IsHeadingLine}, IsImage: {line.IsImage}, " +
                           $"CategoryId: {line.CategoryId}, TextId: {line.TextId}, " +
                           $"DisplayOrder: {line.DisplayOrder}");

            if (line.IsHeadingLine)
            {
                SaveHeading(line, transaction);
            }
            else
            {
                SaveContent(line, transaction);
            }
        }

        private void SaveHeading(MarkdownLineViewModel line, NoteRepository.Transaction transaction)
        {
            int? parentId = FindParentForHeading(line);
            int newLevel = NoteRepository.GetHeadingLevel(line.Content);

            if (line.CategoryId <= 0)
            {
                // 새 카테고리 생성
                int newCategoryId = NoteRepository.InsertCategory(
                    line.Content,
                    line.SubjectId,
                    line.DisplayOrder,
                    newLevel,
                    parentId,
                    transaction);
                line.CategoryId = newCategoryId;
                line.Level = newLevel;

                Debug.WriteLine($"[SAVE] 새 카테고리 생성됨: {newCategoryId}");

                // ✅ 새 제목 추가 후 하위 요소들 재구성
                UpdateSubsequentLinesAfterNewHeading(line, transaction);
            }
            else
            {
                // 기존 카테고리 업데이트
                bool levelChanged = line.Level != newLevel;

                NoteRepository.UpdateCategory(line.CategoryId, line.Content, transaction);
                NoteRepository.UpdateCategoryDisplayOrder(line.CategoryId, line.DisplayOrder, transaction);

                if (levelChanged)
                {
                    // 레벨 변경 시 부모 관계 재설정
                    NoteRepository.UpdateCategoryLevel(line.CategoryId, newLevel, transaction);
                    line.Level = newLevel;

                    // ✅ 제목 레벨 변경 후 모든 하위 요소들의 부모 관계 재구성
                    NoteRepository.UpdateSubsequentCategoryHierarchy(line.SubjectId, line.DisplayOrder, transaction);

                    Debug.WriteLine($"[SAVE] 카테고리 레벨 변경됨: {line.CategoryId}, 새 레벨: {newLevel}");
                }

                Debug.WriteLine($"[SAVE] 카테고리 업데이트됨: {line.CategoryId}");
            }
        }

        private void UpdateSubsequentLinesAfterNewHeading(MarkdownLineViewModel headingLine, NoteRepository.Transaction transaction)
        {
            try
            {
                int headingIndex = Lines.IndexOf(headingLine);
                if (headingIndex == -1) return;

                int currentCategoryId = headingLine.CategoryId;

                // 제목 다음부터 다른 제목이 나올 때까지 모든 라인의 CategoryId 업데이트
                for (int i = headingIndex + 1; i < Lines.Count; i++)
                {
                    var line = Lines[i];

                    // 다른 제목을 만나면 중단
                    if (line.IsHeadingLine) break;

                    // 일반 텍스트의 CategoryId 업데이트
                    if (line.CategoryId != currentCategoryId)
                    {
                        line.CategoryId = currentCategoryId;
                        line.OnPropertyChanged(nameof(line.CategoryId));

                        // 데이터베이스에도 반영 (TextId가 있는 경우)
                        if (line.TextId > 0)
                        {
                            NoteRepository.UpdateLineCategoryId(line.TextId, currentCategoryId, transaction);
                        }

                        Debug.WriteLine($"[SAVE] 라인 {i} CategoryId 업데이트: {currentCategoryId}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SAVE ERROR] UpdateSubsequentLinesAfterNewHeading 실패: {ex.Message}");
            }
        }

        private void SaveContent(MarkdownLineViewModel line, NoteRepository.Transaction transaction)
        {
            if (line.CategoryId <= 0)
            {
                Debug.WriteLine($"[SAVE ERROR] CategoryId가 유효하지 않음: {line.CategoryId}");
                line.CategoryId = GetCurrentCategoryIdForNewLine();
                Debug.WriteLine($"[SAVE] CategoryId 재설정: {line.CategoryId}");
            }

            if (line.TextId <= 0)
            {
                // 새 텍스트 생성
                int newTextId = NoteRepository.InsertNewLine(
                    line.Content,
                    line.SubjectId,
                    line.CategoryId,
                    line.DisplayOrder,
                    line.ContentType,
                    line.ImageUrl,
                    transaction);

                if (newTextId > 0)
                {
                    line.TextId = newTextId;
                    Debug.WriteLine($"[SAVE] 새 {line.ContentType} 생성됨: {newTextId}");
                }
            }
            else
            {
                // 기존 텍스트 업데이트
                NoteRepository.UpdateLine(line, transaction);
                NoteRepository.UpdateLineDisplayOrder(line.TextId, line.DisplayOrder, transaction);
                Debug.WriteLine($"[SAVE] {line.ContentType} 업데이트됨: {line.TextId}");
            }
        }


        /// <summary>
        /// 헤딩의 부모 카테고리 찾기
        /// </summary>
        private int? FindParentForHeading(MarkdownLineViewModel heading)
        {

            Debug.WriteLine($"[SAVE] 부모 찾는다 기다려라");

            int headingIndex = Lines.IndexOf(heading);

            for (int i = headingIndex - 1; i >= 0; i--)
            {
                Debug.WriteLine($"[SAVE] 부모 찾는 중이다 기다려라");

                var line = Lines[i];
                if (line.IsHeadingLine && line.Level < heading.Level && line.CategoryId > 0)
                {
                    Debug.WriteLine($"[SAVE] 부모 찾았다 임마 기다려라");

                    return line.CategoryId;
                }
            }

            Debug.WriteLine($"[SAVE] 부모 몬 찾았다 어어?");


            return null;
        }

        public void InsertImageLineAt(int index, string imagePath)
        {
            if (index < 0 || index > Lines.Count)
                index = Lines.Count;

            int categoryId = GetCurrentCategoryIdForNewLine();
            int displayOrder = GetDisplayOrderForIndex(index);

            var imageLine = new MarkdownLineViewModel
            {
                IsEditing = false,
                Content = $"![이미지]({imagePath})", // 마크다운 이미지 문법
                ImageUrl = imagePath,
                ContentType = "image",
                SubjectId = this.SubjectId,
                CategoryId = categoryId,
                Index = index,
                DisplayOrder = displayOrder,
                TextId = 0
            };

            Lines.Insert(index, imageLine);
            RegisterLineEvents(imageLine);

            // 이후 라인들의 Index와 DisplayOrder 업데이트
            UpdateLineIndicesFrom(index + 1);

            Debug.WriteLine($"[IMAGE] 이미지 라인 추가됨. Index: {index}, Path: {imagePath}");
        }

        private int GetDisplayOrderForIndex(int index)
        {
            if (index == 0)
                return 1;

            if (index >= Lines.Count)
                return Lines.Count > 0 ? Lines.Last().DisplayOrder + 1 : 1;

            // 이전 라인과 다음 라인 사이의 값
            int prevOrder = Lines[index - 1].DisplayOrder;
            int nextOrder = Lines[index].DisplayOrder;

            if (nextOrder - prevOrder > 1)
                return prevOrder + 1;

            // 공간이 없으면 이후 모든 라인들의 DisplayOrder를 밀어냄
            ShiftDisplayOrdersFrom(nextOrder);
            return nextOrder;
        }

        private void UpdateLineIndicesFrom(int startIndex)
        {
            for (int i = startIndex; i < Lines.Count; i++)
            {
                Lines[i].Index = i;
            }
        }


        public void ReorderLine(MarkdownLineViewModel draggedLine, MarkdownLineViewModel targetLine, bool insertBefore)
        {
            if (draggedLine == null || targetLine == null || draggedLine == targetLine)
                return;

            int draggedIndex = Lines.IndexOf(draggedLine);
            int targetIndex = Lines.IndexOf(targetLine);

            if (draggedIndex < 0 || targetIndex < 0)
                return;

            Debug.WriteLine($"[DRAG] 시작 - Dragged: {draggedIndex}, Target: {targetIndex}, InsertBefore: {insertBefore}");

            // 라인 제거
            Lines.RemoveAt(draggedIndex);

            // 새 위치 계산
            int newIndex = targetIndex;
            if (draggedIndex < targetIndex)
            {
                newIndex = insertBefore ? targetIndex - 1 : targetIndex;
            }
            else
            {
                newIndex = insertBefore ? targetIndex : targetIndex + 1;
            }

            // 라인 삽입
            Lines.Insert(newIndex, draggedLine);

            // 모든 라인의 DisplayOrder와 Index 재정렬
            ReorderAllLines();

            // 카테고리 재할당
            ReassignCategories();

            Debug.WriteLine($"[DRAG] 완료 - 이동: {draggedIndex} -> {newIndex}");
        }

        private void ReorderAllLines()
        {
            for (int i = 0; i < Lines.Count; i++)
            {
                var line = Lines[i];
                line.Index = i;
                line.DisplayOrder = i + 1;

                // 변경사항 표시
                if (!line.HasChanges)
                {
                    line.OnPropertyChanged(nameof(line.DisplayOrder));
                }

                Debug.WriteLine($"[REORDER] Index: {i}, DisplayOrder: {line.DisplayOrder}, Content: {line.Content?.Substring(0, Math.Min(20, line.Content?.Length ?? 0))}");
            }
        }

        private void ReassignCategories()
        {
            int currentCategoryId = 1; // 기본 카테고리

            for (int i = 0; i < Lines.Count; i++)
            {
                var line = Lines[i];

                if (line.IsHeadingLine)
                {
                    // 헤딩이면 현재 카테고리 업데이트
                    if (line.CategoryId > 0)
                    {
                        currentCategoryId = line.CategoryId;
                    }
                }
                else
                {
                    // 일반 텍스트나 이미지는 현재 카테고리에 할당
                    if (line.CategoryId != currentCategoryId)
                    {
                        Debug.WriteLine($"[REASSIGN] 라인 {i}의 CategoryId 변경: {line.CategoryId} -> {currentCategoryId}");
                        line.CategoryId = currentCategoryId;

                        // 변경사항 표시
                        if (!line.HasChanges)
                        {
                            line.OnPropertyChanged(nameof(line.CategoryId));
                        }
                    }
                }
            }
        }

        private void ReorderDisplayOrders()
        {
            for (int i = 0; i < Lines.Count; i++)
            {
                Lines[i].Index = i;
                Lines[i].DisplayOrder = i + 1;
            }

            // DisplayOrder 변경을 데이터베이스에 반영하기 위해 모든 라인을 변경됨으로 표시
            foreach (var line in Lines)
            {
                line.OnPropertyChanged(nameof(line.DisplayOrder));
            }
        }

        public void ForceFullSave()
        {
            try
            {
                Debug.WriteLine("[SAVE] 프로그램 종료 - 전체 저장 시작");

                // 모든 라인 저장 (변경사항 여부와 관계없이)
                using var transaction = NoteRepository.BeginTransaction();
                try
                {
                    // 모든 DisplayOrder 업데이트
                    UpdateAllDisplayOrders(transaction);

                    // 모든 라인 저장
                    foreach (var line in Lines)
                    {
                        if (line.IsHeadingLine && line.CategoryId > 0)
                        {
                            NoteRepository.UpdateCategory(line.CategoryId, line.Content, transaction);
                        }
                        else if (!line.IsHeadingLine)
                        {
                            if (line.TextId <= 0)
                            {
                                // 새 라인
                                SaveContent(line, transaction);
                            }
                            else
                            {
                                // 기존 라인
                                NoteRepository.UpdateLine(line, transaction);
                            }
                        }
                    }

                    transaction.Commit();
                    Debug.WriteLine("[SAVE] 프로그램 종료 - 전체 저장 완료");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SAVE ERROR] 프로그램 종료 저장 실패: {ex.Message}");
                    transaction.Rollback();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SAVE ERROR] ForceFullSave 실패: {ex.Message}");
            }
        }

        private void DebugPrintCurrentState()
        {
            Debug.WriteLine("=== 현재 에디터 상태 ===");
            Debug.WriteLine($"SubjectId: {SubjectId}");
            Debug.WriteLine($"CurrentCategoryId: {CurrentCategoryId}");
            Debug.WriteLine($"Lines 개수: {Lines.Count}");

            for (int i = 0; i < Lines.Count; i++)
            {
                var line = Lines[i];
                Debug.WriteLine($"[{i}] " +
                               $"Type: {(line.IsHeadingLine ? "HEADING" : line.IsImage ? "IMAGE" : "TEXT")}, " +
                               $"Content: '{line.Content?.Substring(0, Math.Min(30, line.Content?.Length ?? 0))}', " +
                               $"CategoryId: {line.CategoryId}, " +
                               $"TextId: {line.TextId}, " +
                               $"DisplayOrder: {line.DisplayOrder}, " +
                               $"HasChanges: {line.HasChanges}");

                if (line.IsImage)
                {
                    Debug.WriteLine($"     ImageUrl: {line.ImageUrl}");
                }
            }
            Debug.WriteLine("===================");
        }

        // 데이터 무결성 검증
        public void ValidateDataIntegrity()
        {
            Debug.WriteLine("=== 데이터 무결성 검증 ===");

            // 1. DisplayOrder 중복 검사
            var duplicateOrders = Lines.GroupBy(l => l.DisplayOrder)
                                       .Where(g => g.Count() > 1)
                                       .Select(g => g.Key);

            if (duplicateOrders.Any())
            {
                Debug.WriteLine($"[ERROR] DisplayOrder 중복 발견: {string.Join(", ", duplicateOrders)}");
            }

            // 2. CategoryId 검증
            int orphanedLines = 0;
            foreach (var line in Lines.Where(l => !l.IsHeadingLine))
            {
                if (line.CategoryId <= 0)
                {
                    Debug.WriteLine($"[ERROR] CategoryId가 없는 라인: Index={line.Index}, Content={line.Content?.Substring(0, 20)}");
                    orphanedLines++;
                }
            }

            if (orphanedLines > 0)
            {
                Debug.WriteLine($"[ERROR] 고아 라인 개수: {orphanedLines}");
            }

            // 3. 연속성 검증
            for (int i = 0; i < Lines.Count; i++)
            {
                if (Lines[i].Index != i)
                {
                    Debug.WriteLine($"[ERROR] Index 불일치: 실제={i}, 저장됨={Lines[i].Index}");
                }
            }

            Debug.WriteLine("===================");
        }

        // View가 닫힐 때 호출
        public void OnViewClosing()
        {
            try
            {
                _idleTimer?.Stop();

                // 타이머 활성 카테고리 해제
                if (_rightSidebarViewModel != null)
                {
                    _rightSidebarViewModel.ClearActiveCategory();
                    System.Diagnostics.Debug.WriteLine("[NoteEditor] 활성 카테고리 해제");
                }

                ForceFullSave(); // SaveAllChanges 대신 ForceFullSave 호출
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NoteEditor] View 닫기 처리 오류: {ex.Message}");
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string property = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        }
    }
}
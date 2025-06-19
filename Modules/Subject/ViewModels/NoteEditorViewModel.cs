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

            Debug.WriteLine($"[LOAD] NoteEditorViewModel 생성 시작. 카테고리 수: {loadedNotes?.Count ?? 0}");

            // 카테고리에서 평면적인 라인들 추출
            var allLines = new List<NoteLine>();

            if (loadedNotes != null && loadedNotes.Count > 0)
            {
                foreach (var category in loadedNotes)
                {
                    if (category.Lines != null)
                    {
                        allLines.AddRange(category.Lines);
                        Debug.WriteLine($"[LOAD] 카테고리 '{category.Title}'에서 {category.Lines.Count}개 라인 추출");
                    }
                }
            }

            // DisplayOrder 기준으로 정렬
            allLines = allLines.OrderBy(line => line.DisplayOrder).ToList();
            Debug.WriteLine($"[LOAD] 총 {allLines.Count}개 라인을 DisplayOrder 순으로 정렬");

            // 평면적인 MarkdownLineViewModel 생성
            if (allLines.Count > 0)
            {
                for (int i = 0; i < allLines.Count; i++)
                {
                    var noteLine = allLines[i];

                    var markdownLine = new MarkdownLineViewModel
                    {
                        Content = noteLine.Content ?? "",
                        ContentType = noteLine.ContentType ?? "text",
                        ImageUrl = noteLine.ImageUrl,
                        IsEditing = false,
                        SubjectId = this.SubjectId,
                        CategoryId = noteLine.CategoryId,
                        TextId = noteLine.TextId,
                        DisplayOrder = noteLine.DisplayOrder,
                        Index = i
                    };

                    // 제목인지 확인 (# 으로 시작하거나 ContentType이 heading인 경우)
                    if (noteLine.ContentType == "heading" || noteLine.Content?.StartsWith("#") == true)
                    {
                        markdownLine.IsHeadingLine = true;
                        // # 개수로 레벨 결정
                        var content = noteLine.Content ?? "";
                        int level = 1;
                        while (level < content.Length && content[level - 1] == '#')
                        {
                            level++;
                        }
                        level = Math.Max(1, level - 1); // 최소 1레벨
                        markdownLine.Level = level;

                        Debug.WriteLine($"[LOAD] 제목 라인: '{content}' (Level: {level}, CategoryId: {noteLine.CategoryId})");
                    }

                    markdownLine.SetOriginalContent(noteLine.Content ?? "");
                    Lines.Add(markdownLine);
                    RegisterLineEvents(markdownLine);
                }

                _nextDisplayOrder = allLines.Max(l => l.DisplayOrder) + 1;
                Debug.WriteLine($"[LOAD] 라인 로딩 완료. 다음 DisplayOrder: {_nextDisplayOrder}");

                // CurrentCategoryId 설정 (첫 번째 유효한 CategoryId 사용)
                var firstLineWithCategory = Lines.FirstOrDefault(l => l.CategoryId > 0);
                if (firstLineWithCategory != null)
                {
                    CurrentCategoryId = firstLineWithCategory.CategoryId;
                    Debug.WriteLine($"[LOAD] CurrentCategoryId 설정: {CurrentCategoryId}");
                }
                else
                {
                    CurrentCategoryId = 1; // 기본값
                    Debug.WriteLine($"[LOAD] CurrentCategoryId 기본값 설정: {CurrentCategoryId}");
                }
            }
            else
            {
                Debug.WriteLine("[LOAD] 로드된 데이터 없음. 빈 라인 추가.");
                AddInitialEmptyLine();
            }

            Lines.CollectionChanged += Lines_CollectionChanged;
            Debug.WriteLine($"[LOAD] NoteEditorViewModel 생성 완료. 총 라인 수: {Lines.Count}");
        }

        private int GetSafeCategoryId(int requestedCategoryId)
        {
            // Category 테이블에 해당 ID가 있는지 확인
            try
            {
                using var conn = Notea.Modules.Common.Helpers.DatabaseHelper.Instance.GetConnection();
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM category WHERE categoryId = @categoryId AND subjectId = @subjectId";
                cmd.Parameters.AddWithValue("@categoryId", requestedCategoryId);
                cmd.Parameters.AddWithValue("@subjectId", this.SubjectId);

                var count = Convert.ToInt32(cmd.ExecuteScalar());

                if (count > 0)
                {
                    return requestedCategoryId; // 유효한 CategoryId
                }
                else
                {
                    Debug.WriteLine($"[SAFE] CategoryId {requestedCategoryId}가 존재하지 않음. 기본 카테고리 생성 또는 0 반환");
                    return 0; // 유효하지 않은 경우 0 반환 (저장 시 처리)
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SAFE ERROR] CategoryId 유효성 검사 실패: {ex.Message}");
                return 0;
            }
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
                // 실제 변경사항이 있는 경우만 저장
                var hasRealChanges = Lines.Any(l => l.HasChanges);
                if (hasRealChanges)
                {
                    UpdateActivity();
                    SaveAllChanges();
                }
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
            if (CurrentCategoryId > 0)
            {
                return CurrentCategoryId;
            }

            var lastHeading = Lines.LastOrDefault(l => l.IsHeadingLine && l.CategoryId > 0);
            if (lastHeading != null)
            {
                CurrentCategoryId = lastHeading.CategoryId;
                return CurrentCategoryId;
            }

            Debug.WriteLine("[DEBUG] 제목이 없음 - CategoryId 0 반환 (저장 시 0으로 저장됨)");
            return 0;
        }

        private void OnLinePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MarkdownLineViewModel.CategoryId))
            {
                var line = sender as MarkdownLineViewModel;
                if (line != null && line.CategoryId > 0)
                {
                    try
                    {
                        // CategoryId 유효성 검사
                        var validCategoryId = NoteRepository.GetValidCategoryId(this.SubjectId, line.CategoryId);

                        if (validCategoryId != line.CategoryId)
                        {
                            line.CategoryId = validCategoryId;
                            Debug.WriteLine($"[NoteEditor] CategoryId 보정됨: {validCategoryId}");
                        }

                        CurrentCategoryId = validCategoryId;
                        UpdateTimerCategory();
                        Debug.WriteLine($"[NoteEditor] 활동 업데이트: CategoryId={CurrentCategoryId}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[NoteEditor] 활동 업데이트 오류: {ex.Message}");
                    }
                }
            }

            // 일반적인 변경사항 처리
            if (e.PropertyName == nameof(MarkdownLineViewModel.Content) ||
                e.PropertyName == nameof(MarkdownLineViewModel.ContentType))
            {
                _lastActivityTime = DateTime.Now;
                _idleTimer?.Stop();
                _idleTimer?.Start();

                // ✅ 실제 프로젝트의 활동 업데이트 메소드 사용
                UpdateActivity();
            }
        }

        private void UpdateTimerCategory()
        {
            try
            {
                if (_rightSidebarViewModel != null && CurrentCategoryId > 0)
                {
                    // CategoryId 유효성 재확인
                    var validCategoryId = NoteRepository.GetValidCategoryId(this.SubjectId, CurrentCategoryId);

                    if (validCategoryId > 0)
                    {
                        var subjectName = GetSubjectNameById(SubjectId);
                        if (!string.IsNullOrEmpty(subjectName))
                        {
                            _rightSidebarViewModel.SetActiveCategory(validCategoryId, subjectName);
                            Debug.WriteLine($"[Timer] 활성 카테고리 설정: CategoryId={validCategoryId}, Subject={subjectName}");
                            Debug.WriteLine($"[포커스] 카테고리 포커스 및 타이머 연동 완료: CategoryId={validCategoryId}, Subject={subjectName}");
                        }
                        else
                        {
                            Debug.WriteLine($"[Timer] 과목명을 찾을 수 없음: SubjectId={SubjectId}");
                        }
                    }
                    else
                    {
                        Debug.WriteLine($"[Timer] 유효하지 않은 CategoryId: {CurrentCategoryId}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Timer ERROR] UpdateTimerCategory 실패: {ex.Message}");
            }
        }

        public void ClearCategoryFocus()
        {
            try
            {
                if (_rightSidebarViewModel != null)
                {
                    _rightSidebarViewModel.ClearActiveCategory();
                    Debug.WriteLine($"[Timer] 활성 카테고리 해제");
                    Debug.WriteLine($"[포커스] 카테고리 포커스 해제 및 타이머 연동: CategoryId={CurrentCategoryId}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Timer ERROR] ClearCategoryFocus 실패: {ex.Message}");
            }
        }



        private void HandleHeadingStatusChange(MarkdownLineViewModel line, bool wasHeading, bool isHeading)
        {
            try
            {
                // DisplayOrder 보존을 위해 현재 값 저장
                int originalDisplayOrder = line.DisplayOrder;

                if (wasHeading && !isHeading)
                {
                    Debug.WriteLine($"[DEBUG] 제목에서 일반 텍스트로 변경됨: {line.Content}");

                    if (line.CategoryId <= 1)
                    {
                        Debug.WriteLine("[WARNING] 기본 카테고리는 삭제할 수 없습니다. 텍스트로만 변경됩니다.");
                        line.IsHeadingLine = false;
                        line.Level = 0;
                        line.TextId = 0; // 새로운 텍스트로 생성
                        line.DisplayOrder = originalDisplayOrder; // DisplayOrder 보존
                        return;
                    }

                    int previousCategoryId = FindPreviousCategoryId(line);

                    if (previousCategoryId > 0)
                    {
                        NoteRepository.ReassignTextsToCategory(line.CategoryId, previousCategoryId);
                    }

                    NoteRepository.DeleteCategory(line.CategoryId);

                    line.IsHeadingLine = false;
                    line.CategoryId = previousCategoryId > 0 ? previousCategoryId : 0;
                    line.TextId = 0; // 새로운 텍스트로 생성
                    line.Level = 0;
                    line.DisplayOrder = originalDisplayOrder; // DisplayOrder 보존

                    if (CurrentCategoryId == line.CategoryId)
                    {
                        CurrentCategoryId = previousCategoryId > 0 ? previousCategoryId : 0;
                    }

                    ReassignSubsequentLines(line, previousCategoryId > 0 ? previousCategoryId : 0);
                }
                else if (!wasHeading && isHeading)
                {
                    Debug.WriteLine($"[DEBUG] 일반 텍스트에서 제목으로 변경됨: {line.Content}");
                    Debug.WriteLine($"[DEBUG] 변경 전 DisplayOrder: {line.DisplayOrder}");

                    if (line.TextId > 0)
                    {
                        NoteRepository.DeleteLine(line.TextId);
                        line.TextId = 0;
                    }

                    line.IsHeadingLine = true;
                    line.Level = NoteRepository.GetHeadingLevel(line.Content);
                    line.CategoryId = 0; // 새로운 카테고리로 생성 (저장 시점에 처리)
                    line.DisplayOrder = originalDisplayOrder; // DisplayOrder 보존

                    ScheduleSubsequentCategoryUpdate(line);

                    Debug.WriteLine($"[DEBUG] 텍스트→제목 변환 완료. DisplayOrder 유지: {line.DisplayOrder}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERROR] HandleHeadingStatusChange 실패: {ex.Message}");
                throw;
            }
        }

        private void ScheduleSubsequentCategoryUpdate(MarkdownLineViewModel newHeadingLine)
        {
            try
            {
                // 저장 시점에 실제로 처리되도록 HasChanges 플래그 설정
                newHeadingLine.OnPropertyChanged(nameof(newHeadingLine.HasChanges));

                // 이후 라인들도 변경 필요 표시
                int headingIndex = Lines.IndexOf(newHeadingLine);
                if (headingIndex >= 0)
                {
                    for (int i = headingIndex + 1; i < Lines.Count; i++)
                    {
                        var line = Lines[i];

                        // 다음 제목을 만나면 중단
                        if (line.IsHeadingLine) break;

                        // 일반 텍스트의 변경 필요 표시
                        line.OnPropertyChanged(nameof(line.HasChanges));
                        Debug.WriteLine($"[SCHEDULE] 라인 {i} CategoryId 업데이트 예약됨");
                    }
                }

                Debug.WriteLine($"[SCHEDULE] 제목 변경 후 이후 텍스트들의 CategoryId 업데이트 예약 완료");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERROR] ScheduleSubsequentCategoryUpdate 실패: {ex.Message}");
            }
        }

        private void ReassignSubsequentLines(MarkdownLineViewModel changedLine, int newCategoryId)
        {
            try
            {
                int lineIndex = Lines.IndexOf(changedLine);

                Debug.WriteLine($"[REASSIGN] 라인 {lineIndex} 이후 텍스트들을 CategoryId {newCategoryId}로 재할당 시작");

                for (int i = lineIndex + 1; i < Lines.Count; i++)
                {
                    var subsequentLine = Lines[i];

                    // 다음 제목을 만나면 중단 (해당 제목이 관리하는 영역이므로)
                    if (subsequentLine.IsHeadingLine && subsequentLine.Level <= changedLine.Level)
                    {
                        Debug.WriteLine($"[REASSIGN] 라인 {i}에서 동일/상위 레벨 제목 발견, 재할당 중단");
                        break;
                    }

                    // 일반 텍스트라면 카테고리 재할당
                    if (!subsequentLine.IsHeadingLine)
                    {
                        subsequentLine.CategoryId = newCategoryId; // ✅ 0도 허용
                        subsequentLine.OnPropertyChanged(nameof(subsequentLine.CategoryId));

                        // 데이터베이스에도 반영 (TextId가 있는 경우)
                        if (subsequentLine.TextId > 0)
                        {
                            NoteRepository.UpdateLineCategoryId(subsequentLine.TextId, newCategoryId);
                        }

                        Debug.WriteLine($"[REASSIGN] 라인 {i} CategoryId 재할당: TextId={subsequentLine.TextId}, 새 CategoryId={newCategoryId}");
                    }
                }

                Debug.WriteLine($"[REASSIGN] 라인 재할당 완료");
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
                        Debug.WriteLine($"[FIND] 이전 카테고리 발견: CategoryId={Lines[i].CategoryId}");
                        return Lines[i].CategoryId;
                    }
                }

                // 이전 제목이 없으면 데이터베이스에서 찾기
                int dbCategoryId = NoteRepository.FindPreviousCategoryIdByDisplayOrder(SubjectId, currentLine.DisplayOrder);

                if (dbCategoryId > 0)
                {
                    Debug.WriteLine($"[FIND] 데이터베이스에서 이전 카테고리 발견: CategoryId={dbCategoryId}");
                    return dbCategoryId;
                }

                // ✅ 수정: 이전 카테고리가 없으면 0 반환 (기존에는 1을 반환했음)
                Debug.WriteLine($"[FIND] 이전 카테고리 없음 - 0 반환");
                return 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERROR] FindPreviousCategoryId 실패: {ex.Message}");
                return 0; // ✅ 에러 시에도 0 반환
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
            try
            {
                var categoryId = DetermineCategoryIdForIndex(index);

                // CategoryId 유효성 검사 및 보정
                categoryId = NoteRepository.GetValidCategoryId(this.SubjectId, categoryId);

                var displayOrder = GetDisplayOrderForIndex(index);

                Debug.WriteLine($"[INSERT] 새 라인 삽입. Index: {index}, SubjectId: {this.SubjectId}, CategoryId: {categoryId}, DisplayOrder: {displayOrder}");

                ShiftDisplayOrdersFrom(displayOrder);

                var newLine = new MarkdownLineViewModel
                {
                    IsEditing = true,
                    Content = "",
                    SubjectId = this.SubjectId,
                    CategoryId = categoryId,
                    Index = index,
                    DisplayOrder = displayOrder,
                    TextId = 0
                };

                Lines.Insert(index, newLine);
                RegisterLineEvents(newLine);
                UpdateLineIndicesFrom(index + 1);

                Debug.WriteLine($"[INSERT] 새 라인 삽입 완료");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[INSERT ERROR] InsertNewLineAt 실패: {ex.Message}");
            }
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
        private static readonly object _saveLock = new object();
        // 변경된 라인만 저장
        public void SaveAllChanges()
        {
            lock (_saveLock) // 동시 저장 방지
            {
                try
                {
                    var editingLine = Lines.FirstOrDefault(l => l.IsEditing);
                    if (editingLine != null)
                    {
                        Debug.WriteLine("[SAVE] 편집 중인 라인 감지 - 저장 지연");
                        return; // 편집이 완료될 때까지 저장 지연
                    }

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
        }

        private void UpdateAllDisplayOrders(NoteRepository.Transaction transaction)
        {
            var linesToUpdate = new HashSet<int>(); // 중복 방지

            foreach (var line in Lines)
            {
                if (line.IsHeadingLine && line.CategoryId > 0)
                {
                    if (!linesToUpdate.Contains(line.CategoryId))
                    {
                        NoteRepository.UpdateCategoryDisplayOrder(line.CategoryId, line.DisplayOrder, transaction);
                        linesToUpdate.Add(line.CategoryId);
                    }
                }
                else if (!line.IsHeadingLine && line.TextId > 0)
                {
                    if (!linesToUpdate.Contains(line.TextId))
                    {
                        NoteRepository.UpdateLineDisplayOrder(line.TextId, line.DisplayOrder, transaction);
                        linesToUpdate.Add(line.TextId);
                    }
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

            // DisplayOrder 보존 확인
            Debug.WriteLine($"[SAVE] 헤딩 저장 시작 - DisplayOrder: {line.DisplayOrder}, Content: {line.Content}");

            if (line.CategoryId <= 0)
            {
                // 새 카테고리 생성 - DisplayOrder 명시적 전달
                int newCategoryId = NoteRepository.InsertCategory(
                    line.Content,
                    line.SubjectId,
                    line.DisplayOrder,  // 현재 DisplayOrder 유지
                    newLevel,
                    parentId,
                    transaction);
                line.CategoryId = newCategoryId;
                line.Level = newLevel;

                Debug.WriteLine($"[SAVE] 새 카테고리 생성됨: CategoryId={newCategoryId}, DisplayOrder={line.DisplayOrder}");

                UpdateSubsequentLinesAfterNewHeading(line, transaction);
            }
            else
            {
                // 기존 카테고리 업데이트
                bool levelChanged = line.Level != newLevel;

                NoteRepository.UpdateCategory(line.CategoryId, line.Content, transaction);
                NoteRepository.UpdateCategoryDisplayOrder(line.CategoryId, line.DisplayOrder, transaction);

                Debug.WriteLine($"[SAVE] 카테고리 업데이트 - CategoryId: {line.CategoryId}, DisplayOrder: {line.DisplayOrder}");

                if (levelChanged)
                {
                    // 레벨 변경 시 부모 관계 재설정
                    NoteRepository.UpdateCategoryLevel(line.CategoryId, newLevel, transaction);
                    line.Level = newLevel;

                    // 제목 레벨 변경 후 모든 하위 요소들의 부모 관계 재구성
                    NoteRepository.UpdateSubsequentCategoryHierarchy(line.SubjectId, line.DisplayOrder, transaction);

                    Debug.WriteLine($"[SAVE] 카테고리 레벨 변경됨: {line.CategoryId}, 새 레벨: {newLevel}, DisplayOrder 유지: {line.DisplayOrder}");
                }
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
            int headingIndex = Lines.IndexOf(heading);
            int targetLevel = NoteRepository.GetHeadingLevel(heading.Content);

            // 효율적인 부모 찾기
            for (int i = headingIndex - 1; i >= 0; i--)
            {
                var line = Lines[i];
                if (line.IsHeadingLine && line.Level < targetLevel && line.CategoryId > 0)
                {
                    Debug.WriteLine($"[SAVE] 부모 카테고리 발견: CategoryId={line.CategoryId}, Level={line.Level}");
                    return line.CategoryId;
                }
            }

            Debug.WriteLine($"[SAVE] 최상위 레벨 카테고리 - 부모 없음");
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

            // ✅ 드래그 전 상태 백업 (안전장치)
            var backupLines = new List<(MarkdownLineViewModel line, int originalIndex, int originalDisplayOrder, int originalCategoryId)>();
            for (int i = 0; i < Lines.Count; i++)
            {
                var line = Lines[i];
                backupLines.Add((line, line.Index, line.DisplayOrder, line.CategoryId));
            }

            try
            {
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
                ReorderAllLinesWithSave();

                // 카테고리 재할당
                ReassignCategoriesWithSave();

                // ✅ 즉시 저장 실행
                ForceImmediateSave();

                Debug.WriteLine($"[DRAG] 완료 및 저장됨 - 이동: {draggedIndex} -> {newIndex}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DRAG ERROR] 드래그 실패, 원상복구 시도: {ex.Message}");

                try
                {
                    RestoreFromBackup(backupLines);
                    Debug.WriteLine("[DRAG] 원상복구 완료");
                }
                catch (Exception restoreEx)
                {
                    Debug.WriteLine($"[DRAG ERROR] 원상복구 실패: {restoreEx.Message}");
                }

                throw;
            }
        }

        private void ReorderAllLinesWithSave()
        {
            for (int i = 0; i < Lines.Count; i++)
            {
                var line = Lines[i];
                int newDisplayOrder = i + 1;

                if (line.DisplayOrder != newDisplayOrder || line.Index != i)
                {
                    line.Index = i;
                    line.DisplayOrder = newDisplayOrder;

                    line.OnPropertyChanged(nameof(line.DisplayOrder));
                    line.OnPropertyChanged(nameof(line.HasChanges));

                    Debug.WriteLine($"[REORDER] Index: {i}, DisplayOrder: {line.DisplayOrder}, Content: {line.Content?.Substring(0, Math.Min(20, line.Content?.Length ?? 0))}");
                }
            }
        }

        private void ReassignCategoriesWithSave()
        {
            int currentCategoryId = 1; // 기본 카테고리

            for (int i = 0; i < Lines.Count; i++)
            {
                var line = Lines[i];

                if (line.IsHeadingLine)
                {
                    if (line.CategoryId > 0)
                    {
                        currentCategoryId = line.CategoryId;
                    }
                }
                else
                {
                    if (line.CategoryId != currentCategoryId)
                    {
                        Debug.WriteLine($"[REASSIGN] 라인 {i}의 CategoryId 변경: {line.CategoryId} -> {currentCategoryId}");
                        line.CategoryId = currentCategoryId;

                        line.OnPropertyChanged(nameof(line.CategoryId));
                        line.OnPropertyChanged(nameof(line.HasChanges));
                    }
                }
            }
        }

        private void ForceImmediateSave()
        {
            try
            {
                Debug.WriteLine("[DRAG] 드래그 완료 - 즉시 저장 시작");

                // IdleTimer 정지 (중복 저장 방지)
                _idleTimer?.Stop();

                // 모든 변경사항 즉시 저장
                SaveAllChanges();

                // IdleTimer 재시작
                _idleTimer?.Start();

                Debug.WriteLine("[DRAG] 드래그 완료 - 즉시 저장 완료");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DRAG ERROR] 즉시 저장 실패: {ex.Message}");
                throw;
            }
        }

        private void RestoreFromBackup(List<(MarkdownLineViewModel line, int originalIndex, int originalDisplayOrder, int originalCategoryId)> backup)
        {
            try
            {
                // Lines 컬렉션 완전 재구성
                var restoredLines = new List<MarkdownLineViewModel>();

                // 원래 순서대로 정렬
                var sortedBackup = backup.OrderBy(b => b.originalIndex).ToList();

                foreach (var (line, originalIndex, originalDisplayOrder, originalCategoryId) in sortedBackup)
                {
                    line.Index = originalIndex;
                    line.DisplayOrder = originalDisplayOrder;
                    line.CategoryId = originalCategoryId;
                    restoredLines.Add(line);
                }

                // Lines 컬렉션 교체
                Lines.Clear();
                foreach (var line in restoredLines)
                {
                    Lines.Add(line);
                }

                Debug.WriteLine($"[RESTORE] {restoredLines.Count}개 라인 원상복구 완료");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RESTORE ERROR] 복원 실패: {ex.Message}");
                throw;
            }
        }



        private void ReorderAllLines()
        {
            ReorderAllLinesWithSave();
        }

        private void ReassignCategories()
        {
            ReassignCategoriesWithSave();
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

        public void ValidateHeadingConversion(int lineIndex)
        {
            if (lineIndex < 0 || lineIndex >= Lines.Count)
            {
                Debug.WriteLine($"[VALIDATE ERROR] 유효하지 않은 라인 인덱스: {lineIndex}");
                return;
            }

            var line = Lines[lineIndex];
            Debug.WriteLine($"\n=== 헤딩 변환 검증 시작 (라인 {lineIndex + 1}) ===");
            Debug.WriteLine($"변환 전 상태:");
            Debug.WriteLine($"  - Content: '{line.Content}'");
            Debug.WriteLine($"  - IsHeadingLine: {line.IsHeadingLine}");
            Debug.WriteLine($"  - CategoryId: {line.CategoryId}");
            Debug.WriteLine($"  - TextId: {line.TextId}");
            Debug.WriteLine($"  - DisplayOrder: {line.DisplayOrder}");
            Debug.WriteLine($"  - Index: {line.Index}");

            // displayOrder가 Index + 1과 일치하는지 확인
            if (line.DisplayOrder != line.Index + 1)
            {
                Debug.WriteLine($"[WARNING] DisplayOrder와 Index 불일치! DisplayOrder: {line.DisplayOrder}, Expected: {line.Index + 1}");
            }
        }

        public void TrackTextChange(MarkdownLineViewModel line, string oldContent, string newContent)
        {
            if (oldContent != newContent)
            {
                Debug.WriteLine($"\n[TEXT CHANGE] 텍스트 변경 감지:");
                Debug.WriteLine($"  - Line Index: {Lines.IndexOf(line) + 1}");
                Debug.WriteLine($"  - Old: '{oldContent}'");
                Debug.WriteLine($"  - New: '{newContent}'");
                Debug.WriteLine($"  - DisplayOrder: {line.DisplayOrder}");

                // 헤딩으로 변환되는지 확인
                bool wasHeading = NoteRepository.IsMarkdownHeading(oldContent);
                bool isHeading = NoteRepository.IsMarkdownHeading(newContent);

                if (!wasHeading && isHeading)
                {
                    Debug.WriteLine($"  - [HEADING CONVERSION] 일반 텍스트 → 헤딩으로 변환됨!");
                    Debug.WriteLine($"  - 헤딩 레벨: {NoteRepository.GetHeadingLevel(newContent)}");
                }
                else if (wasHeading && !isHeading)
                {
                    Debug.WriteLine($"  - [HEADING REMOVAL] 헤딩 → 일반 텍스트로 변환됨!");
                }
            }
        }

        public void ValidateAfterReload()
        {
            Debug.WriteLine($"\n=== 데이터 재로드 후 검증 ===");
            Debug.WriteLine($"총 라인 수: {Lines.Count}");

            // DisplayOrder 순서 검증
            var sortedByDisplayOrder = Lines.OrderBy(l => l.DisplayOrder).ToList();
            bool isOrderCorrect = true;

            for (int i = 0; i < Lines.Count; i++)
            {
                if (Lines[i] != sortedByDisplayOrder[i])
                {
                    isOrderCorrect = false;
                    Debug.WriteLine($"[ERROR] DisplayOrder 정렬 오류! Index {i}: " +
                                  $"Expected DisplayOrder {sortedByDisplayOrder[i].DisplayOrder}, " +
                                  $"Actual DisplayOrder {Lines[i].DisplayOrder}");
                }
            }

            if (isOrderCorrect)
            {
                Debug.WriteLine("[OK] DisplayOrder 정렬 검증 통과");
            }

            // 헤딩 변환 확인
            foreach (var line in Lines.Where(l => l.IsHeadingLine))
            {
                Debug.WriteLine($"\n[HEADING] CategoryId: {line.CategoryId}");
                Debug.WriteLine($"  - Content: '{line.Content}'");
                Debug.WriteLine($"  - DisplayOrder: {line.DisplayOrder}");
                Debug.WriteLine($"  - Level: {line.Level}");
                Debug.WriteLine($"  - Line Index: {Lines.IndexOf(line) + 1}");
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string property = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        }
    }
}
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Security.Cryptography;
using System.Windows.Input;
using Notea.Modules.Common.Helpers;
using Notea.Modules.Daily.Models;
using Notea.Modules.Daily.ViewModels;
using Notea.Modules.Subjects.ViewModels;
using Notea.ViewModels;

namespace Notea.Modules.Daily.ViewModels
{
    public class DailyBodyViewModel : ViewModelBase
    {
        // 과목 리스트 - 공유 데이터 또는 자체 데이터
        private ObservableCollection<SubjectProgressViewModel> _subjects;
        public ObservableCollection<SubjectProgressViewModel> Subjects
        {
            get => _subjects;
            set
            {
                if (_subjects != null)
                {
                    _subjects.CollectionChanged -= Subjects_CollectionChanged;
                }

                _subjects = value;

                if (_subjects != null)
                {
                    _subjects.CollectionChanged += Subjects_CollectionChanged;
                }

                OnPropertyChanged(nameof(Subjects));
            }
        }

        private bool _isLoadingSubjects = false;
        private bool _isLoadingFromDatabase = false;
        private bool _hasLoadedOnce = false; // 초기 로드 완료 플래그

        // TODO 리스트
        public ObservableCollection<TodoItem> TodoList { get; set; }

        // 싱글톤 DB 헬퍼 사용
        private readonly DatabaseHelper _db = DatabaseHelper.Instance;

        // 새 할 일 텍스트
        private string _newTodoText;
        public string NewTodoText
        {
            get => _newTodoText;
            set => SetProperty(ref _newTodoText, value);
        }

        // 입력 모드 여부
        private bool _isAdding = false;
        public bool IsAdding
        {
            get => _isAdding;
            set => SetProperty(ref _isAdding, value);
        }

        // 포커스 요청용 이벤트 (View에서 연결)
        public Action? RequestFocusOnInput { get; set; }

        public ICommand AddTodoCommand { get; }
        public ICommand StartAddCommand { get; }
        public ICommand DeleteTodoCommand { get; }

        public DailyBodyViewModel(DateTime appStartDate, bool skipInitialLoad = false)
        {
            System.Diagnostics.Debug.WriteLine($"[DailyBodyViewModel] 생성자 호출 - skipInitialLoad: {skipInitialLoad}");

            SelectedDate = appStartDate;

            // 기본 컬렉션으로 시작 (나중에 공유 데이터로 교체됨)
            Subjects = new ObservableCollection<SubjectProgressViewModel>();
            TodoList = new ObservableCollection<TodoItem>();

            // Commands 초기화
            AddTodoCommand = new RelayCommand(AddTodo);
            StartAddCommand = new RelayCommand(() =>
            {
                IsAdding = true;
                RequestFocusOnInput?.Invoke();
            });
            DeleteTodoCommand = new RelayCommand<TodoItem>(DeleteTodo);

            System.Diagnostics.Debug.WriteLine("[DailyBodyViewModel] Commands 초기화 완료");

            // ✅ 초기 로딩 스킵 옵션 강화
            if (!skipInitialLoad)
            {
                System.Diagnostics.Debug.WriteLine("[DailyBodyViewModel] 초기 데이터 로딩 시작");
                LoadDailyDataSafe(SelectedDate);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[DailyBodyViewModel] 초기 데이터 로딩 스킵됨");
            }

            System.Diagnostics.Debug.WriteLine("[DailyBodyViewModel] 생성자 완료");
        }

        public void InitializeDataWhenReady()
        {
            System.Diagnostics.Debug.WriteLine($"[DailyBodyViewModel] InitializeDataWhenReady 호출 - _hasLoadedOnce: {_hasLoadedOnce}");

            if (_hasLoadedOnce)
            {
                System.Diagnostics.Debug.WriteLine("[DailyBodyViewModel] 이미 로드 완료됨 - 스킵");
                return;
            }

            System.Diagnostics.Debug.WriteLine("[DailyBodyViewModel] 지연 데이터 초기화 시작");
            LoadDailyDataSafe(SelectedDate);
        }

        public void LoadDailyDataSafe(DateTime date)
        {
            System.Diagnostics.Debug.WriteLine($"[DailyBodyViewModel] LoadDailyDataSafe 호출 - 날짜: {date.ToShortDateString()}");

            try
            {
                // 중복 로딩 방지 강화
                if (SelectedDate.Date == date.Date && _hasLoadedOnce && (TodoList?.Count > 0 || !string.IsNullOrEmpty(Comment)))
                {
                    System.Diagnostics.Debug.WriteLine("[DailyBodyViewModel] 이미 로드된 데이터 존재 - 안전한 로딩 스킵");
                    return;
                }

                // 로딩 플래그 설정
                _isLoadingFromDatabase = true;

                try
                {
                    SelectedDate = date;

                    // ===== Phase 1: Comment 로딩 =====
                    try
                    {
                        Comment = _db.GetCommentByDate(date);
                        System.Diagnostics.Debug.WriteLine($"[DailyBodyViewModel] Comment 로드 완료: '{Comment}'");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[DailyBodyViewModel] Comment 로드 오류: {ex.Message}");
                        Comment = string.Empty;
                    }

                    // ===== Phase 2: TodoList 로딩 =====
                    try
                    {
                        foreach (var todo in TodoList)
                        {
                            todo.PropertyChanged -= Todo_PropertyChanged;
                        }

                        TodoList.Clear();
                        var todos = _db.GetTodosByDate(date);

                        System.Diagnostics.Debug.WriteLine($"[DailyBodyViewModel] DB에서 {todos.Count}개 Todo 로드됨");

                        foreach (var todo in todos)
                        {
                            todo.PropertyChanged += Todo_PropertyChanged;
                            TodoList.Add(todo);
                        }

                        System.Diagnostics.Debug.WriteLine($"[DailyBodyViewModel] TodoList에 {TodoList.Count}개 항목 추가됨");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[DailyBodyViewModel] TodoList 로드 오류: {ex.Message}");
                    }

                    // ===== Phase 3: Subject 데이터 로딩 =====
                    try
                    {
                        System.Diagnostics.Debug.WriteLine("[DailyBodyViewModel] Subject 데이터 로딩 시작");
                        LoadDailySubjects(date);
                        System.Diagnostics.Debug.WriteLine("[DailyBodyViewModel] Subject 데이터 로딩 완료");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[DailyBodyViewModel] Subject 로딩 오류: {ex.Message}");
                    }

                    _hasLoadedOnce = true;
                    System.Diagnostics.Debug.WriteLine("[DailyBodyViewModel] 전체 데이터 로딩 완료");
                }
                finally
                {
                    _isLoadingFromDatabase = false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DailyBodyViewModel] LoadDailyDataSafe 전체 오류: {ex.Message}");
                _isLoadingFromDatabase = false;
            }
        }

        private void Todo_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TodoItem.IsCompleted) && sender is TodoItem todo)
            {
                try
                {
                    _db.UpdateTodoCompletion(todo.Id, todo.IsCompleted);
                    System.Diagnostics.Debug.WriteLine($"[Phase2] Todo 완료 상태 업데이트: ID={todo.Id}, Completed={todo.IsCompleted}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Phase2] Todo 업데이트 오류: {ex.Message}");
                }
            }
        }


        private void LoadDailySubjectsSafe(DateTime date)
        {
            if (_isLoadingSubjects)
            {
                System.Diagnostics.Debug.WriteLine("[DailyBodyViewModel] 이미 과목 로딩 중 - 스킵");
                return;
            }

            _isLoadingSubjects = true;

            try
            {
                System.Diagnostics.Debug.WriteLine($"[DailyBodyViewModel] LoadDailySubjectsSafe 시작");

                // 이벤트 임시 해제
                if (Subjects != null)
                {
                    Subjects.CollectionChanged -= Subjects_CollectionChanged;
                }

                var processedSubjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // 데이터베이스에서 안전하게 조회
                List<(string SubjectName, double Progress, int StudyTimeSeconds, List<TopicGroupData> TopicGroups)> dailySubjectsWithGroups;

                try
                {
                    dailySubjectsWithGroups = _db.GetDailySubjectsWithTopicGroups(date);
                    System.Diagnostics.Debug.WriteLine($"[DailyBodyViewModel] DB에서 {dailySubjectsWithGroups.Count}개 과목 조회됨");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[DailyBodyViewModel] DB 조회 오류: {ex.Message}");
                    dailySubjectsWithGroups = new List<(string, double, int, List<TopicGroupData>)>();
                }

                // 안전하게 과목 추가 또는 업데이트
                foreach (var (subjectName, progress, studyTimeSeconds, topicGroupsData) in dailySubjectsWithGroups)
                {
                    try
                    {
                        if (processedSubjects.Contains(subjectName))
                        {
                            System.Diagnostics.Debug.WriteLine($"[DailyBodyViewModel] 중복 과목 스킵: {subjectName}");
                            continue;
                        }
                        processedSubjects.Add(subjectName);

                        // ✅ 수정: 기존 과목이 있는지 확인
                        var existingSubject = Subjects?.FirstOrDefault(s =>
                            string.Equals(s.SubjectName, subjectName, StringComparison.OrdinalIgnoreCase));

                        if (existingSubject != null)
                        {
                            // ✅ 기존 과목이 있으면 UpdateFromDatabase 호출
                            var topicGroups = new ObservableCollection<TopicGroupViewModel>();
                            foreach (var groupData in topicGroupsData)
                            {
                                var topicGroup = new TopicGroupViewModel
                                {
                                    GroupTitle = groupData.GroupTitle,
                                    TotalStudyTimeSeconds = groupData.TotalStudyTimeSeconds,
                                    IsCompleted = groupData.IsCompleted,
                                    CategoryId = groupData.CategoryId
                                };
                                topicGroups.Add(topicGroup);
                            }

                            existingSubject.UpdateFromDatabase(studyTimeSeconds, topicGroups);
                            System.Diagnostics.Debug.WriteLine($"[DailyBodyViewModel] 기존 과목 업데이트됨: {subjectName}");
                        }
                        else
                        {
                            // ✅ 새 과목 생성 후 UpdateFromDatabase 호출
                            var newSubject = new SubjectProgressViewModel
                            {
                                SubjectName = subjectName
                            };

                            var topicGroups = new ObservableCollection<TopicGroupViewModel>();
                            foreach (var groupData in topicGroupsData)
                            {
                                var topicGroup = new TopicGroupViewModel
                                {
                                    GroupTitle = groupData.GroupTitle,
                                    TotalStudyTimeSeconds = groupData.TotalStudyTimeSeconds,
                                    IsCompleted = groupData.IsCompleted,
                                    CategoryId = groupData.CategoryId
                                };
                                topicGroups.Add(topicGroup);
                            }

                            // ✅ UpdateFromDatabase 호출로 통합된 업데이트
                            newSubject.UpdateFromDatabase(studyTimeSeconds, topicGroups);

                            // Subjects 컬렉션에 추가
                            if (Subjects != null)
                            {
                                Subjects.Add(newSubject);
                                System.Diagnostics.Debug.WriteLine($"[DailyBodyViewModel] 새 과목 안전하게 추가됨: {subjectName}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[DailyBodyViewModel] 과목 {subjectName} 처리 오류: {ex.Message}");
                    }
                }

                System.Diagnostics.Debug.WriteLine($"[DailyBodyViewModel] 최종 과목 수: {Subjects?.Count ?? 0}개");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DailyBodyViewModel] LoadDailySubjectsSafe 오류: {ex.Message}");
            }
            finally
            {
                // 이벤트 다시 연결
                if (Subjects != null)
                {
                    Subjects.CollectionChanged += Subjects_CollectionChanged;
                }
                _isLoadingSubjects = false;
                System.Diagnostics.Debug.WriteLine("[DailyBodyViewModel] LoadDailySubjectsSafe 완료");
            }
        }

        // 공유 데이터 설정 메소드 - 수정됨
        public void SetSharedSubjects(ObservableCollection<SubjectProgressViewModel> sharedSubjects)
        {
            System.Diagnostics.Debug.WriteLine("[DailyBodyViewModel] SetSharedSubjects 시작");

            // ✅ 로딩 플래그를 임시로만 설정
            bool wasLoadingFromDb = _isLoadingFromDatabase;
            bool wasLoadingSubjects = _isLoadingSubjects;

            _isLoadingFromDatabase = true;
            _isLoadingSubjects = true;

            try
            {
                // 기존 이벤트 완전 해제
                if (Subjects != null)
                {
                    Subjects.CollectionChanged -= Subjects_CollectionChanged;
                }

                // ✅ 수정: 기존 데이터를 UpdateFromDatabase로 병합
                if (Subjects != null && Subjects.Count > 0)
                {
                    var existingData = Subjects.ToList();
                    foreach (var existingItem in existingData)
                    {
                        // 공유 데이터에서 같은 과목 찾기
                        var sharedItem = sharedSubjects.FirstOrDefault(s =>
                            string.Equals(s.SubjectName, existingItem.SubjectName, StringComparison.OrdinalIgnoreCase));

                        if (sharedItem != null)
                        {
                            // ✅ 기존 항목이 공유 데이터에 있으면 UpdateFromDatabase로 병합
                            sharedItem.UpdateFromDatabase(existingItem.TodayStudyTimeSeconds, existingItem.TopicGroups);
                            System.Diagnostics.Debug.WriteLine($"[SetSharedSubjects] 기존 데이터 병합: {existingItem.SubjectName}");
                        }
                        else
                        {
                            // 공유 데이터에 없는 항목은 추가
                            sharedSubjects.Add(existingItem);
                            System.Diagnostics.Debug.WriteLine($"[SetSharedSubjects] 새 항목 추가: {existingItem.SubjectName}");
                        }
                    }
                }

                // 공유 데이터로 교체
                Subjects = sharedSubjects;

                // 이벤트 다시 연결
                if (Subjects != null)
                {
                    Subjects.CollectionChanged += Subjects_CollectionChanged;
                }

                System.Diagnostics.Debug.WriteLine($"[DailyBodyViewModel] 공유 데이터로 전환됨: {Subjects?.Count ?? 0}개 항목");
            }
            finally
            {
                // ✅ 플래그를 원래 상태로 복원 (중요!)
                _isLoadingFromDatabase = wasLoadingFromDb;
                _isLoadingSubjects = wasLoadingSubjects;

                System.Diagnostics.Debug.WriteLine($"[DailyBodyViewModel] SetSharedSubjects 완료 - _isLoadingSubjects: {_isLoadingSubjects}");
            }
        }

        private void Subjects_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (_isLoadingSubjects || _isLoadingFromDatabase)
            {
                return;
            }

            // Add 액션일 때만 저장
            if (e.Action == NotifyCollectionChangedAction.Add)
            {
                SaveDailySubjects();
            }
        }

        private void AddTodo()
        {
            if (!string.IsNullOrWhiteSpace(NewTodoText))
            {
                string trimmed = NewTodoText.Trim();
                int id = _db.AddTodo(SelectedDate, trimmed); // DB에 저장 + ID 받기

                var newItem = new TodoItem
                {
                    Id = id,
                    Title = trimmed,
                    IsCompleted = false
                };

                // PropertyChanged 이벤트 구독
                newItem.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(TodoItem.IsCompleted))
                    {
                        _db.UpdateTodoCompletion(newItem.Id, newItem.IsCompleted);
                    }
                };

                TodoList.Add(newItem);
                NewTodoText = string.Empty;
            }
            IsAdding = false;
        }

        private void DeleteTodo(TodoItem todo)
        {
            if (todo == null)
            {
                System.Diagnostics.Debug.WriteLine("[Todo] 삭제할 Todo가 null입니다.");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"[Todo] Todo 삭제 시도: {todo.Title} (ID: {todo.Id})");

            try
            {
                _db.DeleteTodo(todo.Id);
                TodoList.Remove(todo);
                System.Diagnostics.Debug.WriteLine($"[Todo] Todo 삭제 완료: {todo.Title}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Todo] Todo 삭제 오류: {ex.Message}");
            }
        }

        // DailyBodyView.xaml.cs에서 호출할 수 있도록 public으로 변경
        public void DeleteTodoItem(TodoItem todo)
        {
            DeleteTodo(todo);
        }

        // 헤더 하단의 comment 관련
        private string _comment = string.Empty;
        public string Comment
        {
            get => _comment;
            set
            {
                if (SetProperty(ref _comment, value))
                {
                    _db.SaveOrUpdateComment(SelectedDate, _comment); // 저장
                }
            }
        }

        public void LoadDailyData(DateTime date)
        {

            SelectedDate = date; // 선택된 날짜를 업데이트하는 코드 추가
            // 🆕 같은 날짜에 대한 중복 로딩 방지
            if (SelectedDate.Date == date.Date && _hasLoadedOnce)
            {
                System.Diagnostics.Debug.WriteLine($"[DailyBodyViewModel] 같은 날짜 데이터 이미 로드됨. 스킵.");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"[DailyBodyViewModel] Phase 1 LoadDailyData 호출 - 날짜: {date.ToShortDateString()}");

            // Phase 1에서는 LoadDailyDataSafe로 리다이렉트
            LoadDailyDataSafe(date);
        }

        private void LoadDailySubjects(DateTime date)
        {
            if (_isLoadingSubjects)
            {
                System.Diagnostics.Debug.WriteLine("[Phase2] 이미 Subject 로딩 중 - 스킵");
                return;
            }

            _isLoadingSubjects = true;

            try
            {
                System.Diagnostics.Debug.WriteLine($"[Phase2] LoadDailySubjects 시작 - 날짜: {date.ToShortDateString()}");

                // 이벤트 임시 해제
                if (Subjects != null)
                {
                    Subjects.CollectionChanged -= Subjects_CollectionChanged;
                }

                var processedSubjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // 데이터베이스에서 안전하게 조회
                List<(string SubjectName, double Progress, int StudyTimeSeconds, List<TopicGroupData> TopicGroups)> dailySubjectsWithGroups;

                try
                {
                    dailySubjectsWithGroups = _db.GetDailySubjectsWithTopicGroups(date);
                    System.Diagnostics.Debug.WriteLine($"[Phase2] DB에서 {dailySubjectsWithGroups.Count}개 과목 조회됨");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Phase2] DB 조회 오류: {ex.Message}");
                    dailySubjectsWithGroups = new List<(string, double, int, List<TopicGroupData>)>();
                }

                // 기존 Subjects 클리어 (공유 데이터가 아닌 경우만)
                if (Subjects != null)
                {
                    Subjects.Clear();
                }

                // 안전하게 과목 추가 또는 업데이트
                foreach (var (subjectName, progress, studyTimeSeconds, topicGroupsData) in dailySubjectsWithGroups)
                {
                    try
                    {
                        if (processedSubjects.Contains(subjectName))
                        {
                            System.Diagnostics.Debug.WriteLine($"[Phase2] 중복 과목 스킵: {subjectName}");
                            continue;
                        }
                        processedSubjects.Add(subjectName);

                        // ✅ 수정: 기존 과목이 있는지 확인
                        var existingSubject = Subjects?.FirstOrDefault(s =>
                            string.Equals(s.SubjectName, subjectName, StringComparison.OrdinalIgnoreCase));

                        if (existingSubject != null)
                        {
                            // ✅ 기존 과목이 있으면 UpdateFromDatabase 호출
                            var topicGroups = new ObservableCollection<TopicGroupViewModel>();
                            foreach (var groupData in topicGroupsData)
                            {
                                var topicGroup = new TopicGroupViewModel
                                {
                                    GroupTitle = groupData.GroupTitle,
                                    TotalStudyTimeSeconds = groupData.TotalStudyTimeSeconds,
                                    IsCompleted = groupData.IsCompleted,
                                    CategoryId = groupData.CategoryId
                                };
                                topicGroups.Add(topicGroup);
                            }

                            existingSubject.UpdateFromDatabase(studyTimeSeconds, topicGroups);
                            System.Diagnostics.Debug.WriteLine($"[Phase2] 기존 과목 업데이트됨: {subjectName} (TopicGroups: {topicGroups.Count}개)");
                        }
                        else
                        {
                            // ✅ 새 과목 생성 후 UpdateFromDatabase 호출
                            var newSubject = new SubjectProgressViewModel
                            {
                                SubjectName = subjectName
                            };

                            var topicGroups = new ObservableCollection<TopicGroupViewModel>();
                            foreach (var groupData in topicGroupsData)
                            {
                                var topicGroup = new TopicGroupViewModel
                                {
                                    GroupTitle = groupData.GroupTitle,
                                    TotalStudyTimeSeconds = groupData.TotalStudyTimeSeconds,
                                    IsCompleted = groupData.IsCompleted,
                                    CategoryId = groupData.CategoryId
                                };
                                topicGroups.Add(topicGroup);
                            }

                            // ✅ UpdateFromDatabase 호출로 통합된 업데이트
                            newSubject.UpdateFromDatabase(studyTimeSeconds, topicGroups);

                            // Subjects 컬렉션에 추가
                            if (Subjects != null)
                            {
                                Subjects.Add(newSubject);
                                System.Diagnostics.Debug.WriteLine($"[Phase2] 새 과목 추가됨: {subjectName} (TopicGroups: {topicGroups.Count}개)");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Phase2] 과목 {subjectName} 처리 오류: {ex.Message}");
                        // 개별 과목 오류는 무시하고 계속 진행
                    }
                }

                System.Diagnostics.Debug.WriteLine($"[Phase2] 최종 과목 수: {Subjects?.Count ?? 0}개");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Phase2] LoadDailySubjects 전체 오류: {ex.Message}");
            }
            finally
            {
                // 이벤트 다시 연결
                if (Subjects != null)
                {
                    Subjects.CollectionChanged += Subjects_CollectionChanged;
                }
                _isLoadingSubjects = false;
                System.Diagnostics.Debug.WriteLine("[Phase2] LoadDailySubjects 완료");
            }
        }

        private void SaveDailySubjects()
        {
            if (_isLoadingSubjects || _isLoadingFromDatabase)
            {
                System.Diagnostics.Debug.WriteLine("[DailyBodyViewModel] SaveDailySubjects 스킵됨 (로딩 중)");
                return; // 로딩 중이면 저장하지 않음
            }

            try
            {
                // 중복 제거된 과목 리스트만 저장
                var uniqueSubjects = Subjects
                    .GroupBy(s => s.SubjectName.Trim(), StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToList();

                // 🆕 일괄 저장 방식으로 중복 저장 방지
                _db.RemoveAllDailySubjects(SelectedDate);

                for (int i = 0; i < uniqueSubjects.Count; i++)
                {
                    var subject = uniqueSubjects[i];
                    // ✅ 실제 측정된 진행률로 저장
                    _db.SaveDailySubjectWithTopicGroups(SelectedDate, subject.SubjectName, subject.ActualProgress, subject.TodayStudyTimeSeconds, i, subject.TopicGroups);
                }

                System.Diagnostics.Debug.WriteLine($"[DailyBodyViewModel] 오늘 할 일 과목과 TopicGroups 일괄 저장 완료: {uniqueSubjects.Count}개");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DailyBodyViewModel] 오늘 할 일 과목 저장 오류: {ex.Message}");
            }
        }

        public void AddSubjectSafely(SubjectProgressViewModel subject)
        {
            if (subject == null || string.IsNullOrWhiteSpace(subject.SubjectName))
                return;

            // 중복 확인 - 대소문자 무시하고 정확한 이름 매치
            var existingSubject = Subjects.FirstOrDefault(s =>
                string.Equals(s.SubjectName.Trim(), subject.SubjectName.Trim(), StringComparison.OrdinalIgnoreCase));

            if (existingSubject == null)
            {
                System.Diagnostics.Debug.WriteLine($"[DailyBodyViewModel] 새 과목 추가: {subject.SubjectName}");
                Subjects.Add(subject);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[DailyBodyViewModel] 중복 과목 무시: {subject.SubjectName} (이미 존재함)");

                // ✅ 기존 과목의 실제 측정 시간 업데이트
                if (existingSubject.TodayStudyTimeSeconds < subject.TodayStudyTimeSeconds)
                {
                    existingSubject.TodayStudyTimeSeconds = subject.TodayStudyTimeSeconds;
                }
            }
        }

        //  우측 정보 영역 Day 정보 표시 및 공부시간 표시
        public string InfoTitle
        {
            get
            {
                if (!IsToday) return "총 학습 시간";

                var dday = _db.GetNextDDay();
                return dday?.Title ?? ""; // D-Day 이벤트가 있으면 제목, 없으면 ""
            }
        }

        public string InfoContent
        {
            get
            {
              if (!IsToday) return TodayStudyTime; // 다른 날짜일때는 해당 날짜의 총 공부시간
        
              var dday = _db.GetNextDDay();
                if (dday.HasValue)
                {
                    // D-Day까지 남은 날짜가 0이면 "D-Day"라고 표시합니다.
                    if (dday.Value.DaysLeft == 0)
                    {
                        return "D-Day";
                    }
                    // 0이 아니면 기존처럼 "D-남은날짜"로 표시합니다.
                    else
                    {
                        return $"D-{dday.Value.DaysLeft}";
                    }
                }

                // D-Day가 없으면 "-"를 반환합니다.
                return "일정 없음";
            }
        }

        public void RefreshDdayInfo()
        {
            OnPropertyChanged(nameof(InfoTitle));
            OnPropertyChanged(nameof(InfoContent));
        }

        public bool IsToday => SelectedDate.Date == DateTime.Today;

        private DateTime _selectedDate;
        public DateTime SelectedDate
        {
            get => _selectedDate;
            set
            {
                if (SetProperty(ref _selectedDate, value))
                {
                    // 날짜가 바뀌면 Comment를 다시 불러오고, 할 일도 갱신하도록 처리
                    Comment = _db.GetCommentByDate(value);

                    // 날짜 변경 시 InfoTitle과 InfoContent도 업데이트
                    OnPropertyChanged(nameof(IsToday));
                    OnPropertyChanged(nameof(InfoTitle));
                    OnPropertyChanged(nameof(InfoContent));
                    OnPropertyChanged(nameof(TodayStudyTime));

                    // 🆕 날짜 변경 시 로드 플래그 리셋
                    _hasLoadedOnce = false;
                }
            }
        }

        // ✅ 오늘의 실제 측정된 총 학습시간
        public string TodayStudyTime
        {
            get
            {
                int totalSeconds = _db.GetTotalStudyTimeSeconds(SelectedDate);
                var totalTime = TimeSpan.FromSeconds(totalSeconds);
                return $"{(int)totalTime.TotalHours:D2}:{totalTime.Minutes:D2}:{totalTime.Seconds:D2}";
            }
        }

        // ✅ 전체 누적 학습시간
        public string AllTimeStudyTime
        {
            get
            {
                int totalSeconds = _db.GetTotalStudyTimeSeconds();
                var totalTime = TimeSpan.FromSeconds(totalSeconds);
                return $"{(int)totalTime.TotalHours}시간 {totalTime.Minutes}분";
            }
        }
    }
}
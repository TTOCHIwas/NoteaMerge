using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Notea.Modules.Common.ViewModels;
using Notea.Modules.Daily.ViewModels;
using Notea.Modules.Daily.Views;
using Notea.Modules.Monthly.ViewModels;
using Notea.Modules.Monthly.Views;
using Notea.Modules.Subjects.ViewModels;
using Notea.Modules.Subjects.Views;

namespace Notea.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        public DateTime AppStartDate { get; } = DateTime.Now.Date;

        private RightSidebarViewModel _rightSidebarViewModel;
        private bool _progressUpdateSubscribed = false;

        // ViewModel들 (한 번만 생성)
        private readonly DailyHeaderViewModel _dailyHeaderVM;
        private readonly DailyBodyViewModel _dailyBodyVM;
        private readonly SubjectListPageViewModel _subjectListPageVM;
        private readonly MonthlyPlanViewModel _monthlyPlanVM;
        private readonly YearMonthListViewModel _yearMonthListVM;

        // View들 (한 번만 생성)
        private readonly DailyHeaderView _dailyHeaderView;
        private readonly DailyBodyView _dailyBodyView;
        private readonly SubjectListPageHeaderView _subjectHeaderView;
        private readonly SubjectListPageBodyView _subjectBodyView;
        private readonly CalendarMonth _calendarMonthView;
        private readonly YearMonthListView _yearMonthListView;

        private Notea.Modules.Subject.Views.NotePageHeaderView _notePageHeaderView;
        private Notea.Modules.Subject.Views.NotePageBodyView _notePageBodyView;
        private Notea.Modules.Subject.ViewModels.NotePageViewModel _notePageVM;

        // 현재 선택된 과목 정보
        private string _currentSelectedSubject;
        private int _currentSelectedSubjectId;

        public ICommand NavigateToNoteEditorCommand { get; }
        public ICommand NavigateBackToSubjectListCommand { get; }

        // 🆕 공유 데이터 소스 - 두 페이지에서 모두 사용 (실제 측정 시간만)
        public ObservableCollection<SubjectProgressViewModel> SharedSubjectProgress { get; set; }

        private bool _isSidebarCollapsed = false;
        public bool IsSidebarCollapsed
        {
            get => _isSidebarCollapsed;
            set
            {
                if (_isSidebarCollapsed != value)
                {
                    _isSidebarCollapsed = value;
                    OnPropertyChanged(nameof(IsSidebarCollapsed));
                }
            }
        }

        public void NavigateToNoteEditorWithCategory(string subjectName, int categoryId)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[MainViewModel] Category 클릭으로 필기 화면 이동 - Subject: {subjectName}, CategoryId: {categoryId}");

                // 기존 필기 화면 저장
                SaveCurrentNotePageIfExists();

                // SubjectId 조회
                int subjectId = Notea.Modules.Subject.Models.NoteRepository.GetSubjectIdByName(subjectName);

                if (subjectId <= 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[MainViewModel] 과목 '{subjectName}'을 찾을 수 없습니다.");
                    return;
                }

                _currentSelectedSubject = subjectName;
                _currentSelectedSubjectId = subjectId;

                // 필기 화면용 ViewModel 생성
                _notePageVM = new Notea.Modules.Subject.ViewModels.NotePageViewModel();

                // Header와 Body View 생성하고 DataContext 설정
                _notePageHeaderView = new Notea.Modules.Subject.Views.NotePageHeaderView { DataContext = _notePageVM };
                _notePageBodyView = new Notea.Modules.Subject.Views.NotePageBodyView { DataContext = _notePageVM };

                // 과목 정보 설정
                _notePageVM.SetSubject(subjectId, subjectName);

                // 특정 Category로 스크롤 (약간의 지연 필요)
                System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Loaded,
                    new Action(() => {
                        _notePageVM.ScrollToCategory(categoryId);

                        // ✅ 추가: 스크롤 완료 후 타이머의 활성 카테고리 설정
                        SetTimerActiveCategory(categoryId, subjectName);
                    })
                );

                // 필기 화면으로 전환
                HeaderContent = _notePageHeaderView;
                BodyContent = _notePageBodyView;

                // 왼쪽 사이드바 설정
                SidebarViewModel.SetContext("today");
                SidebarViewModel.SetSharedSubjectProgress(SharedSubjectProgress);

                System.Diagnostics.Debug.WriteLine($"[MainViewModel] Category {categoryId}로 필기 화면 이동 완료");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainViewModel] Category 필기 화면 이동 오류: {ex.Message}");
            }
        }

        private void SetTimerActiveCategory(int categoryId, string subjectName)
        {
            try
            {
                // RightSidebarViewModel 찾기
                if (_rightSidebarViewModel == null)
                {
                    _rightSidebarViewModel = FindRightSidebarViewModel();
                }

                if (_rightSidebarViewModel != null)
                {
                    // 타이머의 활성 카테고리 설정
                    _rightSidebarViewModel.SetActiveCategory(categoryId, subjectName);
                    System.Diagnostics.Debug.WriteLine($"[MainViewModel] 타이머 활성 카테고리 설정 완료: CategoryId={categoryId}, Subject={subjectName}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[MainViewModel] RightSidebarViewModel을 찾을 수 없어 타이머 설정 실패");

                    // RightSidebarViewModel을 찾지 못한 경우 한 번만 재시도
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                        System.Windows.Threading.DispatcherPriority.Background,
                        new Action(() => {
                            // 한 번 더 시도하되, 실패해도 로그를 출력하지 않음
                            var retryRsvm = FindRightSidebarViewModel();
                            if (retryRsvm != null)
                            {
                                retryRsvm.SetActiveCategory(categoryId, subjectName);
                                System.Diagnostics.Debug.WriteLine($"[MainViewModel] 재시도로 타이머 활성 카테고리 설정 완료: CategoryId={categoryId}");
                            }
                        })
                    );
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainViewModel] 타이머 활성 카테고리 설정 오류: {ex.Message}");
            }
        }

        public ICommand NavigateToNoteEditorWithCategoryCommand { get; }


        private void ToggleSidebar()
        {
            try
            {
                if (LeftSidebarWidth.Value > 0)
                {
                    LeftSidebarWidth = new GridLength(0);
                    IsSidebarCollapsed = true;
                    System.Diagnostics.Debug.WriteLine("[MainViewModel] 사이드바 숨김");
                }
                else
                {
                    LeftSidebarWidth = new GridLength(280);
                    IsSidebarCollapsed = false;
                    System.Diagnostics.Debug.WriteLine("[MainViewModel] 사이드바 표시");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainViewModel] 사이드바 토글 오류: {ex.Message}");
            }
        }

        private LeftSidebarViewModel _sidebarViewModel;
        public LeftSidebarViewModel SidebarViewModel
        {
            get => _sidebarViewModel;
            set
            {
                if (_sidebarViewModel != value)
                {
                    _sidebarViewModel = value;
                    OnPropertyChanged(nameof(SidebarViewModel));
                }
            }
        }

        private GridLength _leftSidebarWidth = new GridLength(280);
        public GridLength LeftSidebarWidth
        {
            get => _leftSidebarWidth;
            set
            {
                if (_leftSidebarWidth != value)
                {
                    _leftSidebarWidth = value;
                    OnPropertyChanged(nameof(LeftSidebarWidth));
                    OnPropertyChanged(nameof(IsSidebarCollapsed));
                }
            }
        }

        public ICommand ToggleSidebarCommand { get; }
        public ICommand ExpandSidebarCommand { get; }
        public ICommand NavigateToSubjectListCommand { get; }
        public ICommand NavigateToTodayCommand { get; }
        public ICommand NavigateToCalendarCommand { get; }
        public ICommand NavigateToYearlyCommand { get; }
        public ICommand NavigateToDailyViewForDateCommand { get; }
        public ICommand NavigateToCalendarFromYearlyCommand { get; }

        private bool _isHeaderVisible = true;
        public bool IsHeaderVisible
        {
            get => _isHeaderVisible;
            set
            {
               _isHeaderVisible = value;
                OnPropertyChanged(nameof(IsHeaderVisible));
            }
        }

private UserControl _headerContent;
        public UserControl HeaderContent
        {
            get => _headerContent;
            set
            {
                if (_headerContent != value)
                {
                    _headerContent = value;
                    OnPropertyChanged(nameof(HeaderContent));
                }
            }
        }

        private UserControl _bodyContent;
        public UserControl BodyContent
        {
            get => _bodyContent;
            set
            {
                if (_bodyContent != value)
                {
                    _bodyContent = value;
                    OnPropertyChanged(nameof(BodyContent));
                }
            }
        }

        // ✅ 수정: 전체 학습시간 표시 속성
        public string TotalStudyTimeDisplay
        {
            get
            {
                try
                {
                    var totalSeconds = SharedSubjectProgress?.Sum(s => s.TodayStudyTimeSeconds) ?? 0;
                    var timeSpan = TimeSpan.FromSeconds(totalSeconds);
                    return timeSpan.ToString(@"hh\:mm\:ss");
                }
                catch
                {
                    return "00:00:00";
                }
            }
        }

        public MainViewModel()
        {
            SharedSubjectProgress = new ObservableCollection<SubjectProgressViewModel>();

            SidebarViewModel = new LeftSidebarViewModel("main");

            _dailyHeaderVM = new DailyHeaderViewModel();
            _dailyBodyVM = new DailyBodyViewModel(AppStartDate, skipInitialLoad: true); // ✅ 초기 로딩 스킵
            _subjectListPageVM = new SubjectListPageViewModel();
            _monthlyPlanVM = new MonthlyPlanViewModel();
            _yearMonthListVM = new YearMonthListViewModel();

            NavigateToNoteEditorWithCategoryCommand = new RelayCommand<object>(ExecuteNavigateToNoteEditorWithCategory);
            _dailyBodyVM.SetSharedSubjects(SharedSubjectProgress);

            _dailyHeaderView = new DailyHeaderView { DataContext = _dailyHeaderVM };
            _dailyBodyView = new DailyBodyView { DataContext = _dailyBodyVM };
            _subjectHeaderView = new SubjectListPageHeaderView();
            _subjectBodyView = new SubjectListPageBodyView { DataContext = _subjectListPageVM };

            // ✅ 수정: CalendarMonth에 올바른 ViewModel 연결
            _calendarMonthView = new CalendarMonth { DataContext = _monthlyPlanVM };

            // ✅ 수정: YearMonthListView DataContext 제거 (생성자에서 이미 설정됨)
            _yearMonthListView = new YearMonthListView{DataContext = _yearMonthListVM};
            //_yearMonthListView.DataContext = _yearMonthListVM; // 명시적 설정

            // 초기 화면 설정 (Daily 화면)

            HeaderContent = _dailyHeaderView;
            BodyContent = _dailyBodyView;

            ToggleSidebarCommand = new RelayCommand(ToggleSidebar);
            ExpandSidebarCommand = new RelayCommand(ExpandSidebar);
            NavigateToSubjectListCommand = new RelayCommand(NavigateToSubjectList);
            NavigateToTodayCommand = new RelayCommand(NavigateToToday);

            NavigateToCalendarCommand = new RelayCommand<string>(NavigateToCalendar);
            NavigateToYearlyCommand = new RelayCommand(NavigateToYearly);
            //미래의 할일은 진입 x
            NavigateToDailyViewForDateCommand = new RelayCommand<DateTime>(
     execute: NavigateToDailyViewForDate,
     canExecute: (date) => date.Date <= DateTime.Today
 );
            NavigateToCalendarFromYearlyCommand = new RelayCommand<YearMonthViewModel>(NavigateToCalendarFromYearly);

            NavigateToNoteEditorCommand = new RelayCommand<object>(NavigateToNoteEditor);
            NavigateBackToSubjectListCommand = new RelayCommand(NavigateBackToSubjectList);

            try
            {
                RestoreDailySubjects();
                SetupProgressUpdateSystem();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainViewModel] 초기화 오류: {ex.Message}");
            }
        }

        private void ExecuteNavigateToNoteEditorWithCategory(object parameter)
        {
            if (parameter is TopicGroupViewModel topicGroup)
            {
                // 부모 Subject 이름 찾기
                string subjectName = topicGroup.ParentSubjectName;

                if (string.IsNullOrEmpty(subjectName))
                {
                    // 현재 선택된 Subject 사용
                    subjectName = _currentSelectedSubject;
                }

                if (!string.IsNullOrEmpty(subjectName) && topicGroup.CategoryId > 0)
                {
                    NavigateToNoteEditorWithCategory(subjectName, topicGroup.CategoryId);
                }
            }
        }

        private void ExpandSidebar()
        {
            try
            {
                LeftSidebarWidth = new GridLength(280);
                IsSidebarCollapsed = false;
                System.Diagnostics.Debug.WriteLine("[MainViewModel] 사이드바 확장");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainViewModel] 사이드바 확장 오류: {ex.Message}");
            }
        }

        private void NavigateToNoteEditor(object parameter)
        {
            try
            {
                string subjectName = null;
                int subjectId = 0;

                System.Diagnostics.Debug.WriteLine($"[MainViewModel] NavigateToNoteEditor 호출됨 - Parameter 타입: {parameter?.GetType().Name}");

                // 파라미터에서 과목 정보 추출
                if (parameter is SubjectGroupViewModel subjectGroup)
                {
                    subjectName = subjectGroup.SubjectName;
                    subjectId = subjectGroup.SubjectId;
                    System.Diagnostics.Debug.WriteLine($"[MainViewModel] SubjectGroupViewModel - Name: '{subjectName}', ID: {subjectId}");
                }
                else if (parameter is SubjectProgressViewModel subjectProgress)
                {
                    subjectName = subjectProgress.SubjectName;
                    subjectId = GetSubjectIdByName(subjectName);
                    System.Diagnostics.Debug.WriteLine($"[MainViewModel] SubjectProgressViewModel - Name: '{subjectName}', ID: {subjectId}");
                }
                else if (parameter is string name)
                {
                    subjectName = name;
                    subjectId = GetSubjectIdByName(subjectName);
                    System.Diagnostics.Debug.WriteLine($"[MainViewModel] String parameter - Name: '{subjectName}', ID: {subjectId}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[MainViewModel] 알 수 없는 parameter 타입: {parameter}");
                }

                // ✅ 유효성 검사 강화
                if (string.IsNullOrEmpty(subjectName))
                {
                    System.Diagnostics.Debug.WriteLine($"[MainViewModel] ERROR: subjectName이 null 또는 빈 문자열");
                    return;
                }

                if (subjectId <= 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[MainViewModel] ERROR: 유효하지 않은 subjectId: {subjectId}");

                    // 과목명으로 다시 조회 시도
                    subjectId = GetSubjectIdByName(subjectName);
                    if (subjectId <= 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"[MainViewModel] ERROR: 과목명 '{subjectName}'으로도 subjectId를 찾을 수 없음");
                        return;
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[MainViewModel] 과목명으로 subjectId 복구 성공: {subjectId}");
                    }
                }

                _currentSelectedSubject = subjectName;
                _currentSelectedSubjectId = subjectId;

                System.Diagnostics.Debug.WriteLine($"[MainViewModel] 최종 확인 - SubjectName: '{_currentSelectedSubject}', SubjectId: {_currentSelectedSubjectId}");

                // 필기 화면용 ViewModel 생성
                _notePageVM = new Notea.Modules.Subject.ViewModels.NotePageViewModel();

                // Header와 Body View 생성하고 DataContext 설정
                _notePageHeaderView = new Notea.Modules.Subject.Views.NotePageHeaderView { DataContext = _notePageVM };
                _notePageBodyView = new Notea.Modules.Subject.Views.NotePageBodyView { DataContext = _notePageVM };

                System.Diagnostics.Debug.WriteLine($"[MainViewModel] NotePageViewModel 및 Views 생성 완료");

                // ✅ 과목 정보 설정 (가장 중요한 부분)
                System.Diagnostics.Debug.WriteLine($"[MainViewModel] SetSubject 호출 시작 - SubjectId: {subjectId}, SubjectName: '{subjectName}'");
                _notePageVM.SetSubject(subjectId, subjectName);
                System.Diagnostics.Debug.WriteLine($"[MainViewModel] SetSubject 호출 완료");

                // 필기 화면으로 전환
                HeaderContent = _notePageHeaderView;
                BodyContent = _notePageBodyView;

                // 왼쪽 사이드바 설정
                SidebarViewModel.SetContext("today");
                SidebarViewModel.SetSharedSubjectProgress(SharedSubjectProgress);

                System.Diagnostics.Debug.WriteLine($"[MainViewModel] 과목 '{subjectName}' 필기 화면으로 이동 완료");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainViewModel] 필기 화면 이동 오류: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[MainViewModel] 스택 트레이스: {ex.StackTrace}");
            }
        }



        private void NavigateBackToSubjectList()
        {
            try
            {
                if (_notePageVM != null)
                {
                    _notePageVM.EditorViewModel?.ForceFullSave();
                    _notePageVM.SaveChanges();
                }

                HeaderContent = _subjectHeaderView;
                BodyContent = _subjectBodyView;

                SidebarViewModel.SetContext("main");

                SidebarViewModel.RefreshData();

                _subjectListPageVM?.RefreshData();

                _notePageHeaderView = null;
                _notePageBodyView = null;
                _notePageVM = null;

                System.Diagnostics.Debug.WriteLine("[MainViewModel] 과목 목록으로 돌아감 완료 (데이터 새로고침됨)");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainViewModel] 과목 목록 돌아가기 오류: {ex.Message}");
            }
        }

        private int GetSubjectIdByName(string subjectName)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[MainViewModel] GetSubjectIdByName 호출 - subjectName: '{subjectName}'");

                if (string.IsNullOrEmpty(subjectName))
                {
                    System.Diagnostics.Debug.WriteLine($"[MainViewModel] GetSubjectIdByName - subjectName이 null 또는 빈 문자열");
                    return 0;
                }

                int result = Notea.Modules.Subject.Models.NoteRepository.GetSubjectIdByName(subjectName);
                System.Diagnostics.Debug.WriteLine($"[MainViewModel] GetSubjectIdByName 결과 - subjectName: '{subjectName}' → subjectId: {result}");

                if (result == 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[MainViewModel] 경고: 과목 '{subjectName}'에 대한 ID를 찾을 수 없습니다!");

                    // 유사한 이름의 과목이 있는지 확인
                    try
                    {
                        using var conn = new System.Data.SQLite.SQLiteConnection($"Data Source={System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "data", "notea.db")};Version=3;");
                        conn.Open();
                        using var cmd = conn.CreateCommand();
                        cmd.CommandText = "SELECT subjectId, Name FROM Subject WHERE Name LIKE @searchName";
                        cmd.Parameters.AddWithValue("@searchName", $"%{subjectName}%");

                        using var reader = cmd.ExecuteReader();
                        System.Diagnostics.Debug.WriteLine($"[MainViewModel] '{subjectName}'과 유사한 과목들:");
                        while (reader.Read())
                        {
                            System.Diagnostics.Debug.WriteLine($"  - ID: {reader["subjectId"]}, Name: '{reader["Name"]}'");
                        }
                    }
                    catch (Exception dbEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"[MainViewModel] 유사 과목 검색 오류: {dbEx.Message}");
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainViewModel] SubjectId 조회 오류: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[MainViewModel] 스택 트레이스: {ex.StackTrace}");
                return 0;
            }
        }

        private void NavigateToSubjectList()
        {
            try
            {

                IsHeaderVisible = true;

                SaveCurrentNotePageIfExists();


                HeaderContent = _subjectHeaderView;
                BodyContent = _subjectBodyView;

                SidebarViewModel.SetContext("today");

                SidebarViewModel.RefreshData();

                _subjectListPageVM?.RefreshData();

                System.Diagnostics.Debug.WriteLine("[MainViewModel] 과목 목록 페이지로 이동 완료 (데이터 새로고침됨)");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainViewModel] 과목 목록 이동 오류: {ex.Message}");
            }
        }

        private void NavigateToToday()
        {
            _dailyBodyVM.RefreshDdayInfo(); // D-Day 정보 갱신
            try
            {

                IsHeaderVisible=true; // 헤더 표시
                SaveCurrentNotePageIfExists();
                HeaderContent = _dailyHeaderView;
                BodyContent = _dailyBodyView;
                SidebarViewModel.SetContext("main");


                // 1. Body ViewModel에 오늘 날짜의 데이터를 로드하라고 명령합니다.
                _dailyBodyVM.LoadDailyData(DateTime.Now);

                // 2. Header ViewModel에 표시될 날짜를 오늘 날짜로 설정하라고 명령합니다.
                _dailyHeaderVM.SetSelectedDate(DateTime.Now);

                _dailyHeaderVM.Title = "오늘 할 일";

                // 3. (D-Day 기능 사용 시) D-Day 정보도 새로고침합니다.
                _dailyBodyVM.RefreshDdayInfo();

                System.Diagnostics.Debug.WriteLine("[MainViewModel] 오늘 페이지로 이동 및 데이터 새로고침 완료");

                SidebarViewModel.RefreshData();

                System.Diagnostics.Debug.WriteLine("[MainViewModel] 오늘 페이지로 이동");

            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainViewModel] 오늘 페이지 이동 오류: {ex.Message}");
            }
        }
        /// <summary>
        /// Daily 화면의 날짜 클릭 → Calendar 화면으로 이동
        /// 파라미터: DailyHeaderViewModel.CurrentDate (string "yyyy.MM.dd")
        /// </summary>
        private void NavigateToCalendar(string dateString)
        {
            try
            {
                DateTime? selectedDate = null;

                // "yyyy.MM.dd" 형식 파싱
                if (!string.IsNullOrEmpty(dateString) &&
                    DateTime.TryParseExact(dateString, "yyyy.MM.dd", null, DateTimeStyles.None, out DateTime parsed))
                {
                    selectedDate = parsed;
                }

                System.Diagnostics.Debug.WriteLine($"[Navigation] 캘린더로 이동 - 날짜: {selectedDate?.ToShortDateString() ?? "파싱 실패"}");

                IsHeaderVisible = false;
                HeaderContent = null; // 헤더 숨김
                BodyContent = _calendarMonthView;

                if (selectedDate.HasValue)
                {
                    _calendarMonthView.CurrentDate = selectedDate.Value;
                }
                else
                {
                    _calendarMonthView.CurrentDate = DateTime.Now;
                }

                // ✅ 수정: 사이드바 컨텍스트를 "today"로 변경 (프로그래스 리스트 표시)
                SidebarViewModel.SetContext("today");

                // ✅ 추가: 공유 데이터 연결
                SidebarViewModel.SetSharedSubjectProgress(SharedSubjectProgress);

                System.Diagnostics.Debug.WriteLine("[Navigation] 캘린더 이동 완료");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Navigation] 캘린더 이동 오류: {ex.Message}");
            }
        }

        /// <summary>
        /// Calendar 화면의 년/월 텍스트 클릭 → Yearly 화면으로 이동
        /// 파라미터: 없음
        /// </summary>
        private void NavigateToYearly()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[Navigation] 연간 뷰로 이동");

                // 헤더 숨김
                IsHeaderVisible = false;

                // 연간 월 목록 뷰로 콘텐츠 변경
                BodyContent = _yearMonthListView;

                // 연간 데이터 새로고침
                if (_yearMonthListVM != null)
                {
                    _yearMonthListVM.RefreshYearData(); // 이 메서드 구현 필요
                }

                // 사이드바 컨텍스트 변경
                SidebarViewModel.SetContext("yearly");

                System.Diagnostics.Debug.WriteLine("[Navigation] 연간 뷰 이동 완료");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Navigation] 연간 뷰 이동 오류: {ex.Message}");
            }
        }

        /// <summary>
        /// Calendar 화면의 날짜 더블클릭 → Daily 화면으로 이동
        /// 파라미터: CalendarDay.Date (DateTime)
        /// </summary>
        private void NavigateToDailyViewForDate(DateTime selectedDate)
        {
            _dailyBodyVM.RefreshDdayInfo(); // D-Day 정보 갱신
            try
            {
                System.Diagnostics.Debug.WriteLine($"[Navigation] Daily 뷰로 이동 - 날짜: {selectedDate.ToShortDateString()}");

                // 헤더 표시
                IsHeaderVisible = true;

                // Daily 뷰로 콘텐츠 변경
                HeaderContent = _dailyHeaderView;
                BodyContent = _dailyBodyView;

                // 선택된 날짜의 데이터 로드
                _dailyBodyVM.LoadDailyData(selectedDate);

                // DailyHeaderViewModel에 선택된 날짜 설정
                if (_dailyHeaderVM != null)
                {
                    _dailyHeaderVM.SetSelectedDate(selectedDate);

                    // 날짜를 비교하여 헤더의 Title을 직접 설정합니다.
                    if (selectedDate.Date == DateTime.Today)
                    {
                        _dailyHeaderVM.Title = "오늘 할 일";
                    }
                    else
                    {
                        _dailyHeaderVM.Title = selectedDate.ToString("지난 일정");
                    }
                }

                // 사이드바 컨텍스트 변경
                SidebarViewModel.SetContext("main");

                System.Diagnostics.Debug.WriteLine($"[Navigation] Daily 뷰 이동 완료 - {selectedDate.ToShortDateString()}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Navigation] Daily 뷰 이동 오류: {ex.Message}");
            }
        }

        /// <summary>
        /// Yearly 화면의 월 항목 클릭 → Calendar 화면으로 이동
        /// 파라미터: YearMonthViewModel 객체
        /// </summary>
        private void NavigateToCalendarFromYearly(YearMonthViewModel selectedMonth)
        {
            try
            {
                if (selectedMonth == null)
                {
                    System.Diagnostics.Debug.WriteLine("[Navigation] 선택된 월이 null입니다.");
                    return;
                }

                // Year 프로퍼티 필요 (YearMonthViewModel에 추가 필요)
                int year = selectedMonth.Year > 0 ? selectedMonth.Year : DateTime.Now.Year;

                System.Diagnostics.Debug.WriteLine($"[Navigation] 연간 뷰에서 캘린더로 이동 - {year}년 {selectedMonth.Month}월");

                // 헤더 숨김
                IsHeaderVisible = false;

                // 캘린더 뷰로 콘텐츠 변경
                BodyContent = _calendarMonthView;

                // 선택된 월로 캘린더 설정
                DateTime targetDate = new DateTime(year, selectedMonth.Month, 1);
                _calendarMonthView.CurrentDate = targetDate;

                // 사이드바 컨텍스트 변경
                SidebarViewModel.SetContext("calendar");

                System.Diagnostics.Debug.WriteLine($"[Navigation] 캘린더 이동 완료 - {year}년 {selectedMonth.Month}월");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Navigation] 연간→캘린더 이동 오류: {ex.Message}");
            }
        }

        private void SaveCurrentNotePageIfExists()
        {
            try
            {
                if (_notePageVM != null)
                {
                    System.Diagnostics.Debug.WriteLine("[MainViewModel] 필기 화면 이탈 감지 - 데이터 저장 시작");

                    // 강제 즉시 저장
                    _notePageVM.EditorViewModel?.ForceFullSave();
                    _notePageVM.SaveChanges();

                    System.Diagnostics.Debug.WriteLine("[MainViewModel] 필기 화면 이탈 - 데이터 저장 완료");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainViewModel] 필기 화면 이탈 저장 오류: {ex.Message}");
            }
        }

        // ✅ 수정: 진행률 업데이트 시스템 설정
        private void SetupProgressUpdateSystem()
        {
            try
            {
                // TopicGroups의 CategoryId 설정
                SetTopicGroupCategoryIds();

                // RightSidebarViewModel 구독 시도
                SubscribeToTimerEvents();

                System.Diagnostics.Debug.WriteLine("[MainViewModel] 진행률 업데이트 시스템 설정 완료");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainViewModel Error] 진행률 업데이트 시스템 설정 실패: {ex.Message}");
            }
        }

        private void SetTopicGroupCategoryIds()
        {
            try
            {
                foreach (var subject in SharedSubjectProgress)
                {
                    foreach (var topicGroup in subject.TopicGroups)
                    {
                        if (topicGroup.CategoryId == 0)
                        {
                            // GroupTitle로 CategoryId 조회
                            int categoryId = GetCategoryIdByTitle(topicGroup.GroupTitle, subject.SubjectName);
                            if (categoryId > 0)
                            {
                                topicGroup.CategoryId = categoryId;
                                System.Diagnostics.Debug.WriteLine($"[Progress] TopicGroup '{topicGroup.GroupTitle}' CategoryId 설정: {categoryId}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Progress Error] CategoryId 설정 실패: {ex.Message}");
            }
        }

        private int GetCategoryIdByTitle(string groupTitle, string subjectName)
        {
            try
            {
                using var conn = Notea.Modules.Common.Helpers.DatabaseHelper.Instance.GetConnection();
                conn.Open();
                using var cmd = conn.CreateCommand();

                // ✅ 수정: Subject 테이블의 컬럼명 통일
                cmd.CommandText = @"
            SELECT c.categoryId 
            FROM category c 
            INNER JOIN Subject s ON c.subjectId = s.subjectId 
            WHERE c.title = @groupTitle AND s.Name = @subjectName";

                cmd.Parameters.AddWithValue("@groupTitle", groupTitle);
                cmd.Parameters.AddWithValue("@subjectName", subjectName);

                var result = cmd.ExecuteScalar();
                return result != null ? Convert.ToInt32(result) : 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainViewModel] CategoryId 조회 오류: {ex.Message}");
                return 0;
            }
        }

        private void SubscribeToTimerEvents()
        {
            try
            {
                if (_progressUpdateSubscribed) return;

                // RightSidebarViewModel 찾기 - 여러 방법 시도
                _rightSidebarViewModel = FindRightSidebarViewModel();

                if (_rightSidebarViewModel != null)
                {
                    _rightSidebarViewModel.ProgressUpdateRequested += OnProgressUpdateRequested;
                    _progressUpdateSubscribed = true;
                    System.Diagnostics.Debug.WriteLine("[MainViewModel] 타이머 이벤트 구독 완료");
                }
                else
                {
                    // RightSidebarViewModel을 찾지 못한 경우 지연 구독
                    System.Diagnostics.Debug.WriteLine("[MainViewModel] RightSidebarViewModel 찾기 실패 - 지연 구독 설정");
                    SetupDelayedSubscription();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainViewModel] 타이머 이벤트 구독 오류: {ex.Message}");
            }
        }

        // ✅ 수정: RightSidebarViewModel 찾기
        private RightSidebarViewModel FindRightSidebarViewModel()
        {
            try
            {
                // 방법 1: Application.Current.MainWindow에서 찾기
                if (Application.Current?.MainWindow?.DataContext is MainViewModel mainViewModel)
                {
                    // MainViewModel의 프로퍼티로 접근
                    var rightSidebarProp = mainViewModel.GetType().GetProperty("RightSidebarViewModel");
                    if (rightSidebarProp != null)
                    {
                        return rightSidebarProp.GetValue(mainViewModel) as RightSidebarViewModel;
                    }
                }

                // 방법 2: MainWindow의 RightSidebar UserControl에서 찾기
                if (Application.Current?.MainWindow is Window mainWindow)
                {
                    var rightSidebar = FindChild<UserControl>(mainWindow, "RightSidebar");
                    if (rightSidebar?.DataContext is RightSidebarViewModel rsvm)
                    {
                        return rsvm;
                    }
                }

                // 방법 3: Visual Tree 전체 검색
                var rightSidebarControl = FindRightSidebarControl();
                if (rightSidebarControl?.DataContext is RightSidebarViewModel rvm)
                {
                    return rvm;
                }

                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainViewModel] RightSidebarViewModel 찾기 오류: {ex.Message}");
                return null;
            }
        }

        private T FindChild<T>(DependencyObject parent, string childName = null) where T : DependencyObject
        {
            if (parent == null) return null;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is T && (childName == null || (child as FrameworkElement)?.Name == childName))
                {
                    return (T)child;
                }

                var childOfChild = FindChild<T>(child, childName);
                if (childOfChild != null)
                    return childOfChild;
            }

            return null;
        }

        private UserControl FindRightSidebarControl()
        {
            if (Application.Current.MainWindow == null) return null;

            return FindVisualChild<UserControl>(Application.Current.MainWindow, "RightSidebar");
        }

        private static T FindVisualChild<T>(DependencyObject parent, string name = null) where T : DependencyObject
        {
            if (parent == null) return null;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is T result && (name == null || (child as FrameworkElement)?.Name == name))
                    return result;

                var childResult = FindVisualChild<T>(child, name);
                if (childResult != null)
                    return childResult;
            }
            return null;
        }

        private void SetupDelayedSubscription()
        {
            // 타이머로 주기적으로 RightSidebarViewModel 찾기 시도
            var retryTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };

            int retryCount = 0;
            retryTimer.Tick += (s, e) =>
            {
                retryCount++;
                if (retryCount > 10) // 최대 10회 시도
                {
                    retryTimer.Stop();
                    System.Diagnostics.Debug.WriteLine("[MainViewModel] RightSidebarViewModel 구독 시도 포기");
                    return;
                }

                _rightSidebarViewModel = FindRightSidebarViewModel();
                if (_rightSidebarViewModel != null)
                {
                    _rightSidebarViewModel.ProgressUpdateRequested += OnProgressUpdateRequested;
                    _progressUpdateSubscribed = true;
                    retryTimer.Stop();
                    System.Diagnostics.Debug.WriteLine($"[MainViewModel] 지연 구독 성공 (시도 {retryCount}회)");
                }
            };

            retryTimer.Start();
        }

        private void OnProgressUpdateRequested()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[MainViewModel] 진행률 업데이트 요청됨");

                // UI 스레드에서 실행 보장
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    UpdateAllProgressData();
                }));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainViewModel] 진행률 업데이트 오류: {ex.Message}");
            }
        }

        // ✅ 수정: 메소드명 통일 및 완전한 구현
        private void UpdateAllProgressData()
{
    try
    {
        var today = DateTime.Today;
        int totalTodaySeconds = 0;

        System.Diagnostics.Debug.WriteLine("[Progress] 전체 진행률 업데이트 시작");

        // 모든 과목의 오늘 학습시간과 TopicGroups 업데이트
        foreach (var subject in SharedSubjectProgress)
        {
            // ✅ 수정: 과목별 실제 측정 시간 조회
            var subjectSeconds = Notea.Modules.Common.Helpers.DatabaseHelper.Instance.GetSubjectDailyTimeSeconds(today, subject.SubjectName);
            totalTodaySeconds += subjectSeconds;

            // ✅ 수정: 기존 TopicGroups를 기반으로 시간만 업데이트
            var topicGroupsData = new ObservableCollection<TopicGroupViewModel>();
            
            try
            {
                var dbHelper = Notea.Modules.Common.Helpers.DatabaseHelper.Instance;
                
                // 기존 TopicGroups를 순회하며 최신 시간으로 업데이트
                foreach (var existingGroup in subject.TopicGroups)
                {
                    // 분류별 실제 측정 시간 조회
                    var categorySeconds = existingGroup.CategoryId > 0
                        ? dbHelper.GetCategoryDailyTimeSeconds(today, existingGroup.CategoryId)
                        : GetTopicGroupTimeByName(today, subject.SubjectName, existingGroup.GroupTitle);

                    var updatedTopicGroup = new TopicGroupViewModel
                    {
                        GroupTitle = existingGroup.GroupTitle,
                        TotalStudyTimeSeconds = categorySeconds,
                        IsCompleted = existingGroup.IsCompleted,
                        CategoryId = existingGroup.CategoryId,
                        ParentSubjectName = subject.SubjectName,
                        Topics = existingGroup.Topics
                    };

                    // 부모 과목의 오늘 학습시간 설정
                    updatedTopicGroup.SetParentTodayStudyTime(subjectSeconds);

                    topicGroupsData.Add(updatedTopicGroup);

                    System.Diagnostics.Debug.WriteLine($"[Progress] 분류 '{updatedTopicGroup.GroupTitle}' 시간 업데이트: {categorySeconds}초");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Progress] 과목 '{subject.SubjectName}' TopicGroups 업데이트 오류: {ex.Message}");
                // 오류 발생 시 기존 TopicGroups 유지
                topicGroupsData = subject.TopicGroups;
            }

            // ✅ 핵심 수정: UpdateFromDatabase 메서드 호출
            subject.UpdateFromDatabase(subjectSeconds, topicGroupsData);

            System.Diagnostics.Debug.WriteLine($"[Progress] 과목 '{subject.SubjectName}' 업데이트 완료: {subjectSeconds}초, TopicGroups: {topicGroupsData.Count}개");
        }

        // 전체 통계 업데이트
        OnPropertyChanged(nameof(TotalStudyTimeDisplay));

        System.Diagnostics.Debug.WriteLine($"[Progress] 전체 업데이트 완료 - 총 시간: {TimeSpan.FromSeconds(totalTodaySeconds):hh\\:mm\\:ss}");
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"[Progress Error] 전체 진행률 업데이트 실패: {ex.Message}");
    }
}

        private int GetTopicGroupTimeByName(DateTime date, string subjectName, string groupTitle)
        {
            try
            {
                // DailyTopicGroup 테이블에서 조회
                return Notea.Modules.Common.Helpers.DatabaseHelper.Instance.GetDailyTopicGroupStudyTimeSeconds(date, subjectName, groupTitle);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Progress] TopicGroup 시간 조회 오류: {ex.Message}");
                return 0;
            }
        }

        // ✅ 신규: 누락된 메소드 구현


        // 🆕 저장된 Daily Subject 데이터 복원 메소드 (실제 측정 시간만)
        private void RestoreDailySubjects()
        {
            try
            {
                // ✅ DailyBodyViewModel 데이터 로딩 트리거 (지연 로딩)
                _dailyBodyVM?.InitializeDataWhenReady();

                System.Diagnostics.Debug.WriteLine("[MainViewModel] 일일 과목 복원 완료");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainViewModel] 일일 과목 복원 오류: {ex.Message}");
            }
        }

        

        public void OnDateSelected(DateTime date)
        {
            _dailyBodyVM.LoadDailyData(date);
        }

        public void Cleanup()
        {
            try
            {
                if (_rightSidebarViewModel != null && _progressUpdateSubscribed)
                {
                    _rightSidebarViewModel.ProgressUpdateRequested -= OnProgressUpdateRequested;
                    _progressUpdateSubscribed = false;
                    System.Diagnostics.Debug.WriteLine("[MainViewModel] 진행률 업데이트 이벤트 구독 해제");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainViewModel] 정리 오류: {ex.Message}");
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
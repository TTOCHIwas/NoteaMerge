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

        // 🆕 공유 데이터 소스 - 두 페이지에서 모두 사용 (실제 측정 시간만)
        public ObservableCollection<SubjectProgressViewModel> SharedSubjectProgress { get; set; }

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

        public bool IsSidebarCollapsed => LeftSidebarWidth.Value == 0;

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
            // 🆕 공유 데이터 소스 초기화 (실제 측정 시간만)
            SharedSubjectProgress = new ObservableCollection<SubjectProgressViewModel>();

            // 사이드바 ViewModel 초기화
            SidebarViewModel = new LeftSidebarViewModel("main");

            // ViewModel들 생성 (한 번만) - 🚨 skipInitialLoad: true 추가
            _dailyHeaderVM = new DailyHeaderViewModel();
            _dailyBodyVM = new DailyBodyViewModel(AppStartDate, skipInitialLoad: true); // ✅ 초기 로딩 스킵
            _subjectListPageVM = new SubjectListPageViewModel();
            _monthlyPlanVM = new MonthlyPlanViewModel();
            _yearMonthListVM = new YearMonthListViewModel();


            // 🆕 DailyBodyViewModel의 Subjects를 공유 데이터로 교체
            _dailyBodyVM.SetSharedSubjects(SharedSubjectProgress);

            // View들 생성 및 DataContext 설정 (한 번만)
            _dailyHeaderView = new DailyHeaderView { DataContext = _dailyHeaderVM };
            _dailyBodyView = new DailyBodyView { DataContext = _dailyBodyVM };
            _subjectHeaderView = new SubjectListPageHeaderView();
            _subjectBodyView = new SubjectListPageBodyView { DataContext = _subjectListPageVM };

            // ✅ 수정: CalendarMonth에 올바른 ViewModel 연결
            _calendarMonthView = new CalendarMonth { DataContext = _monthlyPlanVM };

            // ✅ 수정: YearMonthListView DataContext 제거 (생성자에서 이미 설정됨)
            _yearMonthListView = new YearMonthListView { DataContext =_yearMonthListVM};

            // 초기 화면 설정 (Daily 화면)
            HeaderContent = _dailyHeaderView;
            BodyContent = _dailyBodyView;

            // Commands 초기화
            ToggleSidebarCommand = new RelayCommand(ToggleSidebar);
            ExpandSidebarCommand = new RelayCommand(() => LeftSidebarWidth = new GridLength(280));
            NavigateToSubjectListCommand = new RelayCommand(NavigateToSubjectList);
            NavigateToTodayCommand = new RelayCommand(NavigateToToday);
            NavigateToCalendarCommand = new RelayCommand<string>(NavigateToCalendar);
            NavigateToYearlyCommand = new RelayCommand(NavigateToYearly);
            NavigateToDailyViewForDateCommand = new RelayCommand<DateTime>(NavigateToDailyViewForDate);
            NavigateToCalendarFromYearlyCommand = new RelayCommand<YearMonthViewModel>(NavigateToCalendarFromYearly);

            // ✅ 수정: 데이터베이스 중복 초기화 제거
            try
            {
                RestoreDailySubjects();
                // SetupProgressUpdateSystem(); // 🚨 이 줄도 임시 주석 처리

                System.Diagnostics.Debug.WriteLine("[MainViewModel] 초기화 완료 (데이터 로딩 스킵됨)");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainViewModel] 초기화 오류: {ex.Message}");
            }
        }

        private void NavigateToSubjectList()
        {
            try
            {
                IsHeaderVisible = true;
                HeaderContent = _subjectHeaderView;
                BodyContent = _subjectBodyView;
                SidebarViewModel.SetContext("today"); 
                System.Diagnostics.Debug.WriteLine("[MainViewModel] 과목 목록 페이지로 이동");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainViewModel] 과목 목록 이동 오류: {ex.Message}");
            }
        }

        private void NavigateToToday()
        {
            try
            {
                IsHeaderVisible=true; // 헤더 표시
                HeaderContent = _dailyHeaderView;
                BodyContent = _dailyBodyView;
                SidebarViewModel.SetContext("main");

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
                    _dailyHeaderVM.SetSelectedDate(selectedDate); // 이 메서드 구현 필요
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

                // 캘린더 데이터 새로고침
                _calendarMonthView.LoadEvents();
                _calendarMonthView.LoadMonthComment();

                // 사이드바 컨텍스트 변경
                SidebarViewModel.SetContext("calendar");

                System.Diagnostics.Debug.WriteLine($"[Navigation] 캘린더 이동 완료 - {year}년 {selectedMonth.Month}월");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Navigation] 연간→캘린더 이동 오류: {ex.Message}");
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
                cmd.CommandText = @"
                    SELECT c.categoryId 
                    FROM category c 
                    INNER JOIN subject s ON c.subJectId = s.subJectId 
                    WHERE c.title = @groupTitle AND s.title = @subjectName";

                cmd.Parameters.AddWithValue("@groupTitle", groupTitle);
                cmd.Parameters.AddWithValue("@subjectName", subjectName);

                var result = cmd.ExecuteScalar();
                return result != null ? Convert.ToInt32(result) : 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DB Error] CategoryId 조회 실패: {ex.Message}");
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

                // 1. 모든 과목의 오늘 학습시간 업데이트
                foreach (var subject in SharedSubjectProgress)
                {
                    // 과목별 실제 측정 시간 조회
                    var subjectSeconds = Notea.Modules.Common.Helpers.DatabaseHelper.Instance.GetSubjectDailyTimeSeconds(today, subject.SubjectName);
                    subject.TodayStudyTimeSeconds = subjectSeconds;
                    totalTodaySeconds += subjectSeconds;

                    System.Diagnostics.Debug.WriteLine($"[Progress] 과목 '{subject.SubjectName}' 업데이트: {subjectSeconds}초");

                    // 2. 각 과목의 TopicGroups 업데이트
                    foreach (var topicGroup in subject.TopicGroups)
                    {
                        // 분류별 실제 측정 시간 조회
                        var categorySeconds = topicGroup.CategoryId > 0
                            ? Notea.Modules.Common.Helpers.DatabaseHelper.Instance.GetCategoryDailyTimeSeconds(today, topicGroup.CategoryId)
                            : GetTopicGroupTimeByName(today, subject.SubjectName, topicGroup.GroupTitle);

                        // TopicGroup 시간 업데이트
                        topicGroup.SetParentTodayStudyTime(subjectSeconds);

                        // 실시간 표시 업데이트
                        if (topicGroup.GetType().GetMethod("UpdateRealTimeDisplay") != null)
                        {
                            topicGroup.GetType().GetMethod("UpdateRealTimeDisplay").Invoke(topicGroup, null);
                        }

                        System.Diagnostics.Debug.WriteLine($"[Progress] 분류 '{topicGroup.GroupTitle}' 업데이트: {categorySeconds}초, 진행률: {topicGroup.ProgressRatio:P1}");
                    }
                }

                // 3. 전체 통계 업데이트
                OnPropertyChanged(nameof(TotalStudyTimeDisplay));

                System.Diagnostics.Debug.WriteLine($"[Progress] 전체 업데이트 완료 - 총 시간: {TimeSpan.FromSeconds(totalTodaySeconds):hh\\:mm\\:ss}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Progress Error] 전체 진행률 업데이트 실패: {ex.Message}");
            }
        }

        // ✅ 신규: 누락된 메소드 구현
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

        private void ToggleSidebar()
        {
            LeftSidebarWidth = LeftSidebarWidth.Value == 0
                ? new GridLength(280)
                : new GridLength(0);
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

        // INotifyPropertyChanged 구현
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
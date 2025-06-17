using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Notea.Modules.Common.ViewModels;
using Notea.Modules.Daily.ViewModels;
using Notea.Modules.Daily.Views;
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

        // View들 (한 번만 생성)
        private readonly DailyHeaderView _dailyHeaderView;
        private readonly DailyBodyView _dailyBodyView;
        private readonly SubjectListPageHeaderView _subjectHeaderView;
        private readonly SubjectListPageBodyView _subjectBodyView;

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
                }
            }
        }

        public ICommand ToggleSidebarCommand { get; }
        public ICommand ExpandSidebarCommand { get; }

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

            // ViewModel들 생성 (한 번만)
            _dailyHeaderVM = new DailyHeaderViewModel();
            _dailyBodyVM = new DailyBodyViewModel(AppStartDate);
            _subjectListPageVM = new SubjectListPageViewModel();

            // 🆕 DailyBodyViewModel의 Subjects를 공유 데이터로 교체
            _dailyBodyVM.SetSharedSubjects(SharedSubjectProgress);

            // View들 생성 및 DataContext 설정 (한 번만)
            _dailyHeaderView = new DailyHeaderView { DataContext = _dailyHeaderVM };
            _dailyBodyView = new DailyBodyView { DataContext = _dailyBodyVM };
            _subjectHeaderView = new SubjectListPageHeaderView();
            _subjectBodyView = new SubjectListPageBodyView { DataContext = _subjectListPageVM };

            // 초기 화면 설정 (Daily 화면)
            HeaderContent = _dailyHeaderView;
            BodyContent = _dailyBodyView;

            // Commands 초기화
            ToggleSidebarCommand = new RelayCommand(ToggleSidebar);
            ExpandSidebarCommand = new RelayCommand(() => LeftSidebarWidth = new GridLength(280));

            // ✅ 수정: 초기화 순서 변경
            try
            {
                // 1. 저장된 데이터 복원
                RestoreDailySubjects();

                // 2. 진행률 업데이트 시스템 설정
                SetupProgressUpdateSystem();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainViewModel] 초기화 오류: {ex.Message}");
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
                var dbHelper = Notea.Modules.Common.Helpers.DatabaseHelper.Instance;

                // ✅ 오늘 총 공부시간 먼저 설정
                int todayTotalSeconds = dbHelper.GetTotalStudyTimeSeconds(AppStartDate);
                SubjectProgressViewModel.SetTodayTotalStudyTime(todayTotalSeconds);

                var dailySubjects = dbHelper.GetDailySubjects(AppStartDate);

                foreach (var (subjectName, progress, studyTimeSeconds) in dailySubjects)
                {
                    var existingSubject = SharedSubjectProgress.FirstOrDefault(s =>
                        string.Equals(s.SubjectName, subjectName, StringComparison.OrdinalIgnoreCase));

                    if (existingSubject == null)
                    {
                        // ✅ 실제 측정된 시간만으로 생성
                        SharedSubjectProgress.Add(new SubjectProgressViewModel
                        {
                            SubjectName = subjectName,
                            TodayStudyTimeSeconds = studyTimeSeconds // ✅ 실제 측정된 시간만
                        });
                    }
                    else
                    {
                        // 기존 항목 업데이트
                        existingSubject.TodayStudyTimeSeconds = studyTimeSeconds;
                    }
                }

                System.Diagnostics.Debug.WriteLine($"[MainViewModel] 앱 시작 시 {SharedSubjectProgress.Count}개 DailySubject 복원됨 (총 {todayTotalSeconds}초)");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainViewModel] DailySubject 복원 오류: {ex.Message}");
            }
        }

        // ✅ 과목페이지에서 호출될 메소드 (추후 구현) - 해당 과목의 실시간 시간 증가
        public void OnSubjectPageEntered(string subjectName)
        {
            var subject = SharedSubjectProgress.FirstOrDefault(s =>
                string.Equals(s.SubjectName, subjectName, StringComparison.OrdinalIgnoreCase));

            if (subject != null)
            {
                // ✅ 타이머가 실행중일 때만 시간 증가 (추후 RightSidebarViewModel과 연동)
                System.Diagnostics.Debug.WriteLine($"[MainViewModel] 과목페이지 진입: {subjectName}");
                // subject.IncrementRealTimeStudy(); // 매초 호출될 예정
            }
        }

        // ✅ 분류그룹에서 활동시 호출될 메소드 (추후 구현) - 해당 분류의 실시간 시간 증가
        public void OnTopicGroupActivity(string subjectName, string groupTitle)
        {
            var subject = SharedSubjectProgress.FirstOrDefault(s =>
                string.Equals(s.SubjectName, subjectName, StringComparison.OrdinalIgnoreCase));

            if (subject != null)
            {
                var topicGroup = subject.TopicGroups.FirstOrDefault(tg =>
                    string.Equals(tg.GroupTitle, groupTitle, StringComparison.OrdinalIgnoreCase));

                if (topicGroup != null)
                {
                    // ✅ 타이머가 실행중일 때만 시간 증가 (추후 RightSidebarViewModel과 연동)
                    System.Diagnostics.Debug.WriteLine($"[MainViewModel] 분류그룹 활동: {subjectName} > {groupTitle}");
                    // topicGroup.IncrementRealTimeStudy(); // 매초 호출될 예정
                }
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
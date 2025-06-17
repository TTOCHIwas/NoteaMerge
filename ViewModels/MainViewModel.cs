using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
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
                    OnPropertyChanged(nameof(IsSidebarCollapsed));
                }
            }
        }

        public bool IsSidebarCollapsed => LeftSidebarWidth.Value == 0;

        public ICommand ToggleSidebarCommand { get; }
        public ICommand ExpandSidebarCommand { get; }
        public ICommand NavigateToSubjectListCommand { get; }
        public ICommand NavigateToTodayCommand { get; }

        // 헤더/본문 컨텐츠 프로퍼티
        private object _headerContent;
        public object HeaderContent
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

        private object _bodyContent;
        public object BodyContent
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

            NavigateToTodayCommand = new RelayCommand(() =>
            {
                HeaderContent = _dailyHeaderView;
                BodyContent = _dailyBodyView;
                SidebarViewModel.SetContext("main");

                // 현재 날짜로 데이터 로드 - 강제 리로드
                _dailyBodyVM.LoadDailyData(AppStartDate);

                System.Diagnostics.Debug.WriteLine($"[MainViewModel] Today 페이지로 전환 - 공유 데이터 항목 수: {SharedSubjectProgress.Count}");
            });

            NavigateToSubjectListCommand = new RelayCommand(() =>
            {
                HeaderContent = _subjectHeaderView;
                BodyContent = _subjectBodyView;

                // 과목 페이지로 전환할 때 사이드바 컨텍스트 변경
                SidebarViewModel.SetContext("today");

                // 공유 데이터 설정
                SidebarViewModel.SetSharedSubjectProgress(SharedSubjectProgress);

                System.Diagnostics.Debug.WriteLine($"[MainViewModel] 과목페이지로 전환 - 공유 데이터 항목 수: {SharedSubjectProgress.Count}");
            });

            // 🆕 앱 시작 시 저장된 Daily Subject 데이터 복원 (실제 측정 시간만)
            RestoreDailySubjects();
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

        // INotifyPropertyChanged 구현
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
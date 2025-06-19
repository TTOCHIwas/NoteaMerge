using System.Collections.ObjectModel;
using System.Windows.Input;
using Notea.Modules.Subjects.Models;
using Notea.ViewModels;

namespace Notea.Modules.Subjects.ViewModels
{
    public class TopicGroupViewModel : ViewModelBase
    {
        private int _cachedTodayStudyTimeSeconds = -1;
        private DateTime _lastCacheDate = DateTime.MinValue;

        public string GroupTitle { get; set; } = string.Empty;
        public string ParentSubjectName { get; set; } = string.Empty;
        public ObservableCollection<TopicItem> Topics { get; set; } = new();

        private bool _isExpanded = false;
        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }

        private bool _isCompleted = false;
        public bool IsCompleted
        {
            get => _isCompleted;
            set
            {
                if (SetProperty(ref _isCompleted, value))
                {
                    SaveCheckStateToDatabase();
                }
            }
        }

        public ICommand ToggleCommand { get; }

        // 실시간 진행률 속성
        private double _realTimeProgressPercentage = 0.0;
        public double RealTimeProgressPercentage
        {
            get => _realTimeProgressPercentage;
            set => SetProperty(ref _realTimeProgressPercentage, value);
        }

        private string _realTimeStudyTimeDisplay = "00:00:00";
        public string RealTimeStudyTimeDisplay
        {
            get => _realTimeStudyTimeDisplay;
            set => SetProperty(ref _realTimeStudyTimeDisplay, value);
        }

        public string ProgressRatioPercentText => $"{ProgressRatio:P1}";

        private int _categoryId = 0;
        public int CategoryId
        {
            get => _categoryId;
            set => SetProperty(ref _categoryId, value);
        }

        private int _parentTodayStudyTimeSeconds = 0;

        public TopicGroupViewModel()
        {
            ToggleCommand = new RelayCommand(() => IsExpanded = !IsExpanded);
            Topics.Clear();
        }

        // ✅ 분류별 오늘 학습시간 - StudySession에서 실시간 조회
        public int TodayStudyTimeSeconds
        {
            get
            {
                if (string.IsNullOrEmpty(ParentSubjectName) || string.IsNullOrEmpty(GroupTitle))
                    return 0;

                // 캐시 유효성 검사
                if (_lastCacheDate.Date == DateTime.Today.Date && _cachedTodayStudyTimeSeconds >= 0)
                {
                    return _cachedTodayStudyTimeSeconds;
                }

                try
                {
                    var dbHelper = Notea.Modules.Common.Helpers.DatabaseHelper.Instance;
                    var actualTime = CategoryId > 0
                        ? dbHelper.GetCategoryDailyTimeSeconds(DateTime.Today, CategoryId)
                        : dbHelper.GetTopicGroupDailyTimeSecondsByName(DateTime.Today, ParentSubjectName, GroupTitle);

                    // 캐시 업데이트
                    _cachedTodayStudyTimeSeconds = actualTime;
                    _lastCacheDate = DateTime.Today;

                    // 로그는 캐시 갱신 시에만
                    System.Diagnostics.Debug.WriteLine($"[TopicGroup] {ParentSubjectName}>{GroupTitle} 시간 캐시 갱신: {actualTime}초");
                    return actualTime;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[TopicGroup] {ParentSubjectName}>{GroupTitle} 시간 조회 오류: {ex.Message}");
                    return 0;
                }
            }
            set
            {
                // 캐시 업데이트 (DB 저장은 필요시에만)
                _cachedTodayStudyTimeSeconds = value;
                _lastCacheDate = DateTime.Today;
                OnPropertyChanged(nameof(TodayStudyTimeSeconds));
            }
        }

        public void RefreshStudyTimeCache()
        {
            _cachedTodayStudyTimeSeconds = -1;
            _lastCacheDate = DateTime.MinValue;
            OnPropertyChanged(nameof(TodayStudyTimeSeconds));
        }

        public void SetCachedStudyTime(int seconds)
        {
            _cachedTodayStudyTimeSeconds = seconds;
            _lastCacheDate = DateTime.Today;
            OnPropertyChanged(nameof(TodayStudyTimeSeconds));
        }

        // ✅ 호환성을 위한 프로퍼티
        public int TotalStudyTime
        {
            get => TodayStudyTimeSeconds;
            set => TodayStudyTimeSeconds = value;
        }

        // ✅ 메인 프로퍼티: 초단위 분류별 학습시간
        public int TotalStudyTimeSeconds
        {
            get
            {
                if (CategoryId > 0)
                {
                    try
                    {
                        var dbHelper = Notea.Modules.Common.Helpers.DatabaseHelper.Instance;
                        return dbHelper.GetCategoryStudyTimeSeconds(CategoryId);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[TopicGroup] 학습시간 조회 오류: {ex.Message}");
                        return 0;
                    }
                }
                return 0;
            }
            set
            {
                // setter는 UI 바인딩용으로만 사용, 실제 데이터는 데이터베이스에서 관리
                OnPropertyChanged(nameof(TotalStudyTimeSeconds));
                OnPropertyChanged(nameof(StudyTimeText));
                OnPropertyChanged(nameof(ProgressRatio));
            }
        }

        public void SetParentTodayStudyTime(int parentTodayTimeSeconds)
        {
            _parentTodayStudyTimeSeconds = parentTodayTimeSeconds;
            OnPropertyChanged(nameof(ProgressRatio));
            OnPropertyChanged(nameof(StudyTimeTooltip));
            OnPropertyChanged(nameof(ProgressRatioPercentText));

            System.Diagnostics.Debug.WriteLine($"[TopicGroup] {GroupTitle} 부모 오늘 시간 설정: {parentTodayTimeSeconds}초");
            System.Diagnostics.Debug.WriteLine($"[TopicGroup] {GroupTitle} 업데이트된 ProgressRatio: {ProgressRatio:P2}");
        }

        // ✅ 전체 과목 학습 시간 (외부에서 주입) - 호환성용
        private int _subjectTotalTimeSeconds;
        public void SetSubjectTotalTime(int subjectTimeSeconds)
        {
            _subjectTotalTimeSeconds = subjectTimeSeconds;
        }

        // ✅ 부모 과목의 오늘 시간 대비 이 분류의 비율 (0.0 ~ 1.0)
        public double ProgressRatio
        {
            get
            {
                if (_parentTodayStudyTimeSeconds == 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[TopicGroup] {GroupTitle} ProgressRatio 계산 실패: 부모의 오늘 학습시간이 0입니다.");
                    return 0.0;
                }

                var myTime = TodayStudyTimeSeconds; // 실시간 조회
                var ratio = (double)myTime / _parentTodayStudyTimeSeconds;
                return Math.Min(1.0, ratio); // 100% 이상은 100%로 제한
            }
        }

        // ✅ 학습 시간을 00:00:00 형식으로 표시
        public string StudyTimeText
        {
            get
            {
                var totalSeconds = TodayStudyTimeSeconds; // 실시간 조회
                var hours = totalSeconds / 3600;
                var minutes = (totalSeconds % 3600) / 60;
                var seconds = totalSeconds % 60;
                return $"{hours:D2}:{minutes:D2}:{seconds:D2}";
            }
        }

        public string StudyTimeTooltip
        {
            get => $"{GroupTitle}: {ProgressRatioPercentText} - {RealTimeStudyTimeDisplay}";
        }

        public void UpdateRealTimeDisplay()
        {
            try
            {
                var totalSeconds = TodayStudyTimeSeconds;
                var timeSpan = TimeSpan.FromSeconds(totalSeconds);
                RealTimeStudyTimeDisplay = timeSpan.ToString(@"hh\:mm\:ss");

                // 진행률도 함께 업데이트
                RealTimeProgressPercentage = ProgressRatio * 100;

                // UI 속성들 새로고침
                OnPropertyChanged(nameof(StudyTimeText));
                OnPropertyChanged(nameof(StudyTimeTooltip));
                OnPropertyChanged(nameof(ProgressRatioPercentText));
                OnPropertyChanged(nameof(ProgressRatio));
                OnPropertyChanged(nameof(RealTimeStudyTimeDisplay));
                OnPropertyChanged(nameof(RealTimeProgressPercentage));

                System.Diagnostics.Debug.WriteLine($"[TopicGroup] {GroupTitle} 실시간 표시 업데이트: {RealTimeStudyTimeDisplay}, 진행률: {ProgressRatio:P1}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TopicGroup] 실시간 표시 업데이트 오류: {ex.Message}");
            }
        }
        // ✅ 분류에서 노션처럼 공부할 때 호출될 메소드 (추후 과목페이지에서 사용)
        public void AddStudyTime(int seconds)
        {
            try
            {
                if (CategoryId > 0)
                {
                    var dbHelper = Notea.Modules.Common.Helpers.DatabaseHelper.Instance;

                    // 1. StudySession에 기록 (기존과 동일)
                    dbHelper.SaveStudySession(
                        DateTime.Now.AddSeconds(-seconds),
                        DateTime.Now,
                        seconds,
                        ParentSubjectName,
                        GroupTitle,
                        CategoryId
                    );

                    // 2. ✅ 새로 추가: category 테이블의 TotalStudyTimeSeconds 업데이트
                    dbHelper.UpdateCategoryStudyTimeSeconds(CategoryId, seconds);

                    // 3. 실시간 표시 업데이트
                    UpdateRealTimeDisplay();
                    System.Diagnostics.Debug.WriteLine($"[TopicGroup] {GroupTitle} 학습시간 추가: {seconds}초 (CategoryId: {CategoryId})");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TopicGroup] 학습시간 추가 오류: {ex.Message}");
            }
        }



        public void IncrementRealTimeStudy()
        {
            AddStudyTime(1); // 1초씩 증가
        }

        // ✅ 체크 상태를 DB에 저장하는 메소드
        private void SaveCheckStateToDatabase()
        {
            try
            {
                if (!string.IsNullOrEmpty(ParentSubjectName) && !string.IsNullOrEmpty(GroupTitle))
                {
                    var dbHelper = Notea.Modules.Common.Helpers.DatabaseHelper.Instance;
                    dbHelper.UpdateDailyTopicGroupCompletion(DateTime.Today, ParentSubjectName, GroupTitle, IsCompleted);
                    System.Diagnostics.Debug.WriteLine($"[TopicGroup] 체크 상태 저장: {GroupTitle} = {IsCompleted}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TopicGroup] 체크 상태 저장 오류: {ex.Message}");
            }
        }
    }
}
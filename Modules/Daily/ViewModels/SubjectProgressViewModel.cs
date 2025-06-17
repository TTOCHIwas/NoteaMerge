using Notea.Modules.Subjects.ViewModels;
using System.Collections.ObjectModel;
using Notea.ViewModels;
using System;

namespace Notea.Modules.Daily.ViewModels
{
    public class SubjectProgressViewModel : ViewModelBase
    {
        private string _subjectName = string.Empty;
        public string SubjectName
        {
            get => _subjectName;
            set => SetProperty(ref _subjectName, value);
        }



        // 실시간 진행률 속성 (SubjectProgressViewModel.cs에 추가)
        private double _realTimeProgressPercentage = 0.0;
        public double RealTimeProgressPercentage
        {
            get => _realTimeProgressPercentage;
            set => SetProperty(ref _realTimeProgressPercentage, value);
        }

        // 실시간 학습시간 표시 속성
        private string _realTimeStudyTimeDisplay = "00:00:00";
        public string RealTimeStudyTimeDisplay
        {
            get => _realTimeStudyTimeDisplay;
            set => SetProperty(ref _realTimeStudyTimeDisplay, value);
        }

        // ✅ 진행률은 실시간으로 계산 (DailySubject에 의존하지 않음)
        public double ActualProgress => CalculateTimeBasedProgress();

        // ✅ 오늘 총 공부시간 (모든 과목 합계) - 정적 변수
        private static int _todayTotalStudyTimeSeconds = 0;

        public static void SetTodayTotalStudyTime(int totalSeconds)
        {
            _todayTotalStudyTimeSeconds = totalSeconds;
            System.Diagnostics.Debug.WriteLine($"[SubjectProgress] 오늘 총 공부시간 설정: {totalSeconds}초 ({totalSeconds / 3600.0:F1}시간)");
        }

        // ✅ 실시간 계산: DB에서 직접 해당 과목의 오늘 시간 가져오기
        // ✅ 실제 측정 시간 - 항상 StudySession에서 실시간 조회
        public int TodayStudyTimeSeconds
        {
            get
            {
                if (string.IsNullOrEmpty(SubjectName))
                    return 0;

                try
                {
                    var dbHelper = Notea.Modules.Common.Helpers.DatabaseHelper.Instance;

                    // ✅ StudySession에서 직접 실제 측정 시간 조회
                    var actualTime = dbHelper.GetSubjectActualDailyTimeSeconds(DateTime.Today, SubjectName);

                    System.Diagnostics.Debug.WriteLine($"[SubjectProgress] {SubjectName} 실제 측정 시간: {actualTime}초");
                    return actualTime;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[SubjectProgress] {SubjectName} 시간 조회 오류: {ex.Message}");
                    return 0;
                }
            }
            set
            {
                // DailySubject 업데이트 (UI 표시용)
                try
                {
                    var dbHelper = Notea.Modules.Common.Helpers.DatabaseHelper.Instance;
                    dbHelper.SaveDailySubject(DateTime.Today, SubjectName, ActualProgress, value, 0);

                    OnPropertyChanged(nameof(TodayStudyTimeSeconds));
                    OnPropertyChanged(nameof(StudyTimeText));
                    OnPropertyChanged(nameof(StudyTimeMinutes));
                    OnPropertyChanged(nameof(ActualProgress));
                    OnPropertyChanged(nameof(ProgressWidth));
                    OnPropertyChanged(nameof(ProgressPercentText));
                    OnPropertyChanged(nameof(Tooltip));

                    UpdateTopicGroupsParentTime();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[SubjectProgress] {SubjectName} 시간 저장 오류: {ex.Message}");
                }
            }
        }

        // ✅ 실제 측정 시간 기반 진행률 계산
        private double CalculateTimeBasedProgress()
        {
            if (_todayTotalStudyTimeSeconds == 0)
                return 0.0;

            var todayTime = TodayStudyTimeSeconds; // 실시간 계산
            var ratio = (double)todayTime / _todayTotalStudyTimeSeconds;
            return Math.Min(1.0, ratio); // 최대 100%로 제한
        }

        // ✅ 호환성을 위한 프로퍼티 (기존 코드들이 분 단위로 접근)
        public int StudyTimeMinutes
        {
            get => TodayStudyTimeSeconds / 60;
            set => TodayStudyTimeSeconds = value * 60;
        }

        // ✅ TopicGroups에게 부모의 오늘 학습시간 전달
        private void UpdateTopicGroupsParentTime()
        {
            foreach (var topicGroup in TopicGroups)
            {
                topicGroup.SetParentTodayStudyTime(TodayStudyTimeSeconds);
            }
        }

        // ✅ 시간 표시 텍스트 (00:00:00 형식)
        public string StudyTimeText
        {
            get
            {
                var totalSeconds = TodayStudyTimeSeconds;
                var hours = totalSeconds / 3600;
                var minutes = (totalSeconds % 3600) / 60;
                var seconds = totalSeconds % 60;
                return $"{hours:D2}:{minutes:D2}:{seconds:D2}";
            }
        }

        // Progress Bar 너비 계산 (동적 계산)
        private double _maxWidth = 200;
        public double MaxWidth
        {
            get => _maxWidth;
            set
            {
                if (SetProperty(ref _maxWidth, value))
                {
                    OnPropertyChanged(nameof(ProgressWidth));
                }
            }
        }

        // ✅ 실제 측정된 진행률 기반 너비
        public double ProgressWidth => MaxWidth * ActualProgress;

        // ✅ 실제 측정된 진행률 퍼센트 텍스트
        public string ProgressPercentText
        {
            get => $"{ActualProgress:P0}";
        }

        // ✅ Tooltip에 실제 진행률 표시
        public string Tooltip
        {
            get => $"{SubjectName}: {ProgressPercentText} - {StudyTimeText}";
        }

        // TopicGroup 리스트 (드래그 앤 드롭으로 추가된 분류들)
        public ObservableCollection<TopicGroupViewModel> TopicGroups { get; set; } = new();

        // 무한 루프 방지를 위한 플래그들
        public bool _isUpdatingFromDatabase = false;
        public bool _isSavingToDatabase = false;

        public SubjectProgressViewModel()
        {
            // 초기값 설정
            // TodayStudyTimeSeconds는 프로퍼티에서 실시간 계산

            // TopicGroups 변경 감지
            TopicGroups.CollectionChanged += (s, e) =>
            {
                if (_isUpdatingFromDatabase || _isSavingToDatabase)
                {
                    System.Diagnostics.Debug.WriteLine($"[SubjectProgress] {SubjectName} TopicGroups 변경 무시됨");
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"[SubjectProgress] {SubjectName}의 TopicGroups 변경됨. 현재 개수: {TopicGroups.Count}");
                SaveToDatabase();
            };
        }

        // ✅ DB에서 데이터를 업데이트할 때 사용하는 메소드
        public void UpdateFromDatabase(int studyTimeSeconds, ObservableCollection<TopicGroupViewModel> topicGroups)
        {
            _isUpdatingFromDatabase = true;
            try
            {
                // DailySubject에 시간 저장 (실시간 계산을 위해)
                var dbHelper = Notea.Modules.Common.Helpers.DatabaseHelper.Instance;
                dbHelper.SaveDailySubject(DateTime.Today, SubjectName, ActualProgress, studyTimeSeconds, 0);

                // TopicGroups 업데이트
                TopicGroups.Clear();
                foreach (var group in topicGroups)
                {
                    TopicGroups.Add(group);
                }

                // UI 업데이트
                OnPropertyChanged(nameof(TodayStudyTimeSeconds));
                OnPropertyChanged(nameof(StudyTimeText));
                OnPropertyChanged(nameof(ActualProgress));

                System.Diagnostics.Debug.WriteLine($"[SubjectProgress] {SubjectName} DB에서 업데이트됨: {TopicGroups.Count}개 그룹, 오늘시간: {studyTimeSeconds}초");
            }
            finally
            {
                _isUpdatingFromDatabase = false;
            }
        }

        // ✅ DB에 저장하는 메소드 - 실제 측정된 진행률로 저장
        private void SaveToDatabase()
        {
            if (_isSavingToDatabase || _isUpdatingFromDatabase)
            {
                System.Diagnostics.Debug.WriteLine($"[SubjectProgress] {SubjectName} 저장 스킵됨");
                return;
            }

            if (string.IsNullOrEmpty(SubjectName))
            {
                System.Diagnostics.Debug.WriteLine($"[SubjectProgress] SubjectName이 비어있어 저장 스킵됨");
                return;
            }

            _isSavingToDatabase = true;
            try
            {
                var dbHelper = Notea.Modules.Common.Helpers.DatabaseHelper.Instance;

                // ✅ 실제 측정된 진행률로 저장
                dbHelper.SaveDailySubjectWithTopicGroups(DateTime.Today, SubjectName, ActualProgress, TodayStudyTimeSeconds, 0, TopicGroups);

                System.Diagnostics.Debug.WriteLine($"[SubjectProgress] {SubjectName}과 TopicGroups({TopicGroups.Count}개) DB에 저장됨 (진행률: {ActualProgress:P1}, {TodayStudyTimeSeconds}초)");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SubjectProgress] DB 저장 오류: {ex.Message}");
            }
            finally
            {
                _isSavingToDatabase = false;
            }
        }

        // ✅ 과목페이지 진입시 호출될 메소드 - 실제 측정 시간 추가
        public void AddStudyTime(int seconds)
        {
            var currentTime = TodayStudyTimeSeconds;
            TodayStudyTimeSeconds = currentTime + Math.Max(0, seconds);
            System.Diagnostics.Debug.WriteLine($"[SubjectProgress] {SubjectName} 학습시간 추가: {seconds}초, 총: {TodayStudyTimeSeconds}초, 진행률: {ActualProgress:P1}");
        }

        // ✅ 타이머 기반 실시간 시간 증가 (매초 호출될 예정)
        public void IncrementRealTimeStudy()
        {
            AddStudyTime(1); // 1초씩 증가
        }

        // TopicGroup 추가 메소드
        public void AddTopicGroup(TopicGroupViewModel topicGroup)
        {
            if (topicGroup != null && !TopicGroups.Contains(topicGroup))
            {
                topicGroup.ParentSubjectName = SubjectName;
                topicGroup.SetParentTodayStudyTime(TodayStudyTimeSeconds);

                _isUpdatingFromDatabase = true;
                try
                {
                    TopicGroups.Add(topicGroup);
                    System.Diagnostics.Debug.WriteLine($"[SubjectProgress] {SubjectName}에 TopicGroup '{topicGroup.GroupTitle}' 추가됨");
                }
                finally
                {
                    _isUpdatingFromDatabase = false;
                }
            }
        }

        // TopicGroup 제거 메소드
        public void RemoveTopicGroup(TopicGroupViewModel topicGroup)
        {
            if (topicGroup != null && TopicGroups.Contains(topicGroup))
            {
                _isUpdatingFromDatabase = true;
                try
                {
                    TopicGroups.Remove(topicGroup);
                    System.Diagnostics.Debug.WriteLine($"[SubjectProgress] {SubjectName}에서 TopicGroup '{topicGroup.GroupTitle}' 제거됨");
                }
                finally
                {
                    _isUpdatingFromDatabase = false;
                }
            }
        }

        public override string ToString()
        {
            return $"{SubjectName} - Progress: {ActualProgress:P1} ({StudyTimeText}) [TopicGroups: {TopicGroups.Count}]";
        }
    }
}
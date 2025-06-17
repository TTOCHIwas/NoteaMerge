using System.Collections.ObjectModel;
using System.Windows.Input;
using Notea.ViewModels;
using Notea.Modules.Subjects.ViewModels;

namespace Notea.Modules.Subjects.ViewModels
{
    public class SubjectGroupViewModel : ViewModelBase
    {
        public string SubjectName { get; set; } = string.Empty;
        public int SubjectId { get; set; }
        public ObservableCollection<TopicGroupViewModel> TopicGroups { get; set; } = new();

        private bool _isExpanded = false;
        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }

        public ICommand ToggleCommand { get; }

        public SubjectGroupViewModel()
        {
            ToggleCommand = new RelayCommand(() => IsExpanded = !IsExpanded);
        }

        // ✅ 메인 프로퍼티: 과목별 학습시간 (초 단위)
        private int _totalStudyTimeSeconds;
        public int TotalStudyTimeSeconds
        {
            get => _totalStudyTimeSeconds;
            set
            {
                if (SetProperty(ref _totalStudyTimeSeconds, value))
                {
                    OnPropertyChanged(nameof(TotalStudyTime)); // 호환성
                    OnPropertyChanged(nameof(ProgressRatio));
                    OnPropertyChanged(nameof(ProgressWidth));
                    OnPropertyChanged(nameof(StudyTimeText));
                }
            }
        }

        // ✅ 호환성을 위한 프로퍼티 (기존 코드들이 사용)
        public int TotalStudyTime
        {
            get => TotalStudyTimeSeconds;
            set => TotalStudyTimeSeconds = value;
        }

        // ✅ 전체 과목 학습시간 중 이 과목이 차지하는 비율 (초단위 기준)
        private static int _totalAllSubjectsTimeSeconds = 0;
        public static void SetGlobalTotalTime(int totalSeconds)
        {
            _totalAllSubjectsTimeSeconds = totalSeconds;
        }

        public double ProgressRatio => _totalAllSubjectsTimeSeconds > 0
            ? (double)TotalStudyTimeSeconds / _totalAllSubjectsTimeSeconds
            : 0;

        public double ProgressWidth => ProgressRatio * 200; // 200은 ProgressBar의 최대 너비

        // ✅ 시간 표시 텍스트 (00:00:00 형식)
        public string StudyTimeText
        {
            get
            {
                var hours = TotalStudyTimeSeconds / 3600;
                var minutes = (TotalStudyTimeSeconds % 3600) / 60;
                var seconds = TotalStudyTimeSeconds % 60;
                return $"{hours:D2}:{minutes:D2}:{seconds:D2}";
            }
        }

        public void NotifyProgressChanged()
        {
            OnPropertyChanged(nameof(ProgressRatio));
            OnPropertyChanged(nameof(ProgressWidth));
            OnPropertyChanged(nameof(StudyTimeText));
        }
    }
}
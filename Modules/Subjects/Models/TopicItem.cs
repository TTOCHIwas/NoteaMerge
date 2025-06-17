using System;
using System.ComponentModel;

namespace Notea.Modules.Subjects.Models
{
    public class TopicItem : INotifyPropertyChanged
    {
        public int Id { get; set; }
        public int SubjectId { get; set; }

        private string _name = string.Empty;
        public string Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged(nameof(Name));
                }
            }
        }

        public string Content
        {
            get => _name;
            set => Name = value;
        }

        private double _progress = 0.0;
        public double Progress
        {
            get => _progress;
            set
            {
                if (_progress != value)
                {
                    _progress = Math.Max(0.0, Math.Min(1.0, value));
                    OnPropertyChanged(nameof(Progress));
                    OnPropertyChanged(nameof(ProgressTooltip));
                    OnPropertyChanged(nameof(StudyTimeText));
                }
            }
        }

        // ✅ 메인 프로퍼티: 학습 시간 (초 단위)
        private int _studyTimeSeconds;
        public int StudyTimeSeconds
        {
            get => _studyTimeSeconds;
            set
            {
                if (_studyTimeSeconds != value)
                {
                    _studyTimeSeconds = value;
                    OnPropertyChanged(nameof(StudyTimeSeconds));
                    OnPropertyChanged(nameof(StudyTimeMinutes)); // 호환성
                    OnPropertyChanged(nameof(ProgressTooltip));
                    OnPropertyChanged(nameof(StudyTimeText));

                    // 학습 시간에 따라 Progress 자동 계산 (예: 7200초(2시간) = 100%)
                    if (_studyTimeSeconds > 0)
                    {
                        Progress = Math.Min(1.0, _studyTimeSeconds / 7200.0);
                    }
                }
            }
        }

        // ✅ 호환성을 위한 프로퍼티 (기존 코드들이 분 단위로 접근)
        public int StudyTimeMinutes
        {
            get => StudyTimeSeconds / 60;
            set => StudyTimeSeconds = value * 60;
        }

        private bool _isCompleted = false;
        public bool IsCompleted
        {
            get => _isCompleted;
            set
            {
                if (_isCompleted != value)
                {
                    _isCompleted = value;
                    OnPropertyChanged(nameof(IsCompleted));
                    SaveCheckStateToDatabase();
                }
            }
        }

        // 드래그 앤 드롭을 위한 부모 정보
        public string ParentTopicGroupName { get; set; } = string.Empty;
        public string ParentSubjectName { get; set; } = string.Empty;

        // ✅ Progress Bar Tooltip - 00:00:00 형식으로 수정
        public string ProgressTooltip
        {
            get
            {
                var hours = StudyTimeSeconds / 3600;
                var minutes = (StudyTimeSeconds % 3600) / 60;
                var seconds = StudyTimeSeconds % 60;
                return $"{hours:D2}:{minutes:D2}:{seconds:D2}";
            }
        }

        // ✅ 학습 시간 텍스트 (00:00:00 형식)
        public string StudyTimeText
        {
            get
            {
                var hours = StudyTimeSeconds / 3600;
                var minutes = (StudyTimeSeconds % 3600) / 60;
                var seconds = StudyTimeSeconds % 60;
                return $"{hours:D2}:{minutes:D2}:{seconds:D2}";
            }
        }

        public TopicItem()
        {
            Progress = 0.0;
            StudyTimeSeconds = 0; // ✅ 초단위로 초기화
            IsCompleted = false;
        }

        private void SaveCheckStateToDatabase()
        {
            try
            {
                if (!string.IsNullOrEmpty(ParentSubjectName) && !string.IsNullOrEmpty(ParentTopicGroupName) && !string.IsNullOrEmpty(Name))
                {
                    var dbHelper = Notea.Modules.Common.Helpers.DatabaseHelper.Instance;
                    dbHelper.UpdateDailyTopicItemCompletion(DateTime.Today, ParentSubjectName, ParentTopicGroupName, Name, IsCompleted);
                    System.Diagnostics.Debug.WriteLine($"[TopicItem] 체크 상태 저장: {Name} = {IsCompleted}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TopicItem] 체크 상태 저장 오류: {ex.Message}");
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
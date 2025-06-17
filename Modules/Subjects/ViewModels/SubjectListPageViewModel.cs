using Notea.Modules.Common.Helpers;
using Notea.ViewModels;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace Notea.Modules.Subjects.ViewModels
{
    public class SubjectListPageViewModel : ViewModelBase
    {
        private readonly DatabaseHelper _db = DatabaseHelper.Instance;
        public ObservableCollection<SubjectGroupViewModel> Subjects { get; set; } = new();

        private bool _isAdding;
        public bool IsAdding
        {
            get => _isAdding;
            set
            {
                _isAdding = value;
                OnPropertyChanged(nameof(IsAdding));
            }
        }

        public ICommand StartAddCommand { get; }
        public ICommand AddSubjectCommand { get; }

        private string _newSubjectText;
        public string NewSubjectText
        {
            get => _newSubjectText;
            set => SetProperty(ref _newSubjectText, value);
        }

        public SubjectListPageViewModel()
        {
            Subjects = new ObservableCollection<SubjectGroupViewModel>();
            LoadSubjects();

            StartAddCommand = new RelayCommand(() => IsAdding = true);

            AddSubjectCommand = new RelayCommand(() =>
            {
                if (!string.IsNullOrWhiteSpace(NewSubjectText))
                {
                    int subjectId = _db.AddSubject(NewSubjectText);

                    Subjects.Add(new SubjectGroupViewModel
                    {
                        SubjectId = subjectId,
                        SubjectName = NewSubjectText,
                        TopicGroups = new ObservableCollection<TopicGroupViewModel>(),
                        TotalStudyTimeSeconds = 0 // ✅ 초단위로 설정
                    });

                    NewSubjectText = string.Empty;
                    IsAdding = false;

                    UpdateGlobalTotalTime();
                }
            });
        }

        private void LoadSubjects()
        {
            Subjects.Clear();

            // ✅ 전체 학습시간 계산 및 설정 (초단위)
            int totalAllSubjectsTimeSeconds = _db.GetTotalAllSubjectsStudyTimeSeconds();
            SubjectGroupViewModel.SetGlobalTotalTime(totalAllSubjectsTimeSeconds);

            var subjectList = _db.LoadSubjectsWithGroups();
            foreach (var subject in subjectList)
            {
                Subjects.Add(subject);
            }
        }

        private void UpdateGlobalTotalTime()
        {
            // ✅ 초단위로 계산
            int totalAllSubjectsTimeSeconds = _db.GetTotalAllSubjectsStudyTimeSeconds();
            SubjectGroupViewModel.SetGlobalTotalTime(totalAllSubjectsTimeSeconds);

            foreach (var subject in Subjects)
            {
                subject.NotifyProgressChanged();
            }
        }

        public void RefreshData()
        {
            LoadSubjects();
        }
    }
}
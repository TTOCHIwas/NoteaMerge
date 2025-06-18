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
                    try
                    {
                        int subjectId = _db.AddSubject(NewSubjectText.Trim());

                        Subjects.Add(new SubjectGroupViewModel
                        {
                            SubjectId = subjectId,
                            SubjectName = NewSubjectText.Trim(),
                            TopicGroups = new ObservableCollection<TopicGroupViewModel>(),
                            TotalStudyTimeSeconds = 0
                        });

                        NewSubjectText = string.Empty;
                        IsAdding = false;
                        UpdateGlobalTotalTime();

                        System.Diagnostics.Debug.WriteLine($"[UI] 과목 추가 성공: {NewSubjectText}");
                    }
                    catch (InvalidOperationException ex)
                    {
                        // 중복 과목명 경고창 표시
                        System.Windows.MessageBox.Show(
                            ex.Message,
                            "중복 과목명",
                            System.Windows.MessageBoxButton.OK,
                            System.Windows.MessageBoxImage.Warning
                        );

                        System.Diagnostics.Debug.WriteLine($"[UI] 중복 과목명 경고: {ex.Message}");
                        // IsAdding을 false로 설정하지 않아서 사용자가 다시 입력할 수 있도록 함
                    }
                    catch (Exception ex)
                    {
                        // 기타 오류
                        System.Windows.MessageBox.Show(
                            $"과목 추가 중 오류가 발생했습니다:\n{ex.Message}",
                            "오류",
                            System.Windows.MessageBoxButton.OK,
                            System.Windows.MessageBoxImage.Error
                        );

                        System.Diagnostics.Debug.WriteLine($"[UI] 과목 추가 오류: {ex.Message}");
                        IsAdding = false;
                    }
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
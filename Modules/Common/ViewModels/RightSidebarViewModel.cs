using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using System.Windows.Threading;
using Notea.ViewModels;
using Notea.Modules.Common.Models;
using Notea.Modules.Common.Helpers;
using System.Windows;

namespace Notea.Modules.Common.ViewModels
{
    public class RightSidebarViewModel : ViewModelBase, IDisposable
    {
        // 싱글톤 DB 헬퍼 사용
        private readonly DatabaseHelper _db = DatabaseHelper.Instance;
        private DispatcherTimer _timer;
        private TimeSpan _currentSessionTime; // 현재 세션 시간
        private TimeSpan _todayTotalTime;     // 오늘의 총 학습 시간 (DB에서 로드)
        private bool _isRunning;
        private DateTime _sessionStartTime;   // 세션 시작 시간

        // ✅ 타이머 상태 이벤트 (추후 과목페이지/분류그룹에서 구독할 예정)
        public event Action<bool> TimerStatusChanged;

        // ✅ 현재 활성 과목/분류 정보 (추후 과목페이지에서 설정할 예정)
        private string _currentActiveSubject = string.Empty;
        private string _currentActiveTopicGroup = string.Empty;

        // ✅ 활성 과목/분류 설정 메소드 (추후 과목페이지에서 호출)
        public void SetActiveSubject(string subjectName, string topicGroup = "")
        {
            _currentActiveSubject = subjectName;
            _currentActiveTopicGroup = topicGroup;
            System.Diagnostics.Debug.WriteLine($"[Timer] 활성 설정: 과목={subjectName}, 분류={topicGroup}");
        }

        // ✅ 활성 과목/분류 해제 (페이지 나갈 때)
        public void ClearActiveSubject()
        {
            _currentActiveSubject = string.Empty;
            _currentActiveTopicGroup = string.Empty;
            System.Diagnostics.Debug.WriteLine($"[Timer] 활성 해제");
        }

        // 총 학습 시간을 00:00:00 형식으로 표시 (실시간 업데이트 포함)
        public string TotalStudyTimeDisplay
        {
            get
            {
                // 오늘의 저장된 총 시간 + 현재 실행중인 세션 시간
                var displayTime = _todayTotalTime.Add(_currentSessionTime);
                return displayTime.ToString(@"hh\:mm\:ss");
            }
        }

        public string TimerButtonText => _isRunning ? "일시정지" : "시작";
        public bool IsTimerRunning => _isRunning;

        public ObservableCollection<Note> Memos { get; } = new();

        public ICommand ToggleTimerCommand { get; }
        public ICommand AddMemoCommand { get; }
        public ICommand ToggleMemoCommand { get; }
        public ICommand CloseMemoCommand { get; }
        public ICommand DeleteMemoCommand { get; }

        private string _newMemoText = string.Empty;
        public string NewMemoText
        {
            get => _newMemoText;
            set => SetProperty(ref _newMemoText, value);
        }

        public RightSidebarViewModel()
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += OnTimerTick;

            // Commands 초기화
            ToggleTimerCommand = new RelayCommand(ToggleTimer);
            DeleteMemoCommand = new RelayCommand<Note>(DeleteMemo);
            AddMemoCommand = new RelayCommand(AddMemo);
            ToggleMemoCommand = new RelayCommand<Note>(ToggleMemo);
            CloseMemoCommand = new RelayCommand<Note>(note =>
            {
                if (note != null)
                    note.IsSelected = false;
            });

            LoadTodayTotalTime();
            LoadMemos();

            // 앱 종료 시 세션 저장을 위한 이벤트 등록 (여러 방법으로 보장)
            if (System.Windows.Application.Current != null)
            {
                System.Windows.Application.Current.Exit += Application_Exit;
                System.Windows.Application.Current.SessionEnding += Application_SessionEnding;
            }
        }

        private void Application_Exit(object sender, ExitEventArgs e)
        {
            EndSession();
        }

        private void Application_SessionEnding(object sender, SessionEndingCancelEventArgs e)
        {
            EndSession();
        }

        // ✅ 매초 실행되는 타이머 이벤트 - 과목/분류 시간도 함께 증가
        private void OnTimerTick(object sender, EventArgs e)
        {
            _currentSessionTime = _currentSessionTime.Add(TimeSpan.FromSeconds(1));
            OnPropertyChanged(nameof(TotalStudyTimeDisplay));

            // ✅ 활성 과목/분류 시간 증가 (추후 MainViewModel을 통해 호출)
            if (_isRunning && !string.IsNullOrEmpty(_currentActiveSubject))
            {
                UpdateActiveSubjectTime();
            }
        }

        // ✅ 활성 과목/분류 시간 업데이트 (추후 MainViewModel과 연결)
        private void UpdateActiveSubjectTime()
        {
            try
            {
                // 추후 MainViewModel 인스턴스를 통해 호출할 예정
                // 방법 1: 이벤트 방식
                // SubjectTimeIncremented?.Invoke(_currentActiveSubject, _currentActiveTopicGroup);

                // 방법 2: 직접 참조 방식 (추후 설정)
                // if (_mainViewModelRef != null)
                // {
                //     _mainViewModelRef.OnSubjectPageActivity(_currentActiveSubject);
                //     
                //     if (!string.IsNullOrEmpty(_currentActiveTopicGroup))
                //     {
                //         _mainViewModelRef.OnTopicGroupActivity(_currentActiveSubject, _currentActiveTopicGroup);
                //     }
                // }

                System.Diagnostics.Debug.WriteLine($"[Timer] 시간 증가: 과목={_currentActiveSubject}, 분류={_currentActiveTopicGroup}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Timer] 시간 업데이트 오류: {ex.Message}");
            }
        }

        // ✅ 과목/분류 시간 증가 이벤트 (추후 MainViewModel에서 구독)
        public event Action<string, string> SubjectTimeIncremented;

        private void ToggleTimer()
        {
            if (_isRunning)
            {
                // 타이머 일시정지
                _timer.Stop();

                // 현재 세션이 있으면 DB에 저장 (1초 이상이면 저장)
                if (_currentSessionTime.TotalSeconds >= 1)
                {
                    SaveCurrentSession();

                    // 저장된 시간을 오늘 총 시간에 추가
                    _todayTotalTime = _todayTotalTime.Add(_currentSessionTime);

                    // 현재 세션 초기화
                    _currentSessionTime = TimeSpan.Zero;

                    System.Diagnostics.Debug.WriteLine($"[Timer] 세션 저장 완료. 오늘 총 시간: {_todayTotalTime.ToString(@"hh\:mm\:ss")}");
                }

                OnPropertyChanged(nameof(TotalStudyTimeDisplay));
            }
            else
            {
                // 타이머 시작/재시작
                _sessionStartTime = DateTime.Now;
                _timer.Start();
                System.Diagnostics.Debug.WriteLine($"[Timer] 타이머 시작");
            }

            _isRunning = !_isRunning;
            OnPropertyChanged(nameof(TimerButtonText));
            OnPropertyChanged(nameof(IsTimerRunning));

            // ✅ 타이머 상태 변경 이벤트 발생
            TimerStatusChanged?.Invoke(_isRunning);
        }

        // 현재 세션을 DB에 저장
        private void SaveCurrentSession()
        {
            try
            {
                var endTime = DateTime.Now;
                _db.SaveStudySession(_sessionStartTime, endTime, (int)_currentSessionTime.TotalSeconds);

                System.Diagnostics.Debug.WriteLine($"[Timer] 세션 저장됨: {_currentSessionTime.ToString(@"hh\:mm\:ss")}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Timer] 세션 저장 오류: {ex.Message}");
            }
        }

        // 세션을 완전히 종료하고 저장하는 메소드
        public void EndSession()
        {
            if (_isRunning && _currentSessionTime.TotalSeconds >= 1)
            {
                SaveCurrentSession();
                _todayTotalTime = _todayTotalTime.Add(_currentSessionTime);
                System.Diagnostics.Debug.WriteLine($"[Timer] 앱 종료 시 세션 저장: {_currentSessionTime.ToString(@"hh\:mm\:ss")}");
            }

            // 세션 초기화
            _currentSessionTime = TimeSpan.Zero;
            _timer?.Stop();
            _isRunning = false;

            OnPropertyChanged(nameof(TotalStudyTimeDisplay));
            OnPropertyChanged(nameof(TimerButtonText));
            OnPropertyChanged(nameof(IsTimerRunning));
        }

        // 오늘의 총 학습 시간을 DB에서 로드
        private void LoadTodayTotalTime()
        {
            try
            {
                var totalSeconds = _db.GetTotalStudyTimeSeconds(DateTime.Today);
                _todayTotalTime = TimeSpan.FromSeconds(totalSeconds);
                OnPropertyChanged(nameof(TotalStudyTimeDisplay));

                System.Diagnostics.Debug.WriteLine($"[Timer] 오늘 총 학습 시간 로드됨: {_todayTotalTime.ToString(@"hh\:mm\:ss")}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Timer] 총 학습 시간 로드 오류: {ex.Message}");
                _todayTotalTime = TimeSpan.Zero;
            }
        }

        // ✅ 오늘 총 시간 강제 새로고침 (날짜 변경시 호출)
        public void RefreshTodayTotalTime()
        {
            LoadTodayTotalTime();
        }

        private void AddMemo()
        {
            if (!string.IsNullOrWhiteSpace(NewMemoText))
            {
                var newNote = new Note
                {
                    Content = NewMemoText.Trim()
                };

                _db.SaveNote(newNote);
                NewMemoText = "";
                LoadMemos();
            }
        }

        private void LoadMemos()
        {
            Memos.Clear();
            foreach (var note in _db.GetAllNotes())
                Memos.Add(note);
        }

        private void ToggleMemo(Note note)
        {
            if (note != null)
                note.IsSelected = !note.IsSelected;
        }

        private void DeleteMemo(Note note)
        {
            if (note == null)
            {
                System.Diagnostics.Debug.WriteLine("[삭제 시도] Note가 null입니다.");
                return;
            }
            _db.DeleteNote(note.NoteId);
            LoadMemos();
        }

        // IDisposable 구현 (메모리 누수 방지)
        public void Dispose()
        {
            // 종료 전에 세션 저장
            EndSession();

            _timer?.Stop();
            _timer = null;

            // 앱 종료 시 이벤트 해제
            if (System.Windows.Application.Current != null)
            {
                System.Windows.Application.Current.Exit -= Application_Exit;
                System.Windows.Application.Current.SessionEnding -= Application_SessionEnding;
            }
        }
    }
}
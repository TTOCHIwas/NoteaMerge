using System;
using System.ComponentModel;

namespace Notea.Modules.Daily.ViewModels
{
    public class DailyHeaderViewModel : INotifyPropertyChanged
    {
        private string _title;
        public string Title
        {
            get => _title;
            set
            {
                if (_title != value)
                {
                    _title = value;
                    OnPropertyChanged(nameof(Title));
                }
            }
        }

        private string _currentDate;
        public string CurrentDate
        {
            get => _currentDate;
            set
            {
                if (_currentDate != value)
                {
                    _currentDate = value;
                    OnPropertyChanged(nameof(CurrentDate));
                }
            }
        }
        public void SetSelectedDate(DateTime date)
        {
            CurrentDate = date.ToString("yyyy.MM.dd");
            System.Diagnostics.Debug.WriteLine($"[DailyHeaderViewModel] 날짜 설정: {date.ToShortDateString()}");
        }

        public DailyHeaderViewModel()
        {
            Title = "오늘 할 일";
            CurrentDate = DateTime.Now.ToString("yyyy.MM.dd");
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

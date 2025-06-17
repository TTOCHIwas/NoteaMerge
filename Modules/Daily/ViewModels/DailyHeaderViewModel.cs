using System;
using System.ComponentModel;

namespace Notea.Modules.Daily.ViewModels
{
    public class DailyHeaderViewModel : INotifyPropertyChanged
    {
        public string Title => "오늘 할 일";

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

        public DailyHeaderViewModel()
        {
            CurrentDate = DateTime.Now.ToString("yyyy.MM.dd");
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

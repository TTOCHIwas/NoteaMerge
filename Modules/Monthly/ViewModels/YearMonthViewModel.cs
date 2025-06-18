using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Notea.Modules.Monthly.ViewModels
{
    public class YearMonthViewModel : INotifyPropertyChanged
    {
        private int _month;
        private string _comment;
        private int _year;

        public int Month
        {
            get => _month;
            set
            {
                _month = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(MonthText));
            }
        }
        public int Year
        {
            get => _year;
            set
            {
                _year = value;
                OnPropertyChanged();
            }
        }

        public string MonthText => $"{Month:00}월";

        public string Comment
        {
            get => _comment;
            set
            {
                _comment = value;
                OnPropertyChanged();
            }
        }
        public YearMonthViewModel()
        {
            Year = DateTime.Now.Year; // 기본값 설정
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}

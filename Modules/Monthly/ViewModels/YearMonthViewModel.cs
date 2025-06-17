using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Notea.Modules.Monthly.ViewModels
{
    public class YearMonthViewModel : INotifyPropertyChanged
    {
        private int _month;
        private string _comment;

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

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}

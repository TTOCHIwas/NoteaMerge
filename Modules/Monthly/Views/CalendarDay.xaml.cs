using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Notea.ViewModels;

namespace Notea.Modules.Monthly.Views
{
    /// <summary>
    /// CalendarDay.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class CalendarDay : UserControl, INotifyPropertyChanged
    {
        public DateTime Date { get; set; }
        static DateTime Today = DateTime.Now;
        public string Title { get; set; }

        private string _dayComment;
        public string DayComment
        {
            get => _dayComment;
            set
            {
                if (_dayComment != value)
                {
                    _dayComment = value;
                    OnPropertyChanged(nameof(DayComment));
                }
            }
        }

        public event Action<DateTime>? AddEventRequested;
         public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }


        public CalendarDay()
        {
            InitializeComponent();
            DataContext = this;
        }

        private void AddEvent_Click(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2)
            {
                AddEventRequested?.Invoke(Date);
            }
            else if (e.ChangedButton == MouseButton.Left && e.ClickCount == 1)
            {
            }
        }
        private void DayCommentBlock_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 마우스를 더블클릭 했을 때만 동작하도록 합니다.
            if (e.ClickCount == 2)
            {
                // MainWindow의 DataContext에서 MainViewModel 인스턴스를 가져옵니다.
                if (Application.Current.MainWindow?.DataContext is MainViewModel mainVM)
                {
                    // NavigateToDailyViewForDateCommand 커맨드를 실행합니다.
                    // 이 때, 파라미터로 현재 날짜 칸(CalendarDay)의 Date 프로퍼티를 넘겨줍니다.
                    if (mainVM.NavigateToDailyViewForDateCommand.CanExecute(this.Date))
                    {
                        mainVM.NavigateToDailyViewForDateCommand.Execute(this.Date);
                    }
                }
            }
        }
    }
}

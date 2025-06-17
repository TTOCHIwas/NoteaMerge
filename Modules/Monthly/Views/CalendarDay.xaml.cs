using System;
using System.Collections.Generic;
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

namespace Notea.Modules.Monthly.Views
{
    /// <summary>
    /// CalendarDay.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class CalendarDay : UserControl
    {
        public DateTime Date { get; set; }
        static DateTime Today = DateTime.Now;
        public string Title { get; set; }
        public event Action<DateTime>? AddEventRequested;
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
    }
}

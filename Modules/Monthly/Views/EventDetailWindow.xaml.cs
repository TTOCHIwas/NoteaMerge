using Notea.Modules.Monthly.Models;
using System;
using System.Collections.Generic;
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
using System.Windows.Shapes;

namespace Notea.Modules.Monthly.Views
{
    /// <summary>
    /// EventDetailWindow.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class EventDetailWindow : Window
    {
        public ICalendarPlan Event { get; private set; }
        public bool IsDeleted { get; private set; } = false;

        public EventDetailWindow(ICalendarPlan calendarEvent)
        {
            InitializeComponent();
            Event = calendarEvent;
            this.DataContext = Event;
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true; // 창 닫힘 + 호출부에서 확인 가능
            this.Close();
        }
        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("정말 삭제하시겠습니까?", "삭제 확인", MessageBoxButton.YesNo);
            if (result == MessageBoxResult.Yes)
            {
                this.IsDeleted = true;
                this.DialogResult = true;
                this.Close();
            }
        }
    }
}
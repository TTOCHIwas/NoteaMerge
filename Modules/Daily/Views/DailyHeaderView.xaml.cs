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
using System.Windows.Navigation;
using System.Windows.Shapes;
using Notea.Modules.Daily.ViewModels;
using Notea.ViewModels;

namespace Notea.Modules.Daily.Views
{
    /// <summary>
    /// DailyHeaderView.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class DailyHeaderView : UserControl
    {
        public DailyHeaderView()
        {
            InitializeComponent();
        }
        private void NavigateToCalendar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // MainWindow에서 MainViewModel 인스턴스를 가져옵니다.
            var mainVM = Application.Current.MainWindow?.DataContext as MainViewModel;
            // 이 컨트롤의 DataContext에서 DailyHeaderViewModel 인스턴스를 가져옵니다.
            var headerVM = this.DataContext as DailyHeaderViewModel;

            if (mainVM != null && headerVM != null)
            {
                // 커맨드의 파라미터로 현재 날짜(string)를 전달하며 실행합니다.
                if (mainVM.NavigateToCalendarCommand.CanExecute(headerVM.CurrentDate))
                {
                    mainVM.NavigateToCalendarCommand.Execute(headerVM.CurrentDate);
                }
            }
        }
    }
}

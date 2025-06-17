using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Notea.Modules.Common.ViewModels;
using Notea.Modules.Daily.Views;
using Notea.Modules.Subjects.Views;
using Notea.ViewModels;

namespace Notea.Modules.Common.Views
{
    /// <summary>
    /// LeftSidebar.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class LeftSidebar : UserControl
    {
        public LeftSidebar()
        {
            InitializeComponent();
            // DataContext는 MainViewModel에서 설정됨
        }

        private void TextBlock_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (Window.GetWindow(this)?.DataContext is MainViewModel vm)
            {
                // 현재 화면이 Daily인지 Subject인지 확인하고 토글
                if (vm.HeaderContent is DailyHeaderView)
                {
                    // Daily -> Subject로 전환
                    vm.NavigateToSubjectListCommand.Execute(null);
                }
                else
                {
                    // Subject -> Daily로 전환
                    vm.NavigateToTodayCommand.Execute(null);
                }
            }
        }
    }
}
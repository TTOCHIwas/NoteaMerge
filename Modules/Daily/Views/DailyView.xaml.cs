using System.Windows.Controls;

namespace Notea.Modules.Daily.Views
{
    /// <summary>
    /// DailyView.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class DailyView : UserControl
    {
        public DailyView()
        {
            InitializeComponent();
            // DataContext 설정 제거 - MainViewModel에서 관리
        }
    }
}
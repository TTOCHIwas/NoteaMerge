using System.Windows.Controls;

namespace Notea.Modules.Daily.Views
{
    /// <summary>
    /// TodoListView.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class TodoListView : UserControl
    {
        public TodoListView()
        {
            InitializeComponent();
            // DataContext 설정 제거 - 부모로부터 상속받음
        }
    }
}
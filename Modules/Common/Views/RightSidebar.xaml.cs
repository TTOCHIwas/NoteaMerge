using System.Windows;
using System.Windows.Controls;
using Notea.Modules.Common.Helpers;
using Notea.Modules.Common.Models;

namespace Notea.Modules.Common.Views
{
    /// <summary>
    /// RightSidebar.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class RightSidebar : UserControl
    {
        // 싱글톤 DB 헬퍼 사용
        private readonly DatabaseHelper _db = DatabaseHelper.Instance;

        public RightSidebar()
        {
            InitializeComponent();
        }

        private void MemoTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb && tb.DataContext is Note note)
            {
                _db.UpdateNote(note); // DB에 수정 반영
            }
        }
    }
}
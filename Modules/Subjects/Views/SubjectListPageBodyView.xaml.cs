using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Notea.Modules.Subjects.ViewModels;

namespace Notea.Modules.Subjects.Views
{
    /// <summary>
    /// SubjectListPageBodyView.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class SubjectListPageBodyView : UserControl
    {
        public SubjectListPageBodyView()
        {
            InitializeComponent();
            this.DataContext = new SubjectListPageViewModel(); // ViewModel 명시적 연결
        }

        private void SubjectAddBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (SubjectAddBox.IsVisible)
            {
                SubjectAddBox.Focus();
            }
            else
            {
                // 포커스 다른 곳으로 넘겨 점선 테두리 제거
                FocusManager.SetFocusedElement(FocusManager.GetFocusScope(SubjectAddBox), null);
                Keyboard.ClearFocus();
            }
        }

        private void SubjectAddBox_KeyDown(object sender, KeyEventArgs e)
        {
            // 눌린 키가 ESC 키인지 확인합니다.
            if (e.Key == Key.Escape)
            {
                // DataContext를 ViewModel로 가져옵니다.
                if (this.DataContext is SubjectListPageViewModel vm)
                {
                    // IsAdding 상태를 false로 변경하여 입력창을 숨깁니다.
                    vm.IsAdding = false;
                }
                // 다른 컨트롤로 이벤트가 전파되지 않도록 처리 완료로 표시합니다.
                e.Handled = true;
            }
        }


    }

}

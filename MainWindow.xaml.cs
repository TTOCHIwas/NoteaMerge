using System.Windows;
using System.Windows.Media;
using Notea.ViewModels;
using Notea.Modules.Common.ViewModels;
using Notea.Modules.Common.Views;
using System.Windows.Controls;
using System.Windows.Input;

namespace Notea;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        this.DataContext = new MainViewModel(); // 반드시 있어야 함


        // 앱 종료 시 타이머 저장 보장
        this.Closing += MainWindow_Closing;

        // MainWindow 전체의 KeyDown 이벤트에 핸들러
        this.KeyDown += MainWindow_KeyDown;
    }

    // 스페이스바 입력을 감지하는 이벤트 핸들러 메서드
    private void MainWindow_KeyDown(object sender, KeyEventArgs e)
    {
        // 눌린 키가 스페이스바인지 확인합니다.
        if (e.Key == Key.Space)
        {
            // 현재 포커스가 TextBox 같은 텍스트 입력 컨트롤에 있는지 확인합니다.
            // 텍스트 입력 중일 때는 단축키가 동작하면 안 되기 때문입니다.
            if (Keyboard.FocusedElement is TextBox || Keyboard.FocusedElement is RichTextBox)
            {
                return; // 텍스트 입력 중이므로 단축키 기능을 실행하지 않고 종료
            }

            // RightSidebar의 ViewModel을 찾아 타이머 토글 명령을 실행합니다.
            var layoutShell = this.Content as LayoutShell;
            if (layoutShell != null)
            {
                var rightSidebar = FindChild<RightSidebar>(layoutShell);
                if (rightSidebar?.DataContext is RightSidebarViewModel timerVM)
                {
                    if (timerVM.ToggleTimerCommand.CanExecute(null))
                    {
                        timerVM.ToggleTimerCommand.Execute(null);

                        // 스페이스바가 다른 동작(예: 버튼 클릭)을 유발하지 않도록 이벤트를 처리 완료로 표시합니다.
                        e.Handled = true;
                    }
                }
            }
        }
    }

    private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        try
        {
            // RightSidebar의 타이머 세션 저장
            var layoutShell = this.Content as LayoutShell;
            if (layoutShell != null)
            {
                // LayoutShell에서 RightSidebar 찾기
                var rightSidebar = FindChild<RightSidebar>(layoutShell);
                if (rightSidebar?.DataContext is RightSidebarViewModel timerVM)
                {
                    timerVM.EndSession();
                    System.Diagnostics.Debug.WriteLine("[MainWindow] 앱 종료 시 타이머 세션 저장 완료");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow] 앱 종료 시 오류: {ex.Message}");
        }
    }

    // 자식 컨트롤 찾기 헬퍼 메소드
    private static T FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent == null) return null;

        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T result)
                return result;

            var childOfChild = FindChild<T>(child);
            if (childOfChild != null)
                return childOfChild;
        }
        return null;
    }
}
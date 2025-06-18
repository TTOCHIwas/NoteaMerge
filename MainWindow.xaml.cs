using System.Text;
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
using Notea.Modules.Common.ViewModels;
using Notea.Modules.Common.Views;
using Notea.Modules.Common.ViewModels;
using Notea.Modules.Common.Views;
using Notea.ViewModels;

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
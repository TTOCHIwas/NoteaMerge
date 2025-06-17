using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Notea.Modules.Daily.Models;
using Notea.Modules.Daily.ViewModels;

namespace Notea.Modules.Daily.Views
{
    /// <summary>
    /// DailyBodyView.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class DailyBodyView : UserControl
    {
        public DailyBodyView()
        {
            InitializeComponent();
 

            this.Loaded += (s, e) =>
            {
                System.Diagnostics.Debug.WriteLine(" Loaded fired");
                Console.WriteLine(" Loaded fired");
                if (this.DataContext is DailyBodyViewModel vm)
                {
                    vm.RequestFocusOnInput = () =>
                    {
                        TodoAddBox.Focus();
                        TodoAddBox.SelectAll();
                    };
                    System.Diagnostics.Debug.WriteLine($"[DailyBodyView Loaded] DataContext: {this.DataContext?.GetType().Name ?? "null"}"); // ★★★ Debug.WriteLine으로 변경 ★★★
                }
            };
#if DEBUG
            if (DesignerProperties.GetIsInDesignMode(this))
            {
                Debug.WriteLine("[디자인 모드] DailyBodyView 생성됨 (디자이너)");
                return;
            }
#endif

        }

        //TextBox가 보일 때 자동 포커스
        private void TodoAddBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (TodoAddBox.IsVisible)
            {
                TodoAddBox.Focus();
            }
            else
            {
                // 포커스 다른 곳으로 넘겨 점선 테두리 제거
                FocusManager.SetFocusedElement(FocusManager.GetFocusScope(TodoAddBox), null);
                Keyboard.ClearFocus();
            }
        }

        private void CommentTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
            {
                // Window로 포커스 이동 (점선 없음)
                Keyboard.ClearFocus();
                e.Handled = true;
            }
        }
        // Todo 삭제 Click 이벤트 핸들러 추가
        private void DeleteTodo_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("[Todo] DeleteTodo_Click 이벤트 발생");

            if (sender is MenuItem menuItem && menuItem.Tag is TodoItem todo)
            {
                System.Diagnostics.Debug.WriteLine($"[Todo] 삭제 대상: {todo.Title} (ID: {todo.Id})");

                if (DataContext is DailyBodyViewModel vm)
                {
                    vm.DeleteTodoItem(todo);
                    System.Diagnostics.Debug.WriteLine("[Todo] ViewModel의 DeleteTodoItem 호출 완료");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[Todo] DataContext가 DailyBodyViewModel이 아닙니다.");
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[Todo] MenuItem이나 Tag(TodoItem)를 찾을 수 없습니다.");
            }
        }
    }
}

using System;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.IO;
using Notea.Modules.Subject.ViewModels;
using System.Windows.Shapes;
using Path = System.IO.Path;
using Notea.Modules.Common.ViewModels;

namespace Notea.Modules.Subject.Views
{
    public partial class NoteEditorView : UserControl
    {
        public NoteEditorView()
        {
            InitializeComponent();
        }

        private bool _isInternalFocusChange = false;

        private void TextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox && textBox.DataContext is MarkdownLineViewModel vm)
            {
                vm.IsEditing = true;
                vm.HasFocus = true; // 포커스 상태 설정
                textBox.CaretIndex = textBox.Text.Length;

                // ✅ 카테고리 포커스 추적 및 타이머 연동
                try
                {
                    if (vm.CategoryId > 0)
                    {
                        var editorVM = this.DataContext as NoteEditorViewModel;
                        if (editorVM != null)
                        {
                            string subjectName = GetSubjectNameFromId(editorVM.SubjectId);

                            if (!string.IsNullOrEmpty(subjectName))
                            {
                                // 1. 데이터베이스에 포커스 시작 기록
                                var dbHelper = Notea.Modules.Common.Helpers.DatabaseHelper.Instance;
                                dbHelper.StartCategoryFocus(vm.CategoryId, subjectName);

                                // 2. 타이머에 현재 활성 카테고리 알림
                                var timerVM = GetTimerViewModel();
                                if (timerVM != null)
                                {
                                    timerVM.SetActiveCategory(vm.CategoryId, subjectName);
                                }

                                System.Diagnostics.Debug.WriteLine($"[포커스] 카테고리 포커스 및 타이머 연동 완료: CategoryId={vm.CategoryId}, Subject={subjectName}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[포커스 오류] 카테고리 포커스 시작 실패: {ex.Message}");
                }
            }
        }

        private void TextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isInternalFocusChange)
            {
                return;
            }

            if (sender is TextBox textBox && textBox.DataContext is MarkdownLineViewModel vm)
            {
                vm.HasFocus = false; // 포커스 상태 해제
                vm.IsComposing = false; // IME 조합 상태 리셋
                vm.UpdateInlinesFromContent();

                try
                {
                    if (vm.CategoryId > 0)
                    {
                        var timerVM = GetTimerViewModel();
                        if (timerVM != null)
                        {
                            timerVM.ClearActiveCategory();
                        }

                        System.Diagnostics.Debug.WriteLine($"[포커스] 카테고리 포커스 해제 및 타이머 연동: CategoryId={vm.CategoryId}");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[포커스 오류] 카테고리 포커스 해제 실패: {ex.Message}");
                }

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    vm.IsEditing = false;
                }), DispatcherPriority.DataBind);
            }
        }

        private string GetSubjectNameFromId(int subjectId)
        {
            try
            {
                // Helpers/DatabaseHelper의 ExecuteSelect 사용하여 과목명 조회
                string query = $"SELECT Name FROM subject WHERE subjectId = {subjectId}";
                var result = Notea.Helpers.DatabaseHelper.ExecuteSelect(query);

                if (result.Rows.Count > 0)
                {
                    return result.Rows[0]["title"].ToString();
                }

                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DB 오류] 과목명 조회 실패: {ex.Message}");
                return null;
            }
        }

        // 한글 입력 시 실시간으로 placeholder 숨기기
        private void TextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (sender is TextBox textBox && textBox.DataContext is MarkdownLineViewModel vm)
            {
                // 어떤 텍스트든 입력이 시작되면 즉시 조합 상태로 설정
                vm.IsComposing = true;
            }
        }

        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox textBox && textBox.DataContext is MarkdownLineViewModel vm)
            {
                // 텍스트가 실제로 있으면 조합 중이든 아니든 placeholder 숨김
                vm.IsComposing = !string.IsNullOrEmpty(textBox.Text);
                var noteEditorVm = FindParentDataContext<NoteEditorViewModel>(textBox);
                noteEditorVm.UpdateActivity();
            }
        }

        public static T? FindParentDataContext<T>(DependencyObject child) where T : class
        {
            DependencyObject parent = VisualTreeHelper.GetParent(child);

            while (parent != null)
            {
                if ((parent as FrameworkElement)?.DataContext is T vm)
                    return vm;

                parent = VisualTreeHelper.GetParent(parent);
            }

            return null;
        }


        private void TextBlock_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not MarkdownLineViewModel vm)
                return;

            vm.IsEditing = true;

            Dispatcher.InvokeAsync(() =>
            {
                int index = ((NoteEditorViewModel)this.DataContext).Lines.IndexOf(vm);
                editorView.UpdateLayout();

                var container = ItemsControlContainer.ItemContainerGenerator.ContainerFromIndex(index) as FrameworkElement;
                var textBox = FindVisualChild<TextBox>(container);
                if (textBox != null)
                {
                    textBox.Focus();
                    textBox.Select(textBox.Text.Length, 0);
                }
            }, DispatcherPriority.Input);
        }

        // 2. NoteEditorView.xaml.cs - 단축키 및 리스트 처리 확장
        private void TextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var textBox = sender as TextBox;
            if (textBox == null) return;

            var lineVM = textBox.DataContext as MarkdownLineViewModel;
            if (lineVM == null) return;

            var vm = this.DataContext as NoteEditorViewModel;
            if (vm == null) return;

            if (e.Key == Key.V && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                if (Clipboard.ContainsImage())
                {
                    e.Handled = true;
                    HandleImagePaste(vm, lineVM);
                    return;
                }
            }
            if (e.Key == Key.F && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                e.Handled = true;
                OpenSearch();
                return;
            }
            // 한글 입력 중 ESC 키로 조합 취소 시 처리
            if (e.Key == Key.Escape)
            {
                lineVM.IsComposing = false;
            }
            // Enter 키 처리 - 리스트 자동 계속
            else if (e.Key == Key.Enter)
            {
                e.Handled = true;
                HandleEnterWithList(vm, lineVM);
            }
            else if (e.Key == Key.Back)
            {
                if (lineVM.IsComposing && textBox.Text.Length <= 1)
                {
                    lineVM.IsComposing = false;
                }
                e.Handled = HandleBackspace(vm, textBox, lineVM);
            }
            // 마크다운 단축키 처리
            else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                switch (e.Key)
                {
                    case Key.B:
                        e.Handled = HandleBoldShortcut(textBox);
                        break;
                    case Key.I:
                        e.Handled = HandleItalicShortcut(textBox);
                        break;
                    case Key.U:
                        e.Handled = HandleUnderlineShortcut(textBox);
                        break;
                    case Key.X when Keyboard.Modifiers.HasFlag(ModifierKeys.Shift):
                        e.Handled = HandleStrikethroughShortcut(textBox);
                        break;
                    // 헤딩 단축키 추가
                    case Key.D1:
                        e.Handled = HandleHeadingShortcut(textBox, 1);
                        break;
                    case Key.D2:
                        e.Handled = HandleHeadingShortcut(textBox, 2);
                        break;
                    case Key.D3:
                        e.Handled = HandleHeadingShortcut(textBox, 3);
                        break;
                    case Key.D4:
                        e.Handled = HandleHeadingShortcut(textBox, 4);
                        break;
                    case Key.D5:
                        e.Handled = HandleHeadingShortcut(textBox, 5);
                        break;
                    case Key.D6:
                        e.Handled = HandleHeadingShortcut(textBox, 6);
                        break;
                    // 리스트 토글
                    case Key.L:
                        e.Handled = HandleListToggle(textBox);
                        break;
                }
            }
            // 방향키 네비게이션
            else if (e.Key == Key.Up || e.Key == Key.Down || e.Key == Key.Left || e.Key == Key.Right)
            {
                e.Handled = HandleArrowNavigation(vm, textBox, lineVM, e.Key);
            }
        }

        private void HandleImagePaste(NoteEditorViewModel vm, MarkdownLineViewModel currentLine)
        {
            try
            {
                if (!Clipboard.ContainsImage())
                    return;

                // 클립보드에서 이미지 가져오기
                var image = Clipboard.GetImage();
                if (image == null)
                    return;

                // 이미지 저장
                string imageFileName = $"img_{DateTime.Now:yyyyMMddHHmmss}_{Guid.NewGuid():N}.png";
                string imagePath = SaveImage(image, imageFileName);

                if (!string.IsNullOrEmpty(imagePath))
                {
                    // 현재 라인 다음에 이미지 라인 추가
                    int currentIndex = vm.Lines.IndexOf(currentLine);
                    vm.InsertImageLineAt(currentIndex + 1, imagePath);

                    Debug.WriteLine($"[IMAGE] 이미지 붙여넣기 완료: {imagePath}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERROR] 이미지 붙여넣기 실패: {ex.Message}");
                MessageBox.Show("이미지 붙여넣기에 실패했습니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string SaveImage(BitmapSource image, string fileName)
        {
            try
            {
                string imageFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "images");
                if (!Directory.Exists(imageFolder))
                {
                    Directory.CreateDirectory(imageFolder);
                }

                string fullPath = Path.Combine(imageFolder, fileName);

                // PNG 인코더 사용하여 저장
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));

                using (var fileStream = new FileStream(fullPath, FileMode.Create))
                {
                    encoder.Save(fileStream);
                }

                // 상대 경로 반환 (백슬래시를 슬래시로 변경)
                string relativePath = $"data/images/{fileName}".Replace('\\', '/');

                Debug.WriteLine($"[IMAGE] 이미지 저장 완료. 전체 경로: {fullPath}");
                Debug.WriteLine($"[IMAGE] 상대 경로: {relativePath}");

                // 파일이 실제로 저장되었는지 확인
                if (File.Exists(fullPath))
                {
                    Debug.WriteLine($"[IMAGE] 파일 확인됨. 크기: {new FileInfo(fullPath).Length} bytes");
                    return relativePath;
                }
                else
                {
                    Debug.WriteLine("[IMAGE ERROR] 파일이 저장되지 않음");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERROR] 이미지 저장 실패: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 헤딩 단축키 처리 (Ctrl+1~6)
        /// </summary>
        private bool HandleHeadingShortcut(TextBox textBox, int level)
        {
            string prefix = new string('#', level) + " ";

            // 현재 줄이 이미 헤딩인지 확인
            var headingMatch = Regex.Match(textBox.Text, @"^(#{1,6})\s+(.*)");
            if (headingMatch.Success)
            {
                // 기존 헤딩 레벨 변경
                string content = headingMatch.Groups[2].Value;
                textBox.Text = prefix + content;
                textBox.CaretIndex = textBox.Text.Length;
            }
            else
            {
                // 새로운 헤딩으로 변환
                textBox.Text = prefix + textBox.Text;
                textBox.CaretIndex = textBox.Text.Length;
            }

            return true;
        }

        /// <summary>
        /// 리스트 토글 (Ctrl+L)
        /// </summary>
        private bool HandleListToggle(TextBox textBox)
        {
            var lineVM = textBox.DataContext as MarkdownLineViewModel;
            if (lineVM == null) return false;

            // 이미 리스트인 경우 해제
            if (lineVM.IsList)
            {
                // 리스트 기호 제거
                var listPattern = @"^(\-|\*|\+|\d+\.)\s+";
                textBox.Text = Regex.Replace(textBox.Text, listPattern, "");
                textBox.CaretIndex = 0;
            }
            else
            {
                // 리스트로 변환
                textBox.Text = "- " + textBox.Text;
                textBox.CaretIndex = textBox.Text.Length;
            }

            return true;
        }

        /// <summary>
        /// Enter 키 처리 - 리스트 자동 계속
        /// </summary>
        private void HandleEnterWithList(NoteEditorViewModel vm, MarkdownLineViewModel currentLine)
        {
            _isInternalFocusChange = true;

            // 리스트나 제목 기호만 있으면 제거
            if (currentLine.ShouldCleanupOnEnter())
            {
                // 리스트 해제
                currentLine.IsList = false;
                currentLine.ListSymbol = "";
            }

            var currentIndex = vm.Lines.IndexOf(currentLine);

            // 현재 라인의 편집 모드 종료
            currentLine.IsEditing = false;

            // 새 라인 생성
            vm.InsertNewLineAt(currentIndex + 1);
            var newLine = vm.Lines[currentIndex + 1];

            // 리스트 자동 계속
            if (currentLine.IsList && !string.IsNullOrWhiteSpace(currentLine.Content)
                && !currentLine.ShouldCleanupOnEnter())
            {
                string nextPrefix = currentLine.GetNextListPrefix();
                if (!string.IsNullOrEmpty(nextPrefix))
                {
                    newLine.Content = nextPrefix;
                }
            }

            // 새 라인에 포커스
            Dispatcher.InvokeAsync(() =>
            {
                editorView.UpdateLayout();

                var newContainer = ItemsControlContainer.ItemContainerGenerator
                    .ContainerFromIndex(currentIndex + 1) as FrameworkElement;

                if (newContainer != null)
                {
                    var newTextBox = FindVisualChild<TextBox>(newContainer);
                    if (newTextBox != null)
                    {
                        newTextBox.Focus();
                        // 리스트 prefix가 있으면 커서를 끝으로
                        if (!string.IsNullOrEmpty(newLine.Content))
                        {
                            newTextBox.CaretIndex = newTextBox.Text.Length;
                        }
                    }
                }

                _isInternalFocusChange = false;
            }, DispatcherPriority.Input);
        }

        private bool HandleArrowNavigation(NoteEditorViewModel vm, TextBox textBox, MarkdownLineViewModel lineVM, Key key)
        {
            int currentIndex = vm.Lines.IndexOf(lineVM);
            int caretPos = textBox.CaretIndex;
            int textLength = textBox.Text.Length;

            switch (key)
            {
                case Key.Up:
                    if (currentIndex > 0)
                    {
                        return MoveToLine(vm, currentIndex - 1, caretPos);
                    }
                    break;

                case Key.Down:
                    if (currentIndex < vm.Lines.Count - 1)
                    {
                        return MoveToLine(vm, currentIndex + 1, caretPos);
                    }
                    break;

                case Key.Left:
                    if (caretPos == 0 && currentIndex > 0)
                    {
                        return MoveToLine(vm, currentIndex - 1, -1);
                    }
                    break;

                case Key.Right:
                    if (caretPos == textLength && currentIndex < vm.Lines.Count - 1)
                    {
                        return MoveToLine(vm, currentIndex + 1, 0);
                    }
                    break;
            }

            return false;
        }

        private bool MoveToLine(NoteEditorViewModel vm, int targetIndex, int caretPosition)
        {
            if (targetIndex < 0 || targetIndex >= vm.Lines.Count)
                return false;

            var currentLine = vm.Lines.FirstOrDefault(l => l.IsEditing);
            var targetLine = vm.Lines[targetIndex];

            if (currentLine == targetLine)
                return false;

            _isInternalFocusChange = true;

            targetLine.IsEditing = true;

            Dispatcher.InvokeAsync(() =>
            {
                editorView.UpdateLayout();

                var container = ItemsControlContainer.ItemContainerGenerator.ContainerFromIndex(targetIndex) as FrameworkElement;
                if (container != null)
                {
                    var targetTextBox = FindVisualChild<TextBox>(container);
                    if (targetTextBox != null)
                    {
                        targetTextBox.Focus();

                        if (caretPosition == -1)
                        {
                            targetTextBox.CaretIndex = targetTextBox.Text.Length;
                        }
                        else if (caretPosition >= 0)
                        {
                            targetTextBox.CaretIndex = Math.Min(caretPosition, targetTextBox.Text.Length);
                        }

                        if (currentLine != null && currentLine != targetLine)
                        {
                            Dispatcher.BeginInvoke(() =>
                            {
                                currentLine.IsEditing = false;
                                _isInternalFocusChange = false;
                            }, DispatcherPriority.Background);
                        }

                        container.BringIntoView();
                    }
                }
            }, DispatcherPriority.Render);

            return true;
        }

        private bool HandleMarkdownToggle(TextBox textBox, string markdownSymbol)
        {
            if (textBox.SelectionLength == 0)
            {
                int caretPos = textBox.CaretIndex;
                textBox.Text = textBox.Text.Insert(caretPos, markdownSymbol + markdownSymbol);
                textBox.CaretIndex = caretPos + markdownSymbol.Length;
                return true;
            }

            string fullText = textBox.Text;
            string selectedText = textBox.SelectedText;
            int selectionStart = textBox.SelectionStart;
            int selectionEnd = selectionStart + textBox.SelectionLength;
            int symbolLength = markdownSymbol.Length;

            bool hasExactMarkdownBefore = false;
            bool hasExactMarkdownAfter = false;

            if (selectionStart >= symbolLength && selectionEnd + symbolLength <= fullText.Length)
            {
                string beforeSymbol = fullText.Substring(selectionStart - symbolLength, symbolLength);
                string afterSymbol = fullText.Substring(selectionEnd, symbolLength);

                hasExactMarkdownBefore = beforeSymbol == markdownSymbol;
                hasExactMarkdownAfter = afterSymbol == markdownSymbol;

                if (markdownSymbol == "*" && hasExactMarkdownBefore && selectionStart >= 2)
                {
                    if (fullText[selectionStart - 2] == '*')
                        hasExactMarkdownBefore = false;
                }
                if (markdownSymbol == "*" && hasExactMarkdownAfter && selectionEnd + 1 < fullText.Length)
                {
                    if (fullText[selectionEnd + 1] == '*')
                        hasExactMarkdownAfter = false;
                }
            }

            if (hasExactMarkdownBefore && hasExactMarkdownAfter)
            {
                textBox.Text = fullText.Remove(selectionEnd, symbolLength)
                                      .Remove(selectionStart - symbolLength, symbolLength);

                textBox.SelectionStart = selectionStart - symbolLength;
                textBox.SelectionLength = selectedText.Length;
            }
            else
            {
                string formattedText = markdownSymbol + selectedText + markdownSymbol;
                textBox.Text = fullText.Remove(selectionStart, textBox.SelectionLength)
                                      .Insert(selectionStart, formattedText);

                textBox.SelectionStart = selectionStart;
                textBox.SelectionLength = formattedText.Length;
            }

            return true;
        }

        private bool HandleBoldShortcut(TextBox textBox) => HandleMarkdownToggle(textBox, "**");
        private bool HandleItalicShortcut(TextBox textBox) => HandleMarkdownToggle(textBox, "*");
        private bool HandleUnderlineShortcut(TextBox textBox) => HandleMarkdownToggle(textBox, "__");
        private bool HandleStrikethroughShortcut(TextBox textBox)
        {
            return HandleMarkdownToggle(textBox, "~~");
        }

        private void HandleEnter(NoteEditorViewModel vm)
        {
            _isInternalFocusChange = true;

            // 현재 편집 중인 라인 찾기
            var currentLine = vm.Lines.FirstOrDefault(l => l.IsEditing);
            if (currentLine == null)
            {
                vm.AddNewLine();
                return;
            }

            var currentIndex = vm.Lines.IndexOf(currentLine);

            // 현재 라인의 편집 모드 종료 (자동 저장됨)
            currentLine.IsEditing = false;

            // 중요: 현재 라인 바로 다음에 새 라인 삽입
            vm.InsertNewLineAt(currentIndex + 1);

            // 새로 삽입된 라인에 포커스
            Dispatcher.InvokeAsync(() =>
            {
                editorView.UpdateLayout();

                var newContainer = ItemsControlContainer.ItemContainerGenerator
                    .ContainerFromIndex(currentIndex + 1) as FrameworkElement;

                if (newContainer != null)
                {
                    var newTextBox = FindVisualChild<TextBox>(newContainer);
                    if (newTextBox != null)
                    {
                        newTextBox.Focus();
                    }
                }

                _isInternalFocusChange = false;
            }, DispatcherPriority.Input);
        }

        private bool HandleBackspace(NoteEditorViewModel vm, TextBox textBox, MarkdownLineViewModel lineVM)
        {
            if (textBox.CaretIndex > 0 || !string.IsNullOrWhiteSpace(textBox.Text) || vm.Lines.Count <= 1)
                return false;

            int index = vm.Lines.IndexOf(lineVM);

            // 라인 삭제 (자동으로 데이터베이스에서도 삭제됨)
            vm.RemoveLine(lineVM);

            Dispatcher.BeginInvoke(() =>
            {
                editorView.UpdateLayout();
                FocusTextBoxAtIndex(Math.Max(0, index - 1));
            }, DispatcherPriority.ApplicationIdle);

            return true;
        }

        private void FocusTextBoxAtIndex(int index)
        {
            var container = ItemsControlContainer.ItemContainerGenerator.ContainerFromIndex(index) as FrameworkElement;
            if (container == null)
            {
                Dispatcher.InvokeAsync(() => FocusTextBoxAtIndex(index), DispatcherPriority.Background);
                return;
            }

            var textBox = FindVisualChild<TextBox>(container);

            if (textBox != null)
            {
                textBox.Focus();
                textBox.CaretIndex = textBox.Text.Length;
            }

            container.BringIntoView();
        }

        private void OpenSearch()
        {
            // NotePageViewModel 찾기 - 부모 컨트롤의 DataContext에서
            var notePageViewModel = FindParentDataContext<NotePageViewModel>(this);

            if (notePageViewModel?.SearchViewModel != null)
            {
                notePageViewModel.SearchViewModel.IsSearchPanelVisible = true;

                // SearchBox에 포커스 설정
                Dispatcher.InvokeAsync(() =>
                {
                    // MainWindow에서 SearchPanel 찾기
                    var window = Window.GetWindow(this);
                    if (window != null)
                    {
                        var searchPanel = FindVisualChild<SearchPanel>(window);
                        if (searchPanel != null)
                        {
                            var searchBox = FindVisualChild<TextBox>(searchPanel, "SearchBox");
                            searchBox?.Focus();
                            searchBox?.SelectAll();
                        }
                    }
                }, DispatcherPriority.Input);
            }
            else
            {
                Debug.WriteLine("[ERROR] SearchViewModel을 찾을 수 없습니다.");
            }
        }

        public void HighlightSearchResult(int lineIndex, int startIndex, int length)
        {
            if (lineIndex < 0 || lineIndex >= ItemsControlContainer.Items.Count)
                return;

            Dispatcher.InvokeAsync(() =>
            {
                // 해당 라인의 컨테이너 가져오기
                var container = ItemsControlContainer.ItemContainerGenerator
                    .ContainerFromIndex(lineIndex) as FrameworkElement;

                if (container != null)
                {
                    var textBox = FindVisualChild<TextBox>(container);
                    if (textBox != null)
                    {
                        // 텍스트박스에 포커스
                        textBox.Focus();

                        // 검색 결과 선택
                        textBox.Select(startIndex, length);

                        // 뷰포트로 스크롤
                        container.BringIntoView();
                    }
                }
            }, DispatcherPriority.Input);
        }


        private Point _startPoint;
        private bool _isDragging = false;
        private MarkdownLineViewModel _draggedItem;

        private void Grid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _startPoint = e.GetPosition(null);
            _draggedItem = ((FrameworkElement)sender).DataContext as MarkdownLineViewModel;
        }

        private void Grid_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && !_isDragging && _draggedItem != null)
            {
                Point position = e.GetPosition(null);

                if (Math.Abs(position.X - _startPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(position.Y - _startPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    _isDragging = true;
                    _draggedItem.IsDragging = true;

                    // 드래그 데이터 생성
                    DataObject data = new DataObject("MarkdownLine", _draggedItem);

                    // 드래그 시작
                    DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Move);

                    _isDragging = false;
                    _draggedItem.IsDragging = false;
                }
            }
        }

        private void Grid_DragOver(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent("MarkdownLine"))
            {
                e.Effects = DragDropEffects.None;
                return;
            }

            e.Effects = DragDropEffects.Move;

            // 드롭 인디케이터 표시
            var grid = sender as Grid;
            if (grid != null)
            {
                var dropIndicator = FindVisualChild<Rectangle>(grid, "DropIndicator");
                if (dropIndicator != null)
                {
                    Point position = e.GetPosition(grid);
                    if (position.Y < grid.ActualHeight / 2)
                    {
                        dropIndicator.VerticalAlignment = VerticalAlignment.Top;
                    }
                    else
                    {
                        dropIndicator.VerticalAlignment = VerticalAlignment.Bottom;
                    }
                    dropIndicator.Visibility = Visibility.Visible;
                }
            }
        }

        private void Grid_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent("MarkdownLine"))
                return;

            var draggedLine = e.Data.GetData("MarkdownLine") as MarkdownLineViewModel;
            var targetLine = ((FrameworkElement)sender).DataContext as MarkdownLineViewModel;

            if (draggedLine == null || targetLine == null || draggedLine == targetLine)
                return;

            var vm = DataContext as NoteEditorViewModel;
            if (vm == null)
                return;

            // 드롭 위치 계산
            var grid = sender as Grid;
            Point position = e.GetPosition(grid);
            bool insertBefore = position.Y < grid.ActualHeight / 2;

            // 드롭 인디케이터 숨기기
            var dropIndicator = FindVisualChild<Rectangle>(grid, "DropIndicator");
            if (dropIndicator != null)
            {
                dropIndicator.Visibility = Visibility.Collapsed;
            }

            // 순서 변경 수행
            vm.ReorderLine(draggedLine, targetLine, insertBefore);
        }

        // Grid_DragLeave 이벤트 핸들러 추가
        private void Grid_DragLeave(object sender, DragEventArgs e)
        {
            var grid = sender as Grid;
            if (grid != null)
            {
                var dropIndicator = FindVisualChild<Rectangle>(grid, "DropIndicator");
                if (dropIndicator != null)
                {
                    dropIndicator.Visibility = Visibility.Collapsed;
                }
            }
        }

        // 이미지 삭제 처리
        private void DeleteImage_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var line = button?.DataContext as MarkdownLineViewModel;

            if (line != null && line.IsImage)
            {
                var vm = DataContext as NoteEditorViewModel;
                if (vm != null)
                {
                    var result = MessageBox.Show("이미지를 삭제하시겠습니까?", "확인",
                        MessageBoxButton.YesNo, MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        // 이미지 파일 삭제
                        DeleteImageFile(line.ImageUrl);

                        // 라인 삭제
                        vm.RemoveLine(line);
                    }
                }
            }
        }

        private void DeleteImageFile(string imageUrl)
        {
            try
            {
                if (!string.IsNullOrEmpty(imageUrl))
                {
                    string fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, imageUrl);
                    if (File.Exists(fullPath))
                    {
                        File.Delete(fullPath);
                        Debug.WriteLine($"[IMAGE] 이미지 파일 삭제됨: {fullPath}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERROR] 이미지 파일 삭제 실패: {ex.Message}");
            }
        }

        private RightSidebarViewModel GetTimerViewModel()
        {
            try
            {
                // MainWindow를 통해 RightSidebar의 ViewModel 찾기
                var mainWindow = Application.Current.MainWindow;
                if (mainWindow != null)
                {
                    var rightSidebar = FindVisualChild<Notea.Modules.Common.Views.RightSidebar>(mainWindow);
                    if (rightSidebar?.DataContext is RightSidebarViewModel timerVM)
                    {
                        return timerVM;
                    }
                }
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[포커스] 타이머 ViewModel 찾기 실패: {ex.Message}");
                return null;
            }
        }


        private T FindVisualChild<T>(DependencyObject parent, string name = null) where T : DependencyObject
        {
            if (parent == null) return null;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is T target)
                    return target;

                var result = FindVisualChild<T>(child);
                if (result != null)
                    return result;
            }
            return null;
        }

    }
}
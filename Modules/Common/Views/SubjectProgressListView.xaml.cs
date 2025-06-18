using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Notea.Modules.Common.Helpers;
using Notea.Modules.Common.ViewModels;
using Notea.Modules.Daily.ViewModels;
using Notea.Modules.Subjects.ViewModels;
using Notea.ViewModels;

namespace Notea.Modules.Common.Views
{
    public partial class SubjectProgressListView : UserControl
    {
        private Point _startPoint;
        private bool _isDragging = false;
        private DateTime _lastClickTime = DateTime.MinValue;
        private const int DoubleClickInterval = 500;

        public SubjectProgressListView()
        {
            InitializeComponent();
        }

        public ObservableCollection<SubjectProgressViewModel> Subjects
        {
            get => (ObservableCollection<SubjectProgressViewModel>)GetValue(SubjectsProperty);
            set => SetValue(SubjectsProperty, value);
        }

        public static readonly DependencyProperty SubjectsProperty =
            DependencyProperty.Register("Subjects", typeof(ObservableCollection<SubjectProgressViewModel>), typeof(SubjectProgressListView), new PropertyMetadata(null));

        // 과목명 드래그 이벤트 (삭제용)
        private void SubjectName_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _startPoint = e.GetPosition(null);
            _isDragging = false;
        }

        private void SubjectName_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && !_isDragging)
            {
                Point mousePos = e.GetPosition(null);
                Vector diff = _startPoint - mousePos;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    _isDragging = true;

                    var textBlock = sender as TextBlock;
                    var subject = textBlock?.DataContext as SubjectProgressViewModel;

                    if (subject != null)
                    {
                        var dragData = new DataObject("RemoveSubjectData", subject);
                        DragDrop.DoDragDrop(textBlock, dragData, DragDropEffects.Move);
                        System.Diagnostics.Debug.WriteLine($"[DragDrop] 과목 '{subject.SubjectName}' 삭제 드래그 시작");
                    }

                    _isDragging = false;
                }
            }
        }

        private void SubjectProgressItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDragging)
            {
                var currentTime = DateTime.Now;
                var timeSinceLastClick = (currentTime - _lastClickTime).TotalMilliseconds;

                if (timeSinceLastClick < DoubleClickInterval)
                {
                    HandleSubjectProgressDoubleClick(sender, e);
                }
                else
                {
                    _lastClickTime = currentTime;
                }
            }

            _isDragging = false;
        }

        private void HandleSubjectProgressDoubleClick(object sender, MouseButtonEventArgs e)
        {
            try
            {
                var grid = sender as Grid;
                var subject = grid?.DataContext as SubjectProgressViewModel;

                if (subject != null)
                {
                    var mainWindow = Application.Current.MainWindow;
                    if (mainWindow?.DataContext is MainViewModel mainViewModel)
                    {
                        if (mainViewModel.NavigateToNoteEditorCommand.CanExecute(subject))
                        {
                            mainViewModel.NavigateToNoteEditorCommand.Execute(subject);
                            System.Diagnostics.Debug.WriteLine($"[SubjectProgressListView] 과목 '{subject.SubjectName}' 더블클릭 - 필기 화면으로 이동");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SubjectProgressListView] 과목 더블클릭 처리 오류: {ex.Message}");
            }
        }

        // TopicGroup 드래그 이벤트 (삭제용)
        private void TopicGroup_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _startPoint = e.GetPosition(null);
            _isDragging = false;
        }

        private void TopicGroup_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && !_isDragging)
            {
                Point mousePos = e.GetPosition(null);
                Vector diff = _startPoint - mousePos;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    _isDragging = true;

                    var grid = sender as Grid;
                    var topicGroup = grid?.DataContext as TopicGroupViewModel;

                    if (topicGroup != null)
                    {
                        // 부모 과목명 찾기
                        var parentSubject = FindParentSubject(grid);
                        if (parentSubject != null)
                        {
                            topicGroup.ParentSubjectName = parentSubject.SubjectName;
                        }

                        var dragData = new DataObject("RemoveTopicGroupData", topicGroup);
                        DragDrop.DoDragDrop(grid, dragData, DragDropEffects.Move);
                        System.Diagnostics.Debug.WriteLine($"[DragDrop] TopicGroup '{topicGroup.GroupTitle}' 삭제 드래그 시작 (부모: {topicGroup.ParentSubjectName})");
                    }

                    _isDragging = false;
                }
            }
        }

        // 부모 과목 찾기 헬퍼 메소드
        private SubjectProgressViewModel FindParentSubject(DependencyObject child)
        {
            DependencyObject parent = System.Windows.Media.VisualTreeHelper.GetParent(child);

            while (parent != null)
            {
                if (parent is FrameworkElement element && element.DataContext is SubjectProgressViewModel subject)
                {
                    return subject;
                }
                parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);
            }
            return null;
        }

        private void SubjectProgressListView_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("SubjectData") ||
                e.Data.GetDataPresent("TopicData") ||
                e.Data.GetDataPresent("RemoveSubjectData") ||
                e.Data.GetDataPresent("RemoveTopicGroupData"))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void SubjectProgressListView_Drop(object sender, DragEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("[DragDrop] Drop 이벤트 발생");

            try
            {
                // 공유 데이터 소스 찾기
                ObservableCollection<SubjectProgressViewModel> targetCollection = null;

                if (DataContext is LeftSidebarViewModel sidebarVM && sidebarVM.SharedSubjectProgress != null)
                {
                    targetCollection = sidebarVM.SharedSubjectProgress;
                }
                else if (DataContext is DailyBodyViewModel dailyBodyVM)
                {
                    targetCollection = dailyBodyVM.Subjects;
                }

                if (targetCollection == null)
                {
                    System.Diagnostics.Debug.WriteLine("[DragDrop] 대상 컬렉션을 찾을 수 없음");
                    return;
                }

                // 과목 제거 처리
                if (e.Data.GetDataPresent("RemoveSubjectData"))
                {
                    HandleRemoveSubject(e, targetCollection);
                }
                // TopicGroup 제거 처리
                else if (e.Data.GetDataPresent("RemoveTopicGroupData"))
                {
                    HandleRemoveTopicGroup(e, targetCollection);
                }
                // 과목 추가 처리
                else if (e.Data.GetDataPresent("SubjectData"))
                {
                    HandleAddSingleSubject(e, targetCollection);
                }
                // TopicGroup 추가 처리
                else if (e.Data.GetDataPresent("TopicData"))
                {
                    HandleAddSingleTopicGroup(e, targetCollection);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DragDrop] 오류 발생: {ex.Message}");
            }

            e.Handled = true;
        }

        // ✅ 과목 제거 - 단순화 (DailySubject에서만 제거, 실제 시간은 StudySession에 보존)
        private void HandleRemoveSubject(DragEventArgs e, ObservableCollection<SubjectProgressViewModel> targetCollection)
        {
            var subjectToRemove = e.Data.GetData("RemoveSubjectData") as SubjectProgressViewModel;

            if (subjectToRemove != null)
            {
                var existingSubject = targetCollection.FirstOrDefault(s =>
                    string.Equals(s.SubjectName, subjectToRemove.SubjectName, StringComparison.OrdinalIgnoreCase));

                if (existingSubject != null)
                {
                    var subjectName = existingSubject.SubjectName;

                    // ✅ 1단계: UI 컬렉션에서 제거
                    targetCollection.Remove(existingSubject);

                    // ✅ 2단계: DailySubject에서만 제거 (실제 시간은 StudySession에 보존됨)
                    var dbHelper = DatabaseHelper.Instance;
                    dbHelper.RemoveDailySubject(DateTime.Today, subjectName);

                    System.Diagnostics.Debug.WriteLine($"[DragDrop] 과목 '{subjectName}' 오늘 할 일에서 제거 (실제 시간은 StudySession에 보존)");
                }
            }
        }

        // ✅ TopicGroup 제거 - 단순화 
        private void HandleRemoveTopicGroup(DragEventArgs e, ObservableCollection<SubjectProgressViewModel> targetCollection)
        {
            var topicGroupToRemove = e.Data.GetData("RemoveTopicGroupData") as TopicGroupViewModel;

            if (topicGroupToRemove != null && !string.IsNullOrEmpty(topicGroupToRemove.ParentSubjectName))
            {
                var parentSubject = targetCollection.FirstOrDefault(s =>
                    string.Equals(s.SubjectName, topicGroupToRemove.ParentSubjectName, StringComparison.OrdinalIgnoreCase));

                if (parentSubject != null)
                {
                    var existingTopicGroup = parentSubject.TopicGroups.FirstOrDefault(t =>
                        string.Equals(t.GroupTitle, topicGroupToRemove.GroupTitle, StringComparison.OrdinalIgnoreCase));

                    if (existingTopicGroup != null)
                    {
                        // ✅ UI에서 제거 (실제 시간은 StudySession에 보존됨)
                        parentSubject.TopicGroups.Remove(existingTopicGroup);

                        System.Diagnostics.Debug.WriteLine($"[DragDrop] TopicGroup '{existingTopicGroup.GroupTitle}' 제거됨 (실제 시간은 보존)");
                        System.Diagnostics.Debug.WriteLine($"[DragDrop] 부모 과목 '{parentSubject.SubjectName}' 유지됨 (TopicGroup 개수: {parentSubject.TopicGroups.Count})");
                    }
                }
            }
        }

        // ✅ 과목 추가 - 단순화 (실제 시간은 자동으로 StudySession에서 조회됨)
        private void HandleAddSingleSubject(DragEventArgs e, ObservableCollection<SubjectProgressViewModel> targetCollection)
        {
            var droppedSubject = e.Data.GetData("SubjectData") as SubjectGroupViewModel;
            System.Diagnostics.Debug.WriteLine($"[DragDrop] 드롭된 과목: {droppedSubject?.SubjectName}");

            if (droppedSubject != null)
            {
                var existingSubject = targetCollection.FirstOrDefault(s =>
                    string.Equals(s.SubjectName, droppedSubject.SubjectName, StringComparison.OrdinalIgnoreCase));

                if (existingSubject == null)
                {
                    // ✅ 새 과목 생성 (시간은 TodayStudyTimeSeconds에서 자동 조회)
                    var newSubjectProgress = new SubjectProgressViewModel
                    {
                        SubjectName = droppedSubject.SubjectName
                    };

                    targetCollection.Add(newSubjectProgress);
                    System.Diagnostics.Debug.WriteLine($"[DragDrop] 과목 '{droppedSubject.SubjectName}' 추가됨 (시간 자동 조회: {newSubjectProgress.TodayStudyTimeSeconds}초)");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[DragDrop] 과목 '{droppedSubject.SubjectName}' 이미 존재함");
                }
            }
        }

        // ✅ TopicGroup 추가 - 단순화
        private void HandleAddSingleTopicGroup(DragEventArgs e, ObservableCollection<SubjectProgressViewModel> targetCollection)
        {
            var droppedTopic = e.Data.GetData("TopicData") as TopicGroupViewModel;
            var parentSubjectName = e.Data.GetData("ParentSubjectName") as string;

            System.Diagnostics.Debug.WriteLine($"[DragDrop] 드롭된 분류: {droppedTopic?.GroupTitle}");
            System.Diagnostics.Debug.WriteLine($"[DragDrop] 부모 과목명: {parentSubjectName ?? droppedTopic?.ParentSubjectName}");

            if (droppedTopic != null)
            {
                var effectiveParentName = parentSubjectName ?? droppedTopic.ParentSubjectName;

                if (!string.IsNullOrEmpty(effectiveParentName))
                {
                    var existingSubject = targetCollection.FirstOrDefault(s =>
                        string.Equals(s.SubjectName, effectiveParentName, StringComparison.OrdinalIgnoreCase));

                    if (existingSubject != null)
                    {
                        // 기존 과목에 TopicGroup 추가
                        var existingTopic = existingSubject.TopicGroups.FirstOrDefault(t =>
                            string.Equals(t.GroupTitle, droppedTopic.GroupTitle, StringComparison.OrdinalIgnoreCase));

                        if (existingTopic == null)
                        {
                            var newTopicGroup = new TopicGroupViewModel
                            {
                                GroupTitle = droppedTopic.GroupTitle,
                                ParentSubjectName = effectiveParentName,
                                Topics = new ObservableCollection<Notea.Modules.Subjects.Models.TopicItem>()
                            };

                            newTopicGroup.SetParentTodayStudyTime(existingSubject.TodayStudyTimeSeconds);
                            existingSubject.TopicGroups.Add(newTopicGroup);

                            System.Diagnostics.Debug.WriteLine($"[DragDrop] 기존 과목 '{effectiveParentName}'에 TopicGroup '{droppedTopic.GroupTitle}' 추가됨");
                        }
                    }
                    else
                    {
                        // 새 과목과 TopicGroup 함께 추가
                        var newSubjectProgress = new SubjectProgressViewModel
                        {
                            SubjectName = effectiveParentName
                        };

                        var newTopicGroup = new TopicGroupViewModel
                        {
                            GroupTitle = droppedTopic.GroupTitle,
                            ParentSubjectName = effectiveParentName,
                            Topics = new ObservableCollection<Notea.Modules.Subjects.Models.TopicItem>()
                        };

                        newTopicGroup.SetParentTodayStudyTime(newSubjectProgress.TodayStudyTimeSeconds);
                        newSubjectProgress.TopicGroups.Add(newTopicGroup);
                        targetCollection.Add(newSubjectProgress);

                        System.Diagnostics.Debug.WriteLine($"[DragDrop] 새 과목 '{effectiveParentName}'과 TopicGroup '{droppedTopic.GroupTitle}' 추가됨");
                    }
                }
            }
        }

    }
}
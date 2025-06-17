using Notea.Helpers;
using Notea.Modules.Monthly.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Notea.Modules.Monthly.Views
{
    /// <summary>
    /// CalendarMonth.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class CalendarMonth : UserControl, INotifyPropertyChanged
    {

        private DateTime _currentDate;
        public DateTime CurrentDate
        {
            get { return _currentDate; }
            set
            {
                if (_currentDate != value)
                {
                    _currentDate = value;
                    OnPropertyChanged(() => CurrentDate);
                    SetDateByCurrentDate();
                    LoadEvents();
                    LoadMonthComment(); // 추가
                    DrawDays();
                }
            }
        }

        public ObservableCollection<CalendarDay> DaysInCurrentMonth { get; set; }
        public event PropertyChangedEventHandler PropertyChanged;

        public SolidColorBrush TextDefaultColor
        {
            get { return (SolidColorBrush)GetValue(TextDefaultColorProperty); }
            set { SetValue(TextDefaultColorProperty, value); }
        }

        public static readonly DependencyProperty TextDefaultColorProperty =
         DependencyProperty.Register(
             name: "TextDefaultColor",
             propertyType: typeof(SolidColorBrush),
             ownerType: typeof(CalendarMonth),
             typeMetadata: new PropertyMetadata(
                 defaultValue: new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFFFFF"))
             )
         );
        public SolidColorBrush TextHighlightColor
        {
            get { return (SolidColorBrush)GetValue(TextHighlightColorProperty); }
            set { SetValue(TextHighlightColorProperty, value); }
        }

        public static readonly DependencyProperty TextHighlightColorProperty =
         DependencyProperty.Register(
             name: "TextHighlightColor",
             propertyType: typeof(SolidColorBrush),
             ownerType: typeof(CalendarMonth),
             typeMetadata: new PropertyMetadata(
                 defaultValue: new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#000000"))
             )
         );

        public SolidColorBrush DefaultColor
        {
            get { return (SolidColorBrush)GetValue(DefaultColorProperty); }
            set { SetValue(DefaultColorProperty, value); }
        }

        public static readonly DependencyProperty DefaultColorProperty =
         DependencyProperty.Register(
             name: "DefaultColor",
             propertyType: typeof(SolidColorBrush),
             ownerType: typeof(CalendarMonth),
             typeMetadata: new PropertyMetadata(
                 defaultValue: new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1E1E1E"))
             )
         );

        public SolidColorBrush HighlightColor
        {
            get { return (SolidColorBrush)GetValue(HighlightColorProperty); }
            set { SetValue(HighlightColorProperty, value); }
        }
        public static readonly DependencyProperty HighlightColorProperty =
         DependencyProperty.Register(
             name: "HighlightColor",
             propertyType: typeof(SolidColorBrush),
             ownerType: typeof(CalendarMonth),
             typeMetadata: new PropertyMetadata(
                 defaultValue: new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#DDDDDD"))
             )
         );


        public CalendarMonth()
        {
            InitializeComponent();
            DaysInCurrentMonth = new ObservableCollection<CalendarDay>();
            InitializeDate();
            InitializeDayLabels();
        }

        

        private void InitializeDate()
        {
            CurrentDate = DateTime.Now;
            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1E1E1E");
        }

        private void InitializeDayLabels()
        {
            for (int i = 0; i < 7; i++)
            {
                string dayName = CultureInfo.InvariantCulture.DateTimeFormat.DayNames[i % 7];
                string shortDayName = dayName.Substring(0, Math.Min(3, dayName.Length));
                Label dayLabel = new Label();
                dayLabel.HorizontalContentAlignment = HorizontalAlignment.Center;
                dayLabel.SetValue(Grid.ColumnProperty, i);
                dayLabel.Content = shortDayName;
                dayLabel.Style = (Style)FindResource("weekStyle");
                DayLabelsGrid.Children.Add(dayLabel);
            }
        }

        public IEnumerable<ICalendarPlan> Events
        {
            get { return (IEnumerable<ICalendarPlan>)GetValue(EventsProperty); }
            set { SetValue(EventsProperty, value); }
        }
        private List<ICalendarPlan> _events = new();

        public static readonly DependencyProperty EventsProperty =
         DependencyProperty.Register(
         "Events",
         typeof(IEnumerable<ICalendarPlan>),
         typeof(CalendarMonth),
         new PropertyMetadata(null));


        private void SetDateByCurrentDate()
        {
            date.Text = CurrentDate.Year.ToString() + " / " + CurrentDate.Month.ToString("00");
        }

        internal void CalendarEventClicked(CalendarEventView eventToSelect)
        {
            foreach (CalendarDay day in DaysInCurrentMonth)
            {
                foreach (CalendarEventView e in day.Events.Children)
                {
                    if (e.DataContext == eventToSelect.DataContext)
                    {
                        e.BackgroundColor = HighlightColor;
                        e.Foreground = TextHighlightColor;
                    }
                    else
                    {
                        e.BackgroundColor = e.DefaultBackfoundColor;
                        e.Foreground = TextDefaultColor;

                    }
                }
            }
        }

        public void CalendarEventDoubleClicked(CalendarEventView calendarEventView)
        {
            var eventData = calendarEventView.DataContext as ICalendarPlan;
            if (eventData == null) return;

            var window = new EventDetailWindow(eventData);
            window.Owner = Window.GetWindow(this);

            if (window.ShowDialog() == true)
            {
                if (window.IsDeleted)
                {
                    var list = Events as IList<ICalendarPlan>;
                    list?.Remove(eventData);

                    // planId 기반 안전 삭제
                    if (eventData is MonthlyPlan monthlyPlan)
                    {
                        string deleteQuery = $@"
                            DELETE FROM monthlyEvent
                            WHERE planId = {monthlyPlan.PlanId};
                        ";
                        DatabaseHelper.ExecuteNonQuery(deleteQuery);
                    }
                    DrawDays();
                }
                if (!window.IsDeleted)
                {
                    if(eventData is MonthlyPlan monthlyPlan)
                    {
                        string updateQuery = $@"
                            UPDATE monthlyEvent
                            SET title = '{monthlyPlan.Title.Replace("'", "''")}',
                                description = '{monthlyPlan.Description?.Replace("'", "''")}',
                                isDday = {Convert.ToInt32(monthlyPlan.IsDday)},
                                startDate = '{monthlyPlan.StartDate?.ToString("yyyy-MM-dd HH:mm:ss")}',
                                endDate = '{monthlyPlan.EndDate?.ToString("yyyy-MM-dd HH:mm:ss")}',
                                color = '{monthlyPlan.Color}'
                            WHERE planId = {monthlyPlan.PlanId};
                        ";

                        DatabaseHelper.ExecuteNonQuery(updateQuery);
                        DrawDays();
                    }
                }
            }
        }

        private void PreviousMonthButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (CurrentDate.Month == 1)
            {
                CurrentDate = CurrentDate.AddYears(-1);
            }
            CurrentDate = CurrentDate.AddMonths(-1);
        }

        private void NextMonthButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (CurrentDate.Month == 12)
            {
                CurrentDate = CurrentDate.AddYears(1);
            }
            CurrentDate = CurrentDate.AddMonths(1);
        }

        public void OnPropertyChanged<T>(Expression<Func<T>> exp)
        {
            //the cast will always succeed
            var memberExpression = (MemberExpression)exp.Body;
            var propertyName = memberExpression.Member.Name;

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void OnDayAddEventRequested(DateTime date)
        {
            var newEvent = new MonthlyPlan
            {
                StartDate = date,
                EndDate = date,
                Title = "",
                Description = ""
            };

            var window = new EventDetailWindow(newEvent);
            window.Owner = Window.GetWindow(this);

            if (window.ShowDialog() == true)
            {
                string formattedStartDate = newEvent.StartDate.HasValue
                                       ? newEvent.StartDate.Value.ToString("yyyy-MM-dd HH:mm:ss")
                                       : null;
                string formattedEndDate = newEvent.EndDate.HasValue
                                            ? newEvent.EndDate.Value.ToString("yyyy-MM-dd HH:mm:ss")
                                            : null;

                var list = Events as IList<ICalendarPlan>;
                string insertQuery = $@"
                    INSERT INTO monthlyEvent (title, description, isDday, startDate, endDate, color)
                    VALUES (
                        '{newEvent.Title}',
                        '{newEvent.Description}',
                        '{newEvent.IsDday}',
                        '{formattedStartDate}',
                        '{formattedEndDate}',
                        '#1E1E1E'
                    );
                ";

                int result = DatabaseHelper.ExecuteNonQuery(insertQuery);
                MessageBox.Show(result > 0 ? "일정 저장 성공" : "저장 실패");
                list?.Add(newEvent);
                DrawDays();
            }
        }



        public void DrawDays()
        {
            DaysGrid.Children.Clear();
            DaysInCurrentMonth.Clear();

            DateTime firstDayOfMonth = new DateTime(CurrentDate.Year, CurrentDate.Month, 1);
            DateTime lastDayOfMonth = firstDayOfMonth.AddMonths(1).AddDays(-1);

            for (DateTime date = firstDayOfMonth; date.Date <= lastDayOfMonth; date = date.AddDays(1))
            {
                CalendarDay newDay = new CalendarDay();
                newDay.Date = date;
                newDay.AddEventRequested += OnDayAddEventRequested;
                DaysInCurrentMonth.Add(newDay);
            }

            int row = 0;
            int column = 0;

            for (int i = 0; i < DaysInCurrentMonth.Count; i++)
            {
                switch (DaysInCurrentMonth[i].Date.DayOfWeek)
                {
                    case DayOfWeek.Sunday:
                        column = 0;
                        break;
                    case DayOfWeek.Monday:
                        column = 1;
                        break;
                    case DayOfWeek.Tuesday:
                        column = 2;
                        break;
                    case DayOfWeek.Wednesday:
                        column = 3;
                        break;
                    case DayOfWeek.Thursday:
                        column = 4;
                        break;
                    case DayOfWeek.Friday:
                        column = 5;
                        break;
                    case DayOfWeek.Saturday:
                        column = 6;
                        break;

                }

                Grid.SetRow(DaysInCurrentMonth[i], row);
                Grid.SetColumn(DaysInCurrentMonth[i], column);
                DaysGrid.Children.Add(DaysInCurrentMonth[i]);

                var day = DaysInCurrentMonth[i];
                var ddayEvent = Events?
                    .OfType<MonthlyPlan>()
                    .FirstOrDefault(ev => ev.IsDday && ev.StartDate?.Date == day.Date.Date);

                if (ddayEvent != null)
                {
                    day.DdayCircle.BorderBrush = Brushes.Red;
                    day.DateTextBlock.Foreground = Brushes.Black;
                    day.Title = ddayEvent.Title.ToString();
                }
                else
                {
                    day.DdayCircle.BorderBrush = Brushes.Transparent;
                    day.DateTextBlock.Foreground = Brushes.Black;
                    day.Title = null;
                }

                if (column == 6)
                {
                    row++;
                }
            }

            CalendarDay today = DaysInCurrentMonth.Where(d => d.Date == DateTime.Today).FirstOrDefault();
            if (today != null)
            {
                today.dayGrid.Background = new SolidColorBrush(Colors.Transparent);
            }

            DrawEvents();
        }
        private void DrawEvents()
        {
            if (Events == null)
            {
                return;
            }

            if (Events is IEnumerable<ICalendarPlan> events)
            {

                foreach (var e in events.OrderBy(e => e.StartDate))
                {
                    if (!e.StartDate.HasValue || !e.EndDate.HasValue)
                    {
                        continue;
                    }

                    int eventRow = 0;

                    var dateFrom = (DateTime)e.StartDate;
                    var dateTo = (DateTime)e.EndDate;

                    for (DateTime date = dateFrom; date <= dateTo; date = date.AddDays(1))
                    {
                        CalendarDay day = DaysInCurrentMonth.Where(d => d.Date.Date == date.Date).FirstOrDefault();

                        if (day == null)
                        {
                            continue;
                        }

                        if (day.Date.DayOfWeek == DayOfWeek.Sunday)
                        {
                            eventRow = 0;
                        }

                        if (day.Events.Children.Count > eventRow)
                        {
                            eventRow = Grid.GetRow(day.Events.Children[day.Events.Children.Count - 1]) + 1;
                        }

                        CalendarEventView calendarEventView = new CalendarEventView(DefaultColor, this);

                        if (date != dateFrom && date <= dateTo)
                            calendarEventView.EventTextBlock.Text = null;

                        calendarEventView.DataContext = e;
                        Grid.SetRow(calendarEventView, eventRow);
                        day.Events.Children.Add(calendarEventView);
                    }
                }
            }
            else
            {
                throw new ArgumentException("Events must be IEnumerable<ICalendarEvent>");
            }
        }

        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {

        }

        public void LoadEvents()
        {
            var list = new ObservableCollection<ICalendarPlan>();
            string query = "SELECT * FROM monthlyEvent ORDER BY startDate ASC";
            DataTable dt = DatabaseHelper.ExecuteSelect(query);

            foreach (DataRow row in dt.Rows)
            {
                list.Add(new MonthlyPlan
                {
                    PlanId = Convert.ToInt32(row["planId"]),
                    Title = row["title"].ToString(),
                    Description = row["description"]?.ToString(), // 필드명도 수정
                    IsDday = Convert.ToBoolean(row["isDday"]),
                    StartDate = Convert.ToDateTime(row["startDate"]),
                    EndDate = Convert.ToDateTime(row["endDate"]),
                    Color = row["color"]?.ToString()
                });
            }

            Events = list;
            DrawDays(); // 이 메서드가 Events를 다시 호출하지 않도록 확인 필요
        }

        private string _monthComment;
        public string MonthComment
        {
            get => _monthComment;
            set
            {
                if (_monthComment != value)
                {
                    _monthComment = value;
                    OnPropertyChanged(() => MonthComment);
                }
            }
        }

        private void LoadMonthComment()
        {
            try
            {
                // 현재 월의 첫 날로 날짜 설정
                DateTime monthDate = new DateTime(CurrentDate.Year, CurrentDate.Month, 1);

                string query = $@"
                SELECT comment 
                FROM monthlyComment 
                WHERE date(monthDate) = date('{monthDate:yyyy-MM-dd}')";

                var result = DatabaseHelper.ExecuteSelect(query);
                if (result.Rows.Count > 0)
                {
                    MonthComment = result.Rows[0]["comment"]?.ToString() ?? "";
                }
                else
                {
                    MonthComment = "";
                }

                Debug.WriteLine($"[COMMENT] {CurrentDate:yyyy-MM} 코멘트 로드: {MonthComment}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERROR] 월별 코멘트 로드 실패: {ex.Message}");
                MonthComment = "";
            }
        }

        // Comment 저장
        private void SaveMonthComment()
        {
            try
            {
                DateTime monthDate = new DateTime(CurrentDate.Year, CurrentDate.Month, 1);

                string query = $@"
                INSERT OR REPLACE INTO monthlyComment (monthDate, comment)
                VALUES ('{monthDate:yyyy-MM-dd}', '{MonthComment?.Replace("'", "''")}')";

                DatabaseHelper.ExecuteNonQuery(query);
                Debug.WriteLine($"[COMMENT] {CurrentDate:yyyy-MM} 코멘트 저장: {MonthComment}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERROR] 월별 코멘트 저장 실패: {ex.Message}");
            }
        }

        // TextBox LostFocus 이벤트 핸들러
        private void CommentTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            SaveMonthComment();
        }

    }
}

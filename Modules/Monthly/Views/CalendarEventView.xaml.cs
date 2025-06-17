using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Notea.Modules.Monthly.Views
{
    /// <summary>
    /// CalendarEventView.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class CalendarEventView
    {
        private CalendarMonth _calendar;

        public SolidColorBrush BackgroundColor
        {
            get { return (SolidColorBrush)GetValue(BackgroundColorProperty); }
            set { SetValue(BackgroundColorProperty, value); }
        }

        public static readonly DependencyProperty BackgroundColorProperty =
            DependencyProperty.Register("BackgroundColor", typeof(SolidColorBrush), typeof(CalendarEventView));

        public SolidColorBrush DefaultBackfoundColor;
        private DependencyProperty defaultColorProperty;
        private CalendarMonth calendarMonth;

        public CalendarEventView(SolidColorBrush backgroundColor, CalendarMonth parent)
        {
            InitializeComponent();
            this._calendar = parent;
            this.BackgroundColor = backgroundColor;
            this.DefaultBackfoundColor = backgroundColor;

        }

        public CalendarEventView(DependencyProperty defaultColorProperty, CalendarMonth calendarMonth)
        {
            this.defaultColorProperty = defaultColorProperty;
            this.calendarMonth = calendarMonth;
        }

        private void EventMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2)
            {
                _calendar.CalendarEventDoubleClicked(this);
            }
            else if (e.ChangedButton == MouseButton.Left && e.ClickCount == 1)
            {
            }
        }

        private void EventMouseHover(object sender, MouseButtonEventArgs e)
        {
            _calendar.CalendarEventClicked(this);
        }
    }
}

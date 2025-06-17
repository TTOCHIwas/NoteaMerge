using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Windows;
using System.Windows.Controls;

namespace Notea.Controls
{
    public class TextBoxController : TextBox
    {
        public static readonly DependencyProperty PlaceHolderProperty =
            DependencyProperty.Register("PlaceHolder", typeof(string), typeof(TextBoxController), new PropertyMetadata(string.Empty));

        public string PlaceHolder
        {
            get { return (string)GetValue(PlaceHolderProperty); }
            set { SetValue(PlaceHolderProperty, value); }
        }

        static TextBoxController()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(TextBoxController), new FrameworkPropertyMetadata(typeof(TextBoxController)));
        }
    }
}

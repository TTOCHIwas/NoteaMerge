using System.Windows;
using System.Windows.Controls;

namespace Notea.Modules.Common.Views
{
    public partial class CenterPanelShell : UserControl
    {
        public CenterPanelShell()
        {
            InitializeComponent();
        }

        public static readonly DependencyProperty HeaderContentProperty =
        DependencyProperty.Register("HeaderContent", typeof(object), typeof(CenterPanelShell), new PropertyMetadata(null));

        public object HeaderContent
        {
            get => GetValue(HeaderContentProperty);
            set => SetValue(HeaderContentProperty, value);
        }

        public static readonly DependencyProperty BodyContentProperty =
            DependencyProperty.Register("BodyContent", typeof(object), typeof(CenterPanelShell), new PropertyMetadata(null));

        public object BodyContent
        {
            get => GetValue(BodyContentProperty);
            set => SetValue(BodyContentProperty, value);
        }

    }

}

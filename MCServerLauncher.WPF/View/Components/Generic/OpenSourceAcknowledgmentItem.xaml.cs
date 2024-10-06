using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace MCServerLauncher.WPF.View.Components.Generic
{
    /// <summary>
    /// OpenSourceAcknowledgmentItem.xaml 的交互逻辑
    /// </summary>
    public partial class OpenSourceAcknowledgmentItem : UserControl
    {
        public OpenSourceAcknowledgmentItem()
        {
            InitializeComponent();
        }
        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }
        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register("Title", typeof(string), typeof(OpenSourceAcknowledgmentItem),
                new PropertyMetadata("", OnTitleChanged));

        private static void OnTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not OpenSourceAcknowledgmentItem control) return;
            if (e.NewValue is not string title) return;
            control.AckTitle.Text = title;
        }

        public string Description
        {
            get => (string)GetValue(DescriptionProperty);
            set => SetValue(DescriptionProperty, value);
        }
        public static readonly DependencyProperty DescriptionProperty =
            DependencyProperty.Register("Description", typeof(string), typeof(OpenSourceAcknowledgmentItem),
                new PropertyMetadata("", OnDescriptionChanged));

        private static void OnDescriptionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not OpenSourceAcknowledgmentItem control) return;
            if (e.NewValue is not string desc) return;
            control.AckDesc.Text = desc;
        }

        public string ButtonText
        {
            get => (string)GetValue(ButtonTextProperty);
            set => SetValue(ButtonTextProperty, value);
        }
        public static readonly DependencyProperty ButtonTextProperty =
            DependencyProperty.Register("ButtonText", typeof(string), typeof(OpenSourceAcknowledgmentItem),
                new PropertyMetadata("", OnButtonTextChanged));

        private static void OnButtonTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not OpenSourceAcknowledgmentItem control) return;
            if (e.NewValue is not string desc) return;
            control.AckButton.Content = desc;
            control.AckDesc.Visibility = string.IsNullOrEmpty(desc) ? Visibility.Hidden : Visibility.Visible;
        }

        public string? ButtonUrl { get; set; }

        private void ActionButtonTriggered(object sender, RoutedEventArgs e)
        {
            Process.Start(ButtonUrl);
        }
    }
}

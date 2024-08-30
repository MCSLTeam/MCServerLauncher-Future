using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace MCServerLauncher.WPF.View.Components.Generic
{
    /// <summary>
    /// AcknowledgmentItem.xaml 的交互逻辑
    /// </summary>
    public partial class AcknowledgmentItem : UserControl
    {
        public AcknowledgmentItem()
        {
            InitializeComponent();
        }
        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }
        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register("Title", typeof(string), typeof(AcknowledgmentItem),
                new PropertyMetadata("", OnTitleChanged));

        private static void OnTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not AcknowledgmentItem control) return;
            if (e.NewValue is not string title) return;
            control.AckTitle.Text = title;
        }
        public string ImagePath
        {
            get => (string)GetValue(ImagePathProperty);
            set => SetValue(ImagePathProperty, value);
        }
        public static readonly DependencyProperty ImagePathProperty =
            DependencyProperty.Register("ImagePath", typeof(string), typeof(AcknowledgmentItem),
                new PropertyMetadata("", OnImagePathChanged));

        private static void OnImagePathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not AcknowledgmentItem control) return;
            if (e.NewValue is not string imagePath) return;
            control.Pic.Source = new BitmapImage(new Uri(imagePath, UriKind.Relative));
        }

        public string Description
        {
            get => (string)GetValue(DescriptionProperty);
            set => SetValue(DescriptionProperty, value);
        }
        public static readonly DependencyProperty DescriptionProperty =
            DependencyProperty.Register("Description", typeof(string), typeof(AcknowledgmentItem),
                new PropertyMetadata("", OnDescriptionChanged));

        private static void OnDescriptionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not AcknowledgmentItem control) return;
            if (e.NewValue is not string desc) return;
            control.AckDesc.Text = desc;
        }

        public string ButtonText
        {
            get => (string)GetValue(ButtonTextProperty);
            set => SetValue(ButtonTextProperty, value);
        }
        public static readonly DependencyProperty ButtonTextProperty =
            DependencyProperty.Register("ButtonText", typeof(string), typeof(AcknowledgmentItem),
                new PropertyMetadata("", OnButtonTextChanged));

        private static void OnButtonTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not AcknowledgmentItem control) return;
            if (e.NewValue is not string desc) return;
            control.AckButton.Content = desc;
            control.AckDesc.Visibility = string.IsNullOrEmpty(desc) ? Visibility.Hidden : Visibility.Visible;
        }

        public string ButtonUrl { get; set; }

        private void ActionButtonTriggered(object sender, RoutedEventArgs e)
        {
            Process.Start(ButtonUrl);
        }
    }
}

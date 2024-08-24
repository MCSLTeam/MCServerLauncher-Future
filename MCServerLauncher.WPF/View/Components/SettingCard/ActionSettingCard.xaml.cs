using System.Windows;
using iNKORE.UI.WPF.Modern.Common.IconKeys;

namespace MCServerLauncher.WPF.View.Components.SettingCard
{
    /// <summary>
    ///    ActionSettingCard.xaml 的交互逻辑
    /// </summary>
    public partial class ActionSettingCard
    {
        public ActionSettingCard()
        {
            InitializeComponent();
        }

        /// <summary>
        ///    Setting title.
        /// </summary>
        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }
        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register("Title", typeof(string), typeof(ActionSettingCard),
                new PropertyMetadata("", OnTitleChanged));

        private static void OnTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ActionSettingCard control) return;
            if (e.NewValue is not string title) return;
            control.SettingTitle.Text = title;
        }

        /// <summary>
        ///    Setting description.
        /// </summary>
        public string Description
        {
            get => (string)GetValue(DescriptionProperty);
            set => SetValue(DescriptionProperty, value);
        }
        public static readonly DependencyProperty DescriptionProperty =
            DependencyProperty.Register("Description", typeof(string), typeof(ActionSettingCard),
                new PropertyMetadata("", OnDescriptionChanged));

        private static void OnDescriptionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ActionSettingCard control) return;
            if (e.NewValue is not string description) return;
            control.SettingDescription.Text = description;
        }

        /// <summary>
        ///    Setting icon.
        /// </summary>
        public FontIconData? Icon
        {
            get => SettingIcon.Icon;
            set => SettingIcon.Icon = value;
        }

        /// <summary>
        ///    Controls the accent color of the button.
        /// </summary>
        public bool IsAccentButtonStyle
        {
            get => SettingButton.Style == (Style)FindResource("AccentButtonStyle");
            set => SettingButton.Style = value ? (Style)FindResource("AccentButtonStyle") : null;
        }

        /// <summary>
        ///    Text of the button.
        /// </summary>
        public string ButtonContent
        {
            get => (string)GetValue(ButtonContentProperty);
            set => SetValue(ButtonContentProperty, value);
        }
        public static readonly DependencyProperty ButtonContentProperty =
            DependencyProperty.Register("ButtonContent", typeof(string), typeof(ActionSettingCard),
                new PropertyMetadata("", OnButtonContentChanged));

        private static void OnButtonContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ActionSettingCard control) return;
            if (e.NewValue is not string buttonContent) return;
            control.SettingDescription.Text = buttonContent;
        }
    }
}
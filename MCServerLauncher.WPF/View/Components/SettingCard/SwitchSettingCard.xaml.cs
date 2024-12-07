using iNKORE.UI.WPF.Modern.Common.IconKeys;
using System.Windows;

namespace MCServerLauncher.WPF.View.Components.SettingCard
{
    /// <summary>
    ///    SwitchSettingCard.xaml 的交互逻辑
    /// </summary>
    public partial class SwitchSettingCard
    {
        public SwitchSettingCard()
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
            DependencyProperty.Register("Title", typeof(string), typeof(SwitchSettingCard),
                new PropertyMetadata("", OnTitleChanged));

        private static void OnTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not SwitchSettingCard control) return;
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
            DependencyProperty.Register("Description", typeof(string), typeof(SwitchSettingCard),
                new PropertyMetadata("", OnDescriptionChanged));

        private static void OnDescriptionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not SwitchSettingCard control) return;
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
        ///    Status of ToggleSwitch.
        /// </summary>
        public bool Status
        {
            get => (bool)GetValue(StatusProperty);
            set => SetValue(StatusProperty, value);
        }
        public static readonly DependencyProperty StatusProperty =
            DependencyProperty.Register("Status", typeof(bool), typeof(SwitchSettingCard),
                new PropertyMetadata(false, OnStatusChanged));

        private static void OnStatusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not SwitchSettingCard control) return;
            if (e.NewValue is not bool status) return;
            control.SettingSwitch.IsOn = status;
        }
    }
}
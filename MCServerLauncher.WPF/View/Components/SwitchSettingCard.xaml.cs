using iNKORE.UI.WPF.Helpers;
using iNKORE.UI.WPF.Modern.Common.IconKeys;
using System.Collections.Generic;
using System.Windows;

namespace MCServerLauncher.WPF.View.Components
{
    /// <summary>
    ///     SwitchSettingCard.xaml 的交互逻辑
    /// </summary>
    public partial class SwitchSettingCard
    {
        public SwitchSettingCard()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Setting title.
        /// </summary>
        public string Title
        {
            get => SettingTitle.Text;
            set => SettingTitle.Text = value;
        }

        /// <summary>
        /// Setting description.
        /// </summary>
        public string Description
        {
            get => SettingDescription.Text;
            set => SettingDescription.Text = value;
        }

        /// <summary>
        /// Setting icon.
        /// </summary>
        public FontIconData? Icon
        {
            get => SettingIcon.Icon;
            set => SettingIcon.Icon = value;
        }

        /// <summary>
        /// Status of ToggleSwitch.
        /// </summary>
        public bool Status
        {
            get => (bool)GetValue(StatusProperty);
            set => SetValue(StatusProperty, value);
        }

        public static readonly DependencyProperty StatusProperty =
            DependencyProperty.Register("Status", typeof(bool), typeof(SwitchSettingCard), new PropertyMetadata(false, OnStatusChanged));

        private static void OnStatusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not SwitchSettingCard control) return;
            if (e.NewValue is not bool status) return;
            control.SettingSwitch.IsOn = status;
        }
    }
}
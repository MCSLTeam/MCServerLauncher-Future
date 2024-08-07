﻿using System.Windows;
using iNKORE.UI.WPF.Modern.Common.IconKeys;

namespace MCServerLauncher.WPF.View.Components
{
    /// <summary>
    ///     ActionSettingCard.xaml 的交互逻辑
    /// </summary>
    public partial class ActionSettingCard
    {
        public ActionSettingCard()
        {
            InitializeComponent();
        }

        public string Title
        {
            get => SettingTitle.Text;
            set => SettingTitle.Text = value;
        }

        public string Description
        {
            get => SettingDescription.Text;
            set => SettingDescription.Text = value;
        }

        public bool IsAccentButtonStyle
        {
            get => SettingButton.Style == (Style)FindResource("AccentButtonStyle");
            set => SettingButton.Style = value ? (Style)FindResource("AccentButtonStyle") : null;
        }

        public FontIconData? Icon
        {
            get => SettingIcon.Icon;
            set => SettingIcon.Icon = value;
        }

        public string ButtonContent
        {
            get => SettingButton.Content.ToString();
            set => SettingButton.Content = value;
        }
    }
}
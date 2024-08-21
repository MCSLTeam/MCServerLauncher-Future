using System.Windows;
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
        /// Controls the accent color of the button.
        /// </summary>
        public bool IsAccentButtonStyle
        {
            get => SettingButton.Style == (Style)FindResource("AccentButtonStyle");
            set => SettingButton.Style = value ? (Style)FindResource("AccentButtonStyle") : null;
        }

        /// <summary>
        /// Text of the button.
        /// </summary>
        public string ButtonContent
        {
            get => SettingButton.Content.ToString();
            set => SettingButton.Content = value;
        }
    }
}
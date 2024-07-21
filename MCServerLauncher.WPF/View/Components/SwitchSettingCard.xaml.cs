using iNKORE.UI.WPF.Modern.Common.IconKeys;
using System.Windows.Controls;

namespace MCServerLauncher.WPF.View.Components
{
    /// <summary>
    /// SwitchSettingCard.xaml 的交互逻辑
    /// </summary>
    public partial class SwitchSettingCard : UserControl
    {
        public SwitchSettingCard()
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
        public FontIconData? Icon
        {
            get => SettingIcon.Icon;
            set => SettingIcon.Icon = value;
        }
        public bool Status
        {
            get => SettingSwitch.IsOn;
            set => SettingSwitch.IsOn = value;
        }
    }
}

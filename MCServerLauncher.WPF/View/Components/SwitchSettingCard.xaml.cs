using iNKORE.UI.WPF.Modern.Common.IconKeys;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

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

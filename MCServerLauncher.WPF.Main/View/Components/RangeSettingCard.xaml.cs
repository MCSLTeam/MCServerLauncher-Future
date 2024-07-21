using iNKORE.UI.WPF.Modern.Common.IconKeys;
using System.Windows.Controls;

namespace MCServerLauncher.WPF.Main.View.Components
{
    /// <summary>
    /// RangeSettingCard.xaml 的交互逻辑
    /// </summary>
    public partial class RangeSettingCard : UserControl
    {
        public RangeSettingCard()
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
        public int MinValue
        {
            get => (int)SettingSlider.Minimum;
            set => SettingSlider.Minimum = value;
        }
        public int MaxValue
        {
            get => (int)SettingSlider.Maximum;
            set => SettingSlider.Maximum = value;
        }
        public int Value
        {
            get => (int)SettingSlider.Value;
            set => SettingSlider.Value = value;
        }
        public FontIconData? Icon
        {
            get => SettingIcon.Icon;
            set => SettingIcon.Icon = value;
        }
    }
}

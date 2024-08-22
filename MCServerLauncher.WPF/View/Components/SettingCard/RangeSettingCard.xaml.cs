using System.Windows;
using iNKORE.UI.WPF.Modern.Common.IconKeys;

namespace MCServerLauncher.WPF.View.Components.SettingCard
{
    /// <summary>
    ///    RangeSettingCard.xaml 的交互逻辑
    /// </summary>
    public partial class RangeSettingCard
    {
        public static readonly DependencyProperty SliderValueProperty =
            DependencyProperty.Register("SliderValue", typeof(int), typeof(RangeSettingCard),
                new PropertyMetadata(1, OnSliderValueChanged));

        public RangeSettingCard()
        {
            InitializeComponent();
        }

        /// <summary>
        ///    Setting title.
        /// </summary>
        public string Title
        {
            get => SettingTitle.Text;
            set => SettingTitle.Text = value;
        }

        /// <summary>
        ///    Setting description.
        /// </summary>
        public string Description
        {
            get => SettingDescription.Text;
            set => SettingDescription.Text = value;
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
        ///    Minimum value of slider.
        /// </summary>
        public int MinValue
        {
            get => (int)SettingSlider.Minimum;
            set => SettingSlider.Minimum = value;
        }

        /// <summary>
        ///    Maximum value of slider.
        /// </summary>
        public int MaxValue
        {
            get => (int)SettingSlider.Maximum;
            set => SettingSlider.Maximum = value;
        }

        /// <summary>
        ///    Value of slider.
        /// </summary>
        public int SliderValue
        {
            get => (int)SettingSlider.Value;
            set => SettingSlider.Value = value;
        }

        private static void OnSliderValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not RangeSettingCard control) return;
            control.SettingSlider.Value = (int)e.NewValue;
        }
    }
}
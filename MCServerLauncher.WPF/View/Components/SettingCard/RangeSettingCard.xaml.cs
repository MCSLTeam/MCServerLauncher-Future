using iNKORE.UI.WPF.Modern.Common.IconKeys;
using System.Windows;

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
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }
        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register("Title", typeof(string), typeof(RangeSettingCard),
                new PropertyMetadata("", OnTitleChanged));

        private static void OnTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not RangeSettingCard control) return;
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
            DependencyProperty.Register("Description", typeof(string), typeof(RangeSettingCard),
                new PropertyMetadata("", OnDescriptionChanged));

        private static void OnDescriptionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not RangeSettingCard control) return;
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

        /// <summary>
        ///    Whether to show slider ticks.
        /// </summary>
        public bool ShowSliderTick
        {
            get => (bool)GetValue(ShowSliderTickProperty);
            set => SetValue(ShowSliderTickProperty, value);
        }
        public static readonly DependencyProperty ShowSliderTickProperty =
            DependencyProperty.Register("ShowSliderTick", typeof(bool), typeof(RangeSettingCard),
                new PropertyMetadata(false, OnShowSliderTickChanged));

        private static void OnShowSliderTickChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not RangeSettingCard control) return;
            if (e.NewValue is not bool showTick) return;
            control.SettingSlider.TickPlacement = showTick ? System.Windows.Controls.Primitives.TickPlacement.BottomRight : System.Windows.Controls.Primitives.TickPlacement.None;
        }

        /// <summary>
        ///    Slider tick placement.
        /// </summary>
        public System.Windows.Controls.Primitives.TickPlacement SliderTickPlacement
        {
            get => (System.Windows.Controls.Primitives.TickPlacement)GetValue(SliderTickPlacementProperty);
            set => SetValue(SliderTickPlacementProperty, value);
        }
        public static readonly DependencyProperty SliderTickPlacementProperty =
            DependencyProperty.Register("SliderTickPlacement", typeof(System.Windows.Controls.Primitives.TickPlacement), typeof(RangeSettingCard),
                new PropertyMetadata(System.Windows.Controls.Primitives.TickPlacement.None, OnSliderTickPlacementChanged));

        private static void OnSliderTickPlacementChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not RangeSettingCard control) return;
            control.SettingSlider.TickPlacement = (System.Windows.Controls.Primitives.TickPlacement)e.NewValue;
        }

        /// <summary>
        ///    Slider snaps to step values or ticks.
        /// </summary>
        public bool SliderSnapsToStepValues
        {
            get => (bool)GetValue(SliderSnapsToStepValuesProperty);
            set => SetValue(SliderSnapsToStepValuesProperty, value);
        }
        public static readonly DependencyProperty SliderSnapsToStepValuesProperty =
            DependencyProperty.Register("SliderSnapsToStepValues", typeof(bool), typeof(RangeSettingCard),
                new PropertyMetadata(true, OnSliderSnapsToStepValuesChanged));

        private static void OnSliderSnapsToStepValuesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not RangeSettingCard control) return;
            control.SettingSlider.IsSnapToTickEnabled = (bool)e.NewValue;
        }
    }
}
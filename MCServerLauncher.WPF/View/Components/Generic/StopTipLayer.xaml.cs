using iNKORE.UI.WPF.Modern.Common.IconKeys;
using System.Windows;
using System.Windows.Controls;

namespace MCServerLauncher.WPF.View.Components.Generic
{
    /// <summary>
    /// StopTipLayer.xaml 的交互逻辑
    /// </summary>
    public partial class StopTipLayer : UserControl
    {
        public StopTipLayer()
        {
            InitializeComponent();
        }

        public static readonly DependencyProperty SymbolProperty =
            DependencyProperty.Register(nameof(Symbol), typeof(string), typeof(StopTipLayer), new PropertyMetadata(default(string)));

        public string Symbol
        {
            get => (string)GetValue(SymbolProperty);
            set => SetValue(SymbolProperty, value);
        }

        public static readonly DependencyProperty StopTipProperty =
            DependencyProperty.Register(nameof(StopTip), typeof(string), typeof(StopTipLayer), new PropertyMetadata(default(string)));

        public string StopTip
        {
            get => (string)GetValue(StopTipProperty);
            set => SetValue(StopTipProperty, value);
        }

        public static readonly DependencyProperty StopDescriptionProperty =
            DependencyProperty.Register(nameof(StopDescription), typeof(string), typeof(StopTipLayer), new PropertyMetadata(default(string)));

        public string StopDescription
        {
            get => (string)GetValue(StopDescriptionProperty);
            set => SetValue(StopDescriptionProperty, value);
        }

        public static readonly DependencyProperty ButtonTextProperty =
            DependencyProperty.Register(nameof(ButtonText), typeof(string), typeof(StopTipLayer), new PropertyMetadata(default(string)));

        public string ButtonText
        {
            get => (string)GetValue(ButtonTextProperty);
            set => SetValue(ButtonTextProperty, value);
        }

        public static readonly DependencyProperty ButtonIconProperty =
            DependencyProperty.Register(nameof(ButtonIcon), typeof(FontIconData), typeof(StopTipLayer), new PropertyMetadata(default(FontIconData)));

        public FontIconData ButtonIcon
        {
            get => (FontIconData)GetValue(ButtonIconProperty);
            set => SetValue(ButtonIconProperty, value);
        }
    }
}
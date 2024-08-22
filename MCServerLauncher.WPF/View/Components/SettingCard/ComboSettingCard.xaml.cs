using System.Collections.Generic;
using System.Windows;
using iNKORE.UI.WPF.Modern.Common.IconKeys;

namespace MCServerLauncher.WPF.View.Components.SettingCard
{
    /// <summary>
    ///     ComboSettingCard.xaml 的交互逻辑
    /// </summary>
    public partial class ComboSettingCard
    {
        public ComboSettingCard()
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

        public IEnumerable<string> ComboBoxItems
        {
            get => (IEnumerable<string>)GetValue(ComboBoxItemsProperty);
            set => SetValue(ComboBoxItemsProperty, value);
        }

        public static readonly DependencyProperty ComboBoxItemsProperty = DependencyProperty.Register(
            nameof(ComboBoxItems),
            typeof(IEnumerable<string>),
            typeof(ComboSettingCard),
            new PropertyMetadata(new List<string>(), OnComboBoxItemsChanged));

        private static void OnComboBoxItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ComboSettingCard control) return;
            control.SettingComboBox.Items.Clear();
            if (e.NewValue is not IEnumerable<string> items) return;
            foreach (var item in items)
                control.SettingComboBox.Items.Add(item);
        }

        public static readonly DependencyProperty IndexProperty = DependencyProperty.Register(
            "Index",
            typeof(int),
            typeof(ComboSettingCard),
            new PropertyMetadata(0, OnIndexChanged));

        private static void OnIndexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ComboSettingCard control) return;
            if (e.NewValue is not int value) return;
            control.SettingComboBox.SelectedIndex = value;
        }
    }
}
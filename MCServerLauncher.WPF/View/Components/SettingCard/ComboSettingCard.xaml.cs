using iNKORE.UI.WPF.Modern.Common.IconKeys;
using System.Collections.Generic;
using System.Windows;

namespace MCServerLauncher.WPF.View.Components.SettingCard
{
    /// <summary>
    ///    ComboSettingCard.xaml 的交互逻辑
    /// </summary>
    public partial class ComboSettingCard
    {
        public static readonly DependencyProperty ComboBoxItemsProperty = DependencyProperty.Register(
            nameof(ComboBoxItems),
            typeof(IEnumerable<string>),
            typeof(ComboSettingCard),
            new PropertyMetadata(new List<string>(), OnComboBoxItemsChanged));

        public static readonly DependencyProperty IndexProperty = DependencyProperty.Register(
            "Index",
            typeof(int),
            typeof(ComboSettingCard),
            new PropertyMetadata(0, OnIndexChanged));

        public ComboSettingCard()
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
            DependencyProperty.Register("Title", typeof(string), typeof(ComboSettingCard),
                new PropertyMetadata("", OnTitleChanged));

        private static void OnTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ComboSettingCard control) return;
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
            DependencyProperty.Register("Description", typeof(string), typeof(ComboSettingCard),
                new PropertyMetadata("", OnDescriptionChanged));

        private static void OnDescriptionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ComboSettingCard control) return;
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

        public IEnumerable<string> ComboBoxItems
        {
            get => (IEnumerable<string>)GetValue(ComboBoxItemsProperty);
            set => SetValue(ComboBoxItemsProperty, value);
        }

        private static void OnComboBoxItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ComboSettingCard control) return;
            control.SettingComboBox.Items.Clear();
            if (e.NewValue is not IEnumerable<string> items) return;
            control.SettingComboBox.ItemsSource = items;
        }

        private static void OnIndexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ComboSettingCard control) return;
            if (e.NewValue is not int value) return;
            control.SettingComboBox.SelectedIndex = value;
        }
    }
}
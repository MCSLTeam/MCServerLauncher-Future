using iNKORE.UI.WPF.Modern.Common.IconKeys;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace MCServerLauncher.WPF.Main.View.Components
{
    /// <summary>
    /// ComboSettingCard.xaml 的交互逻辑
    /// </summary>
    public partial class ComboSettingCard : UserControl
    {
        public ComboSettingCard()
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

        public static readonly DependencyProperty ComboBoxItemsProperty = DependencyProperty.Register(
                    "ComboBoxItems",
                    typeof(IEnumerable<string>),
                    typeof(ComboSettingCard),
                    new PropertyMetadata(new List<string>(), OnComboBoxItemsChanged));

        public IEnumerable<string> ComboBoxItems
        {
            get => (IEnumerable<string>)GetValue(ComboBoxItemsProperty);
            set => SetValue(ComboBoxItemsProperty, value);
        }

        private static void OnComboBoxItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ComboSettingCard control)
            {
                control.SettingComboBox.Items.Clear();
                var items = e.NewValue as IEnumerable<string>;
                if (items != null)
                {
                    foreach (var item in items)
                    {
                        control.SettingComboBox.Items.Add(item);
                    }
                }
            }
        }
    }
}

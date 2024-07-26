using System.Windows;
using ListView = iNKORE.UI.WPF.Modern.Controls.ListView;

namespace MCServerLauncher.WPF.Main.View.Components
{
    /// <summary>
    ///     JVMArgumentItem.xaml 的交互逻辑
    /// </summary>
    public partial class JVMArgumentItem
    {
        public JVMArgumentItem()
        {
            InitializeComponent();
        }

        public string Argument
        {
            get => ArgumentTextBox.Text;
            set => ArgumentTextBox.Text = value;
        }

        private void DeleteArgument(object sender, RoutedEventArgs e)
        {
            ((ListView)Parent).Items.Remove(this);
        }
    }
}
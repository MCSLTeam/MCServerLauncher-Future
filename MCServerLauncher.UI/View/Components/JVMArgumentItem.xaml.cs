using System.Windows;
using System.Windows.Controls;
using ListView = iNKORE.UI.WPF.Modern.Controls.ListView;

namespace MCServerLauncher.UI.View.Components
{
    /// <summary>
    /// JVMArgumentItem.xaml 的交互逻辑
    /// </summary>
    

    public partial class JVMArgumentItem : UserControl
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

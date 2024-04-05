using iNKORE.UI.WPF.Modern.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Documents;
using Page = System.Windows.Controls.Page;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace MCServerLauncher.Pages
{
    /// <summary>
    /// CreateInstancePage.xaml 的交互逻辑
    /// </summary>
    public partial class CreateInstancePage : Page
    {
        public CreateInstancePage()
        {
            InitializeComponent();
        }
        public void SelectNewInstanceType(object Sender, MouseButtonEventArgs MouseArg)
        {
            SelectNewInstanceTypeContinueBtn.IsEnabled = true;
        }

        
    }
}

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
using System.Windows.Controls;

namespace MCServerLauncher.Pages
{
    /// <summary>
    /// CreateInstancePage.xaml 的交互逻辑
    /// </summary>
    public partial class CreateInstancePage : Page
    {
        private string CreatingInstanceType = "PreCreating";
        public CreateInstancePage()
        {
            InitializeComponent();
        }
        public void SelectNewInstanceType(object Sender, MouseButtonEventArgs MouseArg)
        {
            CreatingInstanceType = ((Border)Sender).Name;
            SelectNewInstanceTypeContinueBtn.IsEnabled = true;
        }
        public void GoCreateInstance(object Sender, RoutedEventArgs Arg)
        {
            if (((Button)Sender).Name.Contains("_Back"))
            {
                CreatingInstanceType = "PreCreating";
                CreateMinecraftJavaServerGrid.Visibility = Visibility.Hidden;
                PreCreateInstanceGrid.Visibility = Visibility.Visible;
                return;
            }
            else
            {
                if (CreatingInstanceType == "PreCreating")
                {
                    CreateMinecraftJavaServerGrid.Visibility = Visibility.Hidden;
                    PreCreateInstanceGrid.Visibility = Visibility.Visible;
                    return;
                }
                if (CreatingInstanceType == "MinecraftJavaServer")
                {
                    PreCreateInstanceGrid.Visibility = Visibility.Hidden;
                    CreateMinecraftJavaServerGrid.Visibility = Visibility.Visible;
                    return;
                }
            }
        }


    }
}

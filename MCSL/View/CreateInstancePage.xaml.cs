
using System.Windows;
using Page = System.Windows.Controls.Page;
using System.Windows.Input;
using System.Windows.Controls;
using System;
using iNKORE.UI.WPF.Modern.Controls;

namespace MCServerLauncher.View{
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

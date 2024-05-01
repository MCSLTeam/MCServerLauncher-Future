using static MCServerLauncher.UI.Tools.VisualTreeExtensions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Text.RegularExpressions;
using System;

namespace MCServerLauncher.UI.View.CreateInstanceProvider
{
    /// <summary>
    /// PreCreateInstance.xaml 的交互逻辑
    /// </summary>
    public partial class PreCreateInstance : UserControl
    {
        public string CreatingInstanceType = "PreCreating";
        public PreCreateInstance()
        {
            InitializeComponent();
        }
        public void SelectNewInstanceType(object Sender, MouseButtonEventArgs MouseArg)
        {
            Console.WriteLine(((Border)Sender).Name);
            CreatingInstanceType = ((Border)Sender).Name;
            Console.WriteLine(CreatingInstanceType);
            SelectNewInstanceTypeContinueBtn.IsEnabled = true;
        }
        public void GoCreateInstance(object Sender, RoutedEventArgs e)
        {
            if (CreatingInstanceType == "PreCreating")
            {
                return;
            }
            else
            {
                var parent = this.TryFindParent<CreateInstancePage>();
                switch (CreatingInstanceType)
                {
                    case "MinecraftJavaServer":
                        parent.CurrentCreateInstance.Content = parent.NewMinecraftJavaServerPage();
                        break;
                    case "MinecraftForgeServer":
                        parent.CurrentCreateInstance.Content = parent.NewMinecraftForgeServerPage();
                        break;
                    case "MinecraftBedrockServer":
                        parent.CurrentCreateInstance.Content = parent.NewMinecraftBedrockServerPage();
                        break;
                    case "OtherExecutable":
                        parent.CurrentCreateInstance.Content = parent.NewOtherExecutablePage();
                        break;
                    default:
                        break;
                }
            }
        }   
    }
}

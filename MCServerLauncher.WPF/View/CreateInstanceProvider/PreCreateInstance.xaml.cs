using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using static MCServerLauncher.WPF.Helpers.VisualTreeExtensions;
using iNKORE.UI.WPF.Modern.Media.Animation;

namespace MCServerLauncher.WPF.View.CreateInstanceProvider
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
                        parent.CurrentCreateInstance.Navigate(parent.NewMinecraftJavaServerPage(), new DrillInNavigationTransitionInfo());
                        break;
                    case "MinecraftForgeServer":
                        parent.CurrentCreateInstance.Navigate(parent.NewMinecraftForgeServerPage(), new DrillInNavigationTransitionInfo());
                        break;
                    case "MinecraftBedrockServer":
                        parent.CurrentCreateInstance.Navigate(parent.NewMinecraftBedrockServerPage(), new DrillInNavigationTransitionInfo());
                        break;
                    case "OtherExecutable":
                        parent.CurrentCreateInstance.Navigate(parent.NewOtherExecutablePage(), new DrillInNavigationTransitionInfo());
                        break;
                    default:
                        break;
                }
            }
        }
    }
}

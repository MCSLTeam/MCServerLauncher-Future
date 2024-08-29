using iNKORE.UI.WPF.Modern.Controls;
using MCServerLauncher.WPF.Modules.DownloadProvider;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace MCServerLauncher.WPF.View.Pages
{
    /// <summary>
    ///    DebugPage.xaml 的交互逻辑
    /// </summary>
    public partial class DebugPage
    {
        public DebugPage()
        {
            InitializeComponent();
        }

        /// <summary>
        ///    Show debug dialog.
        /// </summary>
        /// <param name="result">Text to show.</param>
        private static async void ShowTextResultContentDialog(string result)
        {
            ContentDialog dialog = new();
            ScrollViewerEx scroll = new();
            dialog.Title = "Result";
            dialog.PrimaryButtonText = "OK";
            dialog.DefaultButton = ContentDialogButton.Primary;
            dialog.FullSizeDesired = false;
            TextBlock textBlock = new()
            {
                TextWrapping = TextWrapping.WrapWithOverflow,
                Text = result
            };
            scroll.Content = textBlock;
            dialog.Content = scroll;
            try
            {
                await dialog.ShowAsync();
            }
            catch (Exception)
            {
                // ignored
            }
        }

        #region FastMirror

        private async void TestFastMirrorEndPoint(object sender, RoutedEventArgs e)
        {
            var results = await new FastMirror().GetCoreInfo();
            var tmpText = results.Aggregate("",
                (current, result) =>
                    current +
                    $"Name: {result.Name}\nTag: {result.Tag}\nHomePage: {result.HomePage}\nRecommend: {result.Recommend}\nMinecraftVersions: {string.Join(", ", result.MinecraftVersions)}\n\n");
            ShowTextResultContentDialog(tmpText);
        }

        private async void TestFastMirrorCore(object sender, RoutedEventArgs e)
        {
            var results = await new FastMirror().GetCoreDetail("Paper", "1.20.1");
            var tmpText = results.Aggregate("",
                (current, result) =>
                    current +
                    $"Name: {result.Name}\nMinecraftVersion: {result.MinecraftVersion}\nCoreVersion: {result.CoreVersion}\nSHA1: {result.Sha1}\n\n");
            ShowTextResultContentDialog(tmpText);
        }

        #endregion

        #region AList

        private async void TestZTsinAList(object sender, RoutedEventArgs e)
        {
            var results = await new AList().GetFileList("https://jn.sv.ztsin.cn:5244", "MCSL2/MCSLAPI/Paper");
            var tmpText = results.Aggregate("",
                (current, result) =>
                    current +
                    $"FileName: {result.FileName}\nFileSize: {result.FileSize}\nIsDirectory: {result.IsDirectory}\n\n");
            ShowTextResultContentDialog(tmpText);
        }

        private async void TestZTsinAListFile(object sender, RoutedEventArgs e)
        {
            var result = await new AList().GetFileUrl("https://jn.sv.ztsin.cn:5244",
                "MCSL2/MCSLAPI/Paper/paper-1.20.2-318.jar");
            ShowTextResultContentDialog($"RawUrl: {result}\n");
        }

        #endregion

        #region Polars

        private async void TestPolars(object sender, RoutedEventArgs e)
        {
            var results = await new PolarsMirror().GetCoreInfo();
            var tmpText = results.Aggregate("",
                (current, result) =>
                    current + $"Name: {result.Name}\nId: {result.Id}\nDescription: {result.Description}\n\n");
            ShowTextResultContentDialog(tmpText);
        }

        private async void TestPolarsCore(object sender, RoutedEventArgs e)
        {
            var results = await new PolarsMirror().GetCoreDetail(1);
            var tmpText = results.Aggregate("",
                (current, result) => current + $"Name: {result.FileName}\nDownloadUrl: {result.DownloadUrl}\n\n");
            ShowTextResultContentDialog(tmpText);
        }

        #endregion

        #region MSL

        private async void TestMSL(object sender, RoutedEventArgs e)
        {
            var results = await new MSLAPI().GetCoreInfo();
            var tmpText = results.Aggregate("", (current, result) => current + $"Name: {result}\n");
            ShowTextResultContentDialog(tmpText);
        }

        private async void TestMSLCore(object sender, RoutedEventArgs e)
        {
            var results = await new MSLAPI().GetMinecraftVersions("paper");
            var tmpText = results.Aggregate("Name: paper\n\n", (current, result) => current + $"Version: {result}\n");
            ShowTextResultContentDialog(tmpText);
        }

        private async void TestMSLDownloadUrl(object sender, RoutedEventArgs e)
        {
            var result = await new MSLAPI().GetDownloadUrl("paper", "1.21");
            ShowTextResultContentDialog($"Name: paper\nVersion:1.21\n{result}\n");
        }

        #endregion

        #region MCSLSync

        private async void TestMCSLSync(object sender, RoutedEventArgs e)
        {
            var results = await new MCSLSync().GetCoreInfo();
            var tmpText = results.Aggregate("", (current, result) => current + $"Name: {result}\n");
            ShowTextResultContentDialog(tmpText);
        }

        private async void TestMCSLSyncCore(object sender, RoutedEventArgs e)
        {
            var results = await new MCSLSync().GetMinecraftVersions("Paper");
            var tmpText = results.Aggregate("Name: Paper\n\n", (current, result) => current + $"Version: {result}\n\n");
            ShowTextResultContentDialog(tmpText);
        }

        private async void TestMCSLSyncCoreVersion(object sender, RoutedEventArgs e)
        {
            var results = await new MCSLSync().GetCoreVersions("Paper", "1.20.6");
            var tmpText = results.Aggregate("Name: Paper\nVersion: 1.20.6\n\n",
                (current, result) => current + $"Version: {result}\n\n");
            ShowTextResultContentDialog(tmpText);
        }

        private async void TestMCSLSyncCoreDetail(object sender, RoutedEventArgs e)
        {
            var result = await new MCSLSync().GetCoreDetail("Paper", "1.20.6", "build148");
            ShowTextResultContentDialog(
                $"Core: {result.Core}\nMinecraftVersion: {result.MinecraftVersion}\nCoreVersion: {result.CoreVersion}\nDownloadUrl: {result.DownloadUrl}\n");
        }

        #endregion
    }
}
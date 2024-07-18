using iNKORE.UI.WPF.Modern.Controls;
using MCServerLauncher.UI.Modules.Download;
using Page = System.Windows.Controls.Page;
using System.Windows;
using System.Collections.Generic;
using System.Windows.Controls;
using System;
using static MCServerLauncher.UI.Modules.Download.FastMirror;
using static MCServerLauncher.UI.Modules.Download.AList;
using static MCServerLauncher.UI.Modules.Download.PolarsMirror;
using static MCServerLauncher.UI.Modules.Download.MSLAPI;
using static MCServerLauncher.UI.Modules.Download.MCSLSync;

namespace MCServerLauncher.UI.View
{
    /// <summary>
    /// TestPage.xaml 的交互逻辑
    /// </summary>
    public partial class TestPage : Page
    {
        public TestPage()
        {
            InitializeComponent();
        }

        private async void ShowTextResultContentDialog(string Result)
        {
            ContentDialog dialog = new();
            ScrollViewerEx scroll = new();
            dialog.Title = "Result";
            dialog.PrimaryButtonText = "OK";
            dialog.DefaultButton = ContentDialogButton.Primary;
            dialog.FullSizeDesired = false;
            TextBlock textBlock = new();
            textBlock.TextWrapping = TextWrapping.WrapWithOverflow;
            textBlock.Text = Result;
            scroll.Content = textBlock;
            dialog.Content = scroll;
            try { await dialog.ShowAsync(); } catch (Exception) { return; }
        }
        #region FastMirror
        private async void TestFastMirrorEndPoint(object sender, RoutedEventArgs e)
        {
            List<FastMirrorCoreInfo> Results = await new FastMirror().GetCoreInfo();
            string tmpText = "";
            foreach (var Result in Results)
            {
                tmpText += $"Name: {Result.Name}\nTag: {Result.Tag}\nHomePage: {Result.HomePage}\nRecommend: {Result.Recommend}\nMinecraftVersions: {string.Join(", ", Result.MinecraftVersions)}\n\n";
            }
            ShowTextResultContentDialog(tmpText);
        }
        private async void TestFastMirrorCore(object sender, RoutedEventArgs e)
        {
            List<FastMirrorCoreDetail> Results = await new FastMirror().GetCoreDetail("Paper", "1.20.1");
            string tmpText = "";
            foreach (var Result in Results)
            {
                tmpText += $"Name: {Result.Name}\nMinecraftVersion: {Result.MinecraftVersion}\nCoreVersion: {Result.CoreVersion}\nSHA1: {Result.SHA1}\n\n";
            }
            ShowTextResultContentDialog(tmpText);
        }
        #endregion
        #region AList
        private async void TestZTsinAList(object sender, RoutedEventArgs e)
        {
            List<AListFileStructure> Results = await new AList().GetFileList("https://jn.sv.ztsin.cn:5244", "MCSL2/MCSLAPI/Paper");
            string tmpText = "";
            foreach (var Result in Results)
            {
                tmpText += $"FileName: {Result.FileName}\nFileSize: {Result.FileSize}\nIsDirectory: {Result.IsDirectory}\n\n";
            }
            ShowTextResultContentDialog(tmpText);
        }
        private async void TestZTsinAListFile(object sender, RoutedEventArgs e)
        {
            AListFileDetail Result = await new AList().GetFileUrl("https://jn.sv.ztsin.cn:5244", "MCSL2/MCSLAPI/Paper/paper-1.20.2-318.jar");
            string tmpText = $"FileName: {Result.FileName}\nFileSize: {Result.FileSize}\nIsDirectory: {Result.IsDirectory}\nRawUrl: {Result.RawUrl}\n\n";
            ShowTextResultContentDialog(tmpText);
        }
        #endregion
        #region Polars
        private async void TestPolars(object sender, RoutedEventArgs e)
        {
            List<PolarsMirrorCoreInfo> Results = await new PolarsMirror().GetCoreInfo();
            string tmpText = "";
            foreach (var Result in Results)
            {
                tmpText += $"Name: {Result.Name}\nId: {Result.Id}\nDescription: {Result.Description}\n\n";
            }
            ShowTextResultContentDialog(tmpText);
        }
        private async void TestPolarsCore(object sender, RoutedEventArgs e)
        {
            List<PolarsMirrorCoreDetail> Results = await new PolarsMirror().GetCoreDetail(1);
            string tmpText = "";
            foreach (var Result in Results)
            {
                tmpText += $"Name: {Result.FileName}\nDownloadUrl: {Result.DownloadUrl}\n\n";
            }
            ShowTextResultContentDialog(tmpText);
        }
        #endregion
        #region MSL
        private async void TestMSL(object sender, RoutedEventArgs e)
        {
            List<string> Results = await new MSLAPI().GetCoreInfo();
            string tmpText = "";
            foreach (var Result in Results)
            {
                tmpText += $"Name: {Result}\n";
            }
            ShowTextResultContentDialog(tmpText);
        }
        private async void TestMSLCore(object sender, RoutedEventArgs e)
        {
            List<string> Results = await new MSLAPI().GetMinecraftVersions("paper");
            string tmpText = "Name: paper\n\n";
            foreach (var Result in Results)
            {
                tmpText += $"Version: {Result}\n";
            }
            ShowTextResultContentDialog(tmpText);
        }
        private async void TestMSLDownloadUrl(object sender, RoutedEventArgs e)
        {
            string Result = await new MSLAPI().GetDownloadUrl("paper", "1.21");
            string tmpText = $"Name: paper\nVersion:1.21\n{Result}\n";
            ShowTextResultContentDialog(tmpText);
        }
        #endregion
        #region MCSLSync
        private async void TestMCSLSync(object sender, RoutedEventArgs e)
        {
            List<string> Results = await new MCSLSync().GetCoreInfo();
            string tmpText = "";
            foreach (var Result in Results)
            {
                tmpText += $"Name: {Result}\n";
            }
            ShowTextResultContentDialog(tmpText);
        }
        private async void TestMCSLSyncCore(object sender, RoutedEventArgs e)
        {
            List<string> Results = await new MCSLSync().GetMinecraftVersions("Paper");
            string tmpText = "Name: Paper\n\n";
            foreach (var Result in Results)
            {
                tmpText += $"Version: {Result}\n\n";
            }
            ShowTextResultContentDialog(tmpText);
        }
        private async void TestMCSLSyncCoreVersion(object sender, RoutedEventArgs e)
        {
            List<string> Results = await new MCSLSync().GetCoreVersions("Paper", "1.20.6");
            string tmpText = "Name: Paper\nVersion: 1.20.6\n\n";
            foreach (var Result in Results)
            {
                tmpText += $"Version: {Result}\n\n";
            }
            ShowTextResultContentDialog(tmpText);
        }
        private async void TestMCSLSyncCoreDetail(object sender, RoutedEventArgs e)
        {
            MCSLSyncCoreDetail Result = await new MCSLSync().GetCoreDetail("Paper", "1.20.6", "build30");
            string tmpText = $"Core: {Result.Core}\nMinecraftVersion: {Result.MinecraftVersion}\nCoreVersion: {Result.CoreVersion}\nDownloadUrl: {Result.DownloadUrl}\n";
            ShowTextResultContentDialog(tmpText);
        }
        #endregion
    }
}

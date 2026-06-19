using iNKORE.UI.WPF.Modern.Controls;
using MCServerLauncher.Common.DownloadProvider;
using MCServerLauncher.WPF.InstanceConsole.View.Dialogs;
using MCServerLauncher.WPF.Modules;
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ConsoleWindow = MCServerLauncher.WPF.InstanceConsole.Window;

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


        private void ShowConsoleWindow(object sender, RoutedEventArgs e)
        {
            new ConsoleWindow().Show();
        }

        private void ShowFirstSetup(object sender, RoutedEventArgs e)
        {
            if (Application.Current.MainWindow is MainWindow mainWindow)
            {
                mainWindow.ShowFirstSetupForDebug();
            }
        }

        private void OpenFileEditor_Log(object sender, RoutedEventArgs e) => OpenFileEditor("test.log");
        private void OpenFileEditor_Ini(object sender, RoutedEventArgs e) => OpenFileEditor("test.ini");
        private void OpenFileEditor_Yaml(object sender, RoutedEventArgs e) => OpenFileEditor("test.yaml");
        private void OpenFileEditor_Toml(object sender, RoutedEventArgs e) => OpenFileEditor("test.toml");
        private void OpenFileEditor_Bat(object sender, RoutedEventArgs e) => OpenFileEditor("test.bat");
        private void OpenFileEditor_Shell(object sender, RoutedEventArgs e) => OpenFileEditor("test.sh");
        private void OpenFileEditor_Csv(object sender, RoutedEventArgs e) => OpenFileEditor("test.csv");

        private void OpenFileEditor(string filename)
        {
            var tempPath = Path.Combine(Path.GetTempPath(), filename);
            if (!File.Exists(tempPath))
            {
                File.WriteAllText(tempPath, GetSampleContent(filename));
            }

            var window = new FileEditorWindow();
            var viewModel = new FileEditorViewModel(null!, tempPath, filename, 0, window)
            {
                IsLoading = false,
                StatusText = "Ready (Debug Mode)"
            };
            viewModel.Document.Text = File.ReadAllText(tempPath);

            window.DataContext = viewModel;
            window.Show();
        }

        private static string GetSampleContent(string filename)
        {
            var ext = Path.GetExtension(filename).ToLowerInvariant();
            return ext switch
            {
                ".log" => "[12:34:56] [main/INFO]: This is an info message\n[12:34:57] [main/WARN]: This is a warning\n[12:34:58] [main/ERROR]: This is an error\n\tat com.example.Main.main(Main.java:10)",
                ".ini" => "[Section]\nKey=Value\n# Comment\nNum=123",
                ".yaml" => "name: Test\nversion: 1.0\ndependencies:\n  - lib1\n  - lib2",
                ".toml" => "[package]\nname = \"test\"\nversion = \"0.1.0\"\n\n[dependencies]\nserde = \"1.0\"",
                ".bat" => "@echo off\nREM This is a batch file\nset VAR=Hello\necho %VAR%\nif \"%VAR%\"==\"Hello\" echo World",
                ".sh" => "#!/bin/bash\n# This is a shell script\nVAR=\"Hello\"\necho $VAR\nif [ \"$VAR\" == \"Hello\" ]; then\n  echo \"World\"\nfi",
                ".csv" => "Name,Age,City\nAlice,30,New York\nBob,25,Los Angeles\nCharlie,35,Chicago",
                _ => "Sample text"
            };
        }

        private void ShowExceptionWindow(object sender, RoutedEventArgs e)
        {
            throw new Exception("Test Exception");
        }

        private void PushSimpleNotification(object sender, RoutedEventArgs e)
        {
            var parts = sender is Button button
                ? button.Content?.ToString()?.Split('-')
                : null;

            if (parts is not { Length: >= 2 }) return;

            InfoBarSeverity infoBarSeverity = parts[0] switch
            {
                "Informational" => InfoBarSeverity.Informational,
                "Success" => InfoBarSeverity.Success,
                "Warning" => InfoBarSeverity.Warning,
                "Error" => InfoBarSeverity.Error,
                _ => InfoBarSeverity.Informational
            };

            Constants.InfoBarPosition infoBarPosition = parts[1] switch
            {
                "Top" => Constants.InfoBarPosition.Top,
                "TopRight" => Constants.InfoBarPosition.TopRight,
                "Bottom" => Constants.InfoBarPosition.Bottom,
                "BottomRight" => Constants.InfoBarPosition.BottomRight,
                _ => Constants.InfoBarPosition.Top
            };

            var random = new Random();
            var randomNumber = random.Next(100000, 999999).ToString();
            Notification.Push("Title", $"Message{randomNumber} - {infoBarPosition}", false, infoBarSeverity, infoBarPosition, 3000);
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
            var results = await FastMirror.GetCoreInfo();
            var tmpText = (results ?? []).Aggregate("",
                (current, result) =>
                    current +
                    $"Name: {result.Name}\nTag: {result.Tag}\nHomePage: {result.HomePage}\nRecommend: {result.Recommend}\nMinecraftVersions: {string.Join(", ", result.MinecraftVersions ?? [])}\n\n");
            ShowTextResultContentDialog(tmpText);
        }

        private async void TestFastMirrorCore(object sender, RoutedEventArgs e)
        {
            var results = await FastMirror.GetCoreDetail("Paper", "1.20.1");
            var tmpText = (results ?? []).Aggregate("",
                (current, result) =>
                    current +
                    $"Name: {result.Name}\nMinecraftVersion: {result.MinecraftVersion}\nCoreVersion: {result.CoreVersion}\nSHA1: {result.Sha1}\n\n");
            ShowTextResultContentDialog(tmpText);
        }

        #endregion

        #region AList

        private async void TestRianYunAList(object sender, RoutedEventArgs e)
        {
            var results = await AList.GetFileList("https://mirrors.rainyun.com", "服务端合集/Arclight");
            var tmpText = (results ?? []).Aggregate("",
                (current, result) =>
                    current +
                    $"FileName: {result.FileName}\nFileSize: {result.FileSize}\nIsDirectory: {result.IsDirectory}\n\n");
            ShowTextResultContentDialog(tmpText);
        }

        private async void TestRianYunAListFile(object sender, RoutedEventArgs e)
        {
            var result = await AList.GetFileUrl("https://mirrors.rainyun.com",
                "服务端合集/Arclight/1.21-neoforge.zip");
            ShowTextResultContentDialog($"RawUrl: {result}\n");
        }

        #endregion

        #region Polars

        private async void TestPolars(object sender, RoutedEventArgs e)
        {
            var results = await PolarsMirror.GetCoreInfo();
            var tmpText = (results ?? []).Aggregate("",
                (current, result) =>
                    current + $"Name: {result.Name}\nId: {result.Id}\nDescription: {result.Description}\n\n");
            ShowTextResultContentDialog(tmpText);
        }

        private async void TestPolarsCore(object sender, RoutedEventArgs e)
        {
            var results = await PolarsMirror.GetCoreDetail(1);
            var tmpText = (results ?? []).Aggregate("",
                (current, result) => current + $"Name: {result.FileName}\nDownloadUrl: {result.DownloadUrl}\n\n");
            ShowTextResultContentDialog(tmpText);
        }

        #endregion

        #region MSL

        private async void TestMSL(object sender, RoutedEventArgs e)
        {
            var results = await MSLAPI.GetCoreInfo();
            var tmpText = (results ?? []).Aggregate("", (current, result) => current + $"Name: {result}\n");
            ShowTextResultContentDialog(tmpText);
        }

        private async void TestMSLCore(object sender, RoutedEventArgs e)
        {
            var results = await MSLAPI.GetMinecraftVersions("paper");
            var tmpText = (results ?? []).Aggregate("Name: paper\n\n", (current, result) => current + $"Version: {result}\n");
            ShowTextResultContentDialog(tmpText);
        }

        private async void TestMSLDownloadUrl(object sender, RoutedEventArgs e)
        {
            var result = await MSLAPI.GetDownloadUrl("paper", "1.21");
            ShowTextResultContentDialog($"Name: paper\nVersion:1.21\n{result}\n");
        }

        #endregion

        #region MCSLSync

        private async void TestMCSLSync(object sender, RoutedEventArgs e)
        {
            var results = await MCSLSync.GetCoreInfo();
            var tmpText = (results ?? []).Aggregate("", (current, result) => current + $"Name: {result}\n");
            ShowTextResultContentDialog(tmpText);
        }

        private async void TestMCSLSyncCore(object sender, RoutedEventArgs e)
        {
            var results = await MCSLSync.GetMinecraftVersions("Paper");
            var tmpText = (results ?? []).Aggregate("Name: Paper\n\n", (current, result) => current + $"Version: {result}\n\n");
            ShowTextResultContentDialog(tmpText);
        }

        private async void TestMCSLSyncCoreVersion(object sender, RoutedEventArgs e)
        {
            var results = await MCSLSync.GetCoreVersions("Paper", "1.20.6");
            var tmpText = (results ?? []).Aggregate("Name: Paper\nVersion: 1.20.6\n\n",
                (current, result) => current + $"Version: {result}\n\n");
            ShowTextResultContentDialog(tmpText);
        }

        private async void TestMCSLSyncCoreDetail(object sender, RoutedEventArgs e)
        {
            var result = await MCSLSync.GetCoreDetail("Paper", "1.20.6", "build148");
            ShowTextResultContentDialog(
                $"Core: {result?.Core}\nMinecraftVersion: {result?.MinecraftVersion}\nCoreVersion: {result?.CoreVersion}\nDownloadUrl: {result?.DownloadUrl}\n");
        }

        #endregion
    }
}

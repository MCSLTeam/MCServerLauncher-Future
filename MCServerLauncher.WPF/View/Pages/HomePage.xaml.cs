using iNKORE.UI.WPF.Modern.Controls;
using MCServerLauncher.WPF.InstanceConsole.View.Dialogs;
using MCServerLauncher.WPF.Modules;
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ConsoleWindow = MCServerLauncher.WPF.InstanceConsole.Window;

namespace MCServerLauncher.WPF.View.Pages
{
    /// <summary>
    ///    HomePage.xaml 的交互逻辑
    /// </summary>
    public partial class HomePage
    {
        public HomePage()
        {
            InitializeComponent();
        }

        private void ShowConsoleWindow(object sender, RoutedEventArgs e)
        {
            new ConsoleWindow().Show();
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
            // Create a dummy file for testing
            var tempPath = Path.Combine(Path.GetTempPath(), filename);
            if (!File.Exists(tempPath))
            {
                File.WriteAllText(tempPath, GetSampleContent(filename));
            }

            var window = new FileEditorWindow();
            // We need a dummy daemon for the view model
            // Note: Daemon constructor is private, and ClientConnection is internal.
            // We cannot easily create a dummy Daemon instance here without reflection or changing visibility.
            // However, for UI testing purposes, we can pass null if the ViewModel handles it gracefully,
            // OR we can use reflection to create the instance if absolutely necessary.
            // Given the constraints and the goal of just testing the editor UI:
            
            // Let's try to use reflection to create a dummy Daemon, as the constructor is private.
            // But ClientConnection is also internal. This is getting complicated for a simple UI test.
            
            // Alternative: Modify FileEditorViewModel to accept null daemon for testing?
            // Or just pass null and hope it doesn't crash before we override the text.
            
            // The ViewModel constructor assigns _daemon. It doesn't use it immediately.
            // It uses it in ReloadFileAsync and SaveFileAsync.
            // We are overriding the text manually below, so ReloadFileAsync might not be needed if we are careful.
            // But the ViewModel constructor calls FixHighlightingColors, which is fine.
            // It sets up commands.
            
            // Ideally, we should mock IDaemon. But we don't have a mocking framework here.
            // Let's try passing null and see if it works for just viewing.
            // We need to be careful about ReloadFileAsync being called in constructor or property changes.
            // The ViewModel constructor does NOT call ReloadFileAsync. It is called by LoadFileAsync which is usually called by the window code-behind or external caller.
            // Wait, SelectedEncoding setter calls ReloadFileAsync if !_isLoading.
            // And SelectedEncoding is set in the constructor.
            
            // Let's look at the ViewModel constructor again.
            // _selectedEncoding = Encodings.FirstOrDefault...
            // It sets the backing field directly, so the property setter is NOT triggered.
            // So ReloadFileAsync is NOT called in constructor.
            
            // So passing null for daemon should be safe as long as we don't trigger save/load.
            
            var viewModel = new FileEditorViewModel(null!, tempPath, filename, 0, window);
            
            // Override the LoadFileAsync to just load local file for testing
            viewModel.Document.Text = File.ReadAllText(tempPath);
            viewModel.IsLoading = false;
            viewModel.StatusText = "Ready (Debug Mode)";

            window.DataContext = viewModel;
            window.Show();
        }

        private string GetSampleContent(string filename)
        {
            var ext = Path.GetExtension(filename).ToLower();
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
            InfoBarSeverity infoBarSeverity = ((Button)sender).Content.ToString().Split('-')[0] switch
            {
                "Informational" => InfoBarSeverity.Informational,
                "Success" => InfoBarSeverity.Success,
                "Warning" => InfoBarSeverity.Warning,
                "Error" => InfoBarSeverity.Error,
                _ => InfoBarSeverity.Informational
            };

            Constants.InfoBarPosition infoBarPosition = ((Button)sender).Content.ToString().Split('-')[1] switch
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
    }
}
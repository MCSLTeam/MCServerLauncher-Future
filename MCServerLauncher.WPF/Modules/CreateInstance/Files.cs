using System;
using System.Windows.Forms;

namespace MCServerLauncher.WPF.Modules.CreateInstance
{
    internal class Files
    {
        public static string SelectFile(string title, string filter)
        {
            var dialog = new OpenFileDialog
            {
                Title = title,
                Filter = filter
            };
            return dialog.ShowDialog() == DialogResult.OK ? dialog.FileName : null;
        }

        public static string SelectFolder(string title)
        {
            FolderBrowserDialog folderBrowserDialog = new();
            folderBrowserDialog.RootFolder = Environment.SpecialFolder.MyComputer;
            folderBrowserDialog.ShowDialog();
            return folderBrowserDialog.ShowDialog() == DialogResult.OK ? folderBrowserDialog.SelectedPath : null;
        }
    }
}

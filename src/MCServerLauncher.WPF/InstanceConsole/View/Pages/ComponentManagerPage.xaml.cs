using System.Linq;
using System.Windows;
using MCServerLauncher.WPF.InstanceConsole.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace MCServerLauncher.WPF.InstanceConsole.View.Pages
{
    public partial class ComponentManagerPage
    {
        private readonly ComponentManagerViewModel _viewModel;

        public ComponentManagerPage()
        {
            InitializeComponent();
            _viewModel = App.Services.GetRequiredService<ComponentManagerViewModel>();
            DataContext = _viewModel;
            Loaded += async (_, _) => await _viewModel.InitializeAsync();
        }

        private async void Page_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
            {
                await _viewModel.HandleDroppedFilesAsync(files);
            }
        }

        private void Page_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                bool hasJar = files?.Any(f => f.EndsWith(".jar", System.StringComparison.OrdinalIgnoreCase)) == true;
                e.Effects = hasJar ? DragDropEffects.Copy : DragDropEffects.None;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }
    }
}

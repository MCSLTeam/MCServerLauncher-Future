using MCServerLauncher.WPF.InstanceConsole.Modules;
using Serilog;
using System;
using System.Threading.Tasks;
using System.Windows;

namespace MCServerLauncher.WPF.InstanceConsole.View.Components
{
    /// <summary>
    /// Instance performance monitor component
    /// </summary>
    public partial class InstancePerformance : IInstanceBoardComponent
    {
        private bool _isLoading;
        private bool _hasError;

        public InstancePerformance()
        {
            InitializeComponent();
        }

        public bool IsLoading
        {
            get => _isLoading;
            private set => _isLoading = value;
        }

        public bool HasError
        {
            get => _hasError;
            private set => _hasError = value;
        }

        public async Task InitializeAsync()
        {
            try
            {
                IsLoading = true;
                HasError = false;

                // Subscribe to report updates
                InstanceDataManager.Instance.ReportUpdated += OnReportUpdated;

                await RefreshAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[InstancePerformance] Failed to initialize");
                HasError = true;
            }
            finally
            {
                IsLoading = false;
            }
        }

        public async Task RefreshAsync()
        {
            try
            {
                var report = InstanceDataManager.Instance.CurrentReport;
                if (report == null)
                    return;

                var perfCounter = report.PerformanceCounter;

                // Update memory
                var memoryMB = perfCounter.Memory / 1024.0 / 1024.0;
                
                Dispatcher.Invoke(() =>
                {
                    // For memory, we show current usage
                    MemoryStatusTextBlock.Text = $"{memoryMB:F2} MB";
                    // Progress bar shows relative usage (we don't have max memory info, so just show the value)
                    MemoryStatusProgressBar.Value = Math.Min(memoryMB / 10, 100); // Rough estimate
                });

                // Update CPU
                var cpuPercent = perfCounter.Cpu;
                
                Dispatcher.Invoke(() =>
                {
                    CPUStatusTextBlock.Text = $"{cpuPercent:F2} %";
                    CPUStatusProgressBar.Value = Math.Min(cpuPercent, 100);
                });

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[InstancePerformance] Failed to refresh");
                HasError = true;
            }
        }

        private async void OnReportUpdated(object? sender, Common.ProtoType.Instance.InstanceReport? report)
        {
            await RefreshAsync();
        }
    }
}

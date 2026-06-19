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

        public Task DisposeAsync()
        {
            InstanceDataManager.Instance.ReportUpdated -= OnReportUpdated;
            return Task.CompletedTask;
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
                var memoryBytes = Math.Max(0, perfCounter.Memory);
                var memoryMB = memoryBytes / 1024.0 / 1024.0;
                var memoryProgress = Math.Clamp(memoryMB / 10, 0, 100);
                
                Dispatcher.Invoke(() =>
                {
                    // For memory, we show current usage
                    MemoryStatusTextBlock.Text = $"{memoryMB:F2} MB";
                    // Progress bar shows relative usage (we don't have max memory info, so just show the value)
                    MemoryStatusProgressBar.Value = memoryProgress; // Rough estimate
                });

                // Update CPU
                var cpuPercent = NormalizeCpu(perfCounter.Cpu);
                
                Dispatcher.Invoke(() =>
                {
                    CPUStatusTextBlock.Text = $"{cpuPercent:F2} %";
                    CPUStatusProgressBar.Value = cpuPercent;
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

        private static double NormalizeCpu(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return 0;
            }

            return Math.Clamp(value, 0, 100);
        }
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Collections.Generic;
using System;

namespace MCServerLauncher.WPF.View.Components.CreateInstance
{
    /// <summary>
    /// JvmArgHelper.xaml 的交互逻辑
    /// </summary>
    public partial class JvmArgHelper : UserControl
    {
        public JvmArgHelper()
        {
            InitializeComponent();
        }

        private string templateOption = "NoTemplate";

        private void TemplateChanged(object sender, RoutedEventArgs e)
        {
            templateOption = ((RadioButton)sender).GetType().GetProperty("Name")?.GetValue(sender).ToString()!;
        }

        private string[] MemoryArg()
        {
            var memArgs = new List<string>();
            
            if (!string.IsNullOrEmpty(MinMemorySetting.Text))
            {
                memArgs.Add($"-Xms{MinMemorySetting.Text}{MemoryUnitSetting.Text}");
            }
            
            if (!string.IsNullOrEmpty(MaxMemorySetting.Text))
            {
                memArgs.Add($"-Xmx{MaxMemorySetting.Text}{MemoryUnitSetting.Text}");
            }
            
            return memArgs.ToArray();
        }

        private string[] CodecsArg()
        {
            if (string.IsNullOrEmpty(CodecsTextBox.Text))
            {
                return new string[] { };
            }
            return new string[] { $"-Dfile.encoding={CodecsTextBox.Text}" };
        }

        private string[] TemplateArg()
        {
            if (string.IsNullOrWhiteSpace(MinMemorySetting.Text)
                || string.IsNullOrWhiteSpace(MaxMemorySetting.Text))
            {
                throw new InvalidOperationException("Memory settings cannot be empty. Please set both minimum and maximum memory values.");
            }
            return templateOption switch
            {
                "BasicTemplate" => new string[] { "-XX:+AggressiveOpts" },
                "AdvancedTemplate" => new string[] { "-XX:+UseG1GC",
                                                  "-XX:+UnlockExperimentalVMOptions",
                                                  "-XX:+ParallelRefProcEnabled",
                                                  "-XX:MaxGCPauseMillis=200",
                                                  "-XX:+UnlockExperimentalVMOptions",
                                                  "-XX:+DisableExplicitGC",
                                                  "-XX:+AlwaysPreTouch",
                                                  "-XX:G1NewSizePercent=30",
                                                  "-XX:G1MaxNewSizePercent=40",
                                                  "-XX:G1HeapRegionSize=8M",
                                                  "-XX:G1ReservePercent=20",
                                                  "-XX:G1HeapWastePercent=5",
                                                  "-XX:G1MixedGCCountTarget=4",
                                                  "-XX:InitiatingHeapOccupancyPercent=15",
                                                  "-XX:G1MixedGCLiveThresholdPercent=90",
                                                  "-XX:G1RSetUpdatingPauseTimePercent=5",
                                                  "-XX:SurvivorRatio=32",
                                                  "-XX:+PerfDisableSharedMem",
                                                  "-XX:MaxTenuringThreshold=1",
                                                  "-Dusing.aikars.flags=https://mcflags.emc.gs",
                                                  "-Daikars.new.flags=true"
                },
                "NoTemplate" => new string[] { },
                _ => new string[] { }
            };
        }

        public string[] GetArgs()
        {
            var args = new List<string>();
            args.AddRange(MemoryArg());
            args.AddRange(CodecsArg());
            args.AddRange(TemplateArg());
            return args.ToArray();
        }
    }
}

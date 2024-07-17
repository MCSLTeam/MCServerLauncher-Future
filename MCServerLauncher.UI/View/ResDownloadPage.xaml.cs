using Page = System.Windows.Controls.Page;
using MCServerLauncher.UI.View.ResDownloadProvider;
using iNKORE.UI.WPF.Modern.Controls;
using IconAndText = iNKORE.UI.WPF.Modern.Controls.IconAndText;
using System;
using iNKORE.UI.WPF.Modern.Common.IconKeys;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Serilog;

namespace MCServerLauncher.UI.View
{
    /// <summary>
    /// ResDownloadPage.xaml 的交互逻辑
    /// </summary>
    public partial class ResDownloadPage : Page
    {
        public FastMirrorProvider FastMirror = new();
        public ResDownloadPage()
        {
            InitializeComponent();
            CurrentResDownloadProvider.Content = FastMirror;
            IsVisibleChanged += (sender, e) => { if (IsVisible) Refresh(); };
        }
        public async void Refresh()
        {
            await FastMirror.Refresh(SequenceMinecraftVersion);
        }
        public List<string> SequenceMinecraftVersion(List<string> OriginalList)
        {
            return OriginalList.OrderByDescending(VersionComparator).ToList();

        }
        static Func<string, (int, int, int, int)> VersionToTuple = version =>
        {
            if (!version.Contains(".") && !version.Contains("-")) {
                switch (version)
                {
                    case "horn":
                        return (1, 19, 2, 0);
                    case "GreatHorn":
                        return (1, 19, 3, 0);
                    case "Executions":
                        return (1, 19, 4, 0);
                    case "Trials":
                        return (1, 20, 1, 0);
                    case "Net":
                        return (1, 20, 2, 0);
                    case "Whisper":
                        return (1, 20, 4, 0);
                    case "general":
                        return (0, 0, 0, 0);
                    case "snapshot":
                        return (0, 0, 0, 0);
                    case "release":
                        return (0, 0, 0, 0);
                    default:
                        return (0, 0, 0, 0);
                }
            }
            if (version.Contains("-"))
            {
                version = version.ToLower().Replace("-", ".").Replace("rc", "").Replace("pre", "").Replace("snapshot", "0");
            }
            Console.WriteLine(version);
            string[] parts = version.Split('.');
            Console.WriteLine(string.Join(", ", parts));
            if (parts.Length == 2)
            {
                return (int.Parse(parts[0]), int.Parse(parts[1]), 0, 0);
            }
            else if (parts.Length == 3)
            {
                return (int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]), 0);
            }
            else
            {
                return (int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]), int.Parse(parts[3]));
            };
        };

        static Func<string, string> VersionComparator = version =>
        {
            var versionTuple = VersionToTuple(version);
            return $"{versionTuple.Item1:D3}.{versionTuple.Item2:D3}.{versionTuple.Item3:D3}";
        };
    }
}

﻿namespace MCServerLauncher.WPF.View.Components
{
    /// <summary>
    ///     ZCloudFileResCoreItem.xaml 的交互逻辑
    /// </summary>
    public partial class ZCloudFileResCoreItem
    {
        public ZCloudFileResCoreItem()
        {
            InitializeComponent();
        }

        public string CoreName
        {
            get => CoreNameReplacer.Text;
            set => CoreNameReplacer.Text = value;
        }
    }
}
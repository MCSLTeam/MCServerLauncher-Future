using iNKORE.UI.WPF.Modern.Controls;
using MCServerLauncher.WPF.View.Components.DaemonManager;
using System;
using System.Collections;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using static MCServerLauncher.WPF.Modules.Constants;

namespace MCServerLauncher.WPF.View.Components.CreateInstance
{
    /// <summary>
    ///    SetMinecraftJavaJvmArgument.xaml 的交互逻辑
    /// </summary>
    public partial class SetMinecraftJavaJvmArgument : ICreateInstanceStep
    {
        public SetMinecraftJavaJvmArgument()
        {
            InitializeComponent();

            // 为现有的项目添加事件处理程序
            foreach (var item in ArgsListView.Items.OfType<JvmArgumentItem>())
            {
                AttachTextChangedEvent(item);
            }

            // As you can see, we have to trigger it manually
            JvmArgumentItem tmpArg = new() { Argument = "111" };
            ArgsListView.Items.Add(tmpArg);
            ArgsListView.Items.Remove(tmpArg);
            
            // 初始更新状态
            UpdateIsFinishedStatus();
        }

        private void ArgsListView_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (IsDisposed) return;

            // 处理新添加的项目
            if (e.NewItems != null)
            {
                foreach (JvmArgumentItem item in e.NewItems)
                {
                    AttachTextChangedEvent(item);
                }
            }

            // 处理移除的项目
            if (e.OldItems != null)
            {
                foreach (JvmArgumentItem item in e.OldItems)
                {
                    DetachTextChangedEvent(item);
                }
            }

            // 更新 IsFinished 状态
            UpdateIsFinishedStatus();
        }

        private void AttachTextChangedEvent(JvmArgumentItem item)
        {
            // 为 JvmArgumentItem 的 ArgumentTextBox 添加文本变化事件
            var textBox = item.FindName("ArgumentTextBox") as TextBox;
            if (textBox != null)
            {
                textBox.TextChanged += ArgumentTextBox_TextChanged;
            }
        }

        private void DetachTextChangedEvent(JvmArgumentItem item)
        {
            // 移除 JvmArgumentItem 的 ArgumentTextBox 文本变化事件
            var textBox = item.FindName("ArgumentTextBox") as TextBox;
            if (textBox != null)
            {
                textBox.TextChanged -= ArgumentTextBox_TextChanged;
            }
        }

        private void ArgumentTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!IsDisposed)
            {
                UpdateIsFinishedStatus();
            }
        }
        private void UpdateIsFinishedStatus()
        {
            var args = GetAllArgs();
            // 检查是否至少有一个非空白的参数
            bool hasValidArgs = args.Any(arg => !string.IsNullOrWhiteSpace(arg));
            SetValue(IsFinishedProperty, hasValidArgs);
        }

        private bool IsDisposed { get; set; } = false;

        ~SetMinecraftJavaJvmArgument()
        {
            IsDisposed = true;
        }

        public static readonly DependencyProperty IsFinishedProperty = DependencyProperty.Register(
            nameof(IsFinished),
            typeof(bool),
            typeof(SetMinecraftJavaJvmArgument),
            new PropertyMetadata(false));


        public bool IsFinished
        {
            get => (bool)GetValue(IsFinishedProperty);
            private set => SetValue(IsFinishedProperty, value);
        }

        private void AddJvmArgument(object sender, RoutedEventArgs e)
        {
            var newItem = new JvmArgumentItem();
            ArgsListView.Items.Add(newItem);
            
            // 为新添加的项目附加事件
            AttachTextChangedEvent(newItem);
            
            // 更新状态
            UpdateIsFinishedStatus();
        }

        private void AddJvmArguments(string[] arg)
        {
            if (arg == null) return;
            var itemsToRemove = ArgsListView.Items.OfType<JvmArgumentItem>()
                .Where(item => string.IsNullOrEmpty(item.Argument))
                .ToList();
            foreach (var item in itemsToRemove)
            {
                DetachTextChangedEvent(item);
                ArgsListView.Items.Remove(item);
            }
            foreach (string argument in arg)
            {
                if (!string.IsNullOrWhiteSpace(argument))
                {
                    var newItem = new JvmArgumentItem { Argument = argument };
                    ArgsListView.Items.Add(newItem);
                    AttachTextChangedEvent(newItem);
                }
            }
            
            UpdateIsFinishedStatus();
        }

        // 添加一个公共方法供 JvmArgumentItem 在删除时调用
        public void OnItemRemoved(JvmArgumentItem item)
        {
            DetachTextChangedEvent(item);
            UpdateIsFinishedStatus();
        }

        private string[] GetAllArgs()
        {
            var args = new string[ArgsListView.Items.Count];
            for (var i = 0; i < ArgsListView.Items.Count; i++)
            {
                var item = (JvmArgumentItem)ArgsListView.Items[i];
                if (!string.IsNullOrWhiteSpace(item.Argument)) args[i] = item.Argument;
            }
            return args;
        }
        public CreateInstanceData ActualData
        {
            get => new()
            {
                Type = CreateInstanceDataType.List,
                Data = GetAllArgs(),
            };
        }
        private async void ShowArgHelper(object sender, RoutedEventArgs e)
        {
            (ContentDialog dialog, JvmArgHelper argHelper) = await Utils.ConstructJvmArgHelperDialog();
            dialog.PrimaryButtonClick += (o, args) => AddJvmArguments(argHelper.GetArgs());
            try
            {
                await dialog.ShowAsync();
            }
            catch (Exception)
            {
                // ignored
            }
        }
    }
}
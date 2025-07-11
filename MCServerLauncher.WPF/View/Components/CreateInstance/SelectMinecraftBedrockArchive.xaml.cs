using System.Windows;
using static MCServerLauncher.WPF.Modules.Constants;
using System.Threading.Tasks;
using System.Windows.Controls;
using MCServerLauncher.WPF.Modules;

namespace MCServerLauncher.WPF.View.Components.CreateInstance
{
    /// <summary>
    ///    SelectMinecraftBedrockArchive.xaml 的交互逻辑
    /// </summary>
    public partial class SelectMinecraftBedrockArchive: ICreateInstanceStep
    {
        public SelectMinecraftBedrockArchive()
        {
            InitializeComponent();

            void initialHandler(object sender, TextChangedEventArgs args)
            {
                if (!IsDisposed)
                {
                    SetValue(IsFinishedProperty, !string.IsNullOrWhiteSpace(ArchiveTextBox.Text));
                }
            }

            ArchiveTextBox.TextChanged += initialHandler;

            // As you can see, we have to trigger it manually
            VisualTreeHelper.InitStepState(ArchiveTextBox);
        }

        private bool IsDisposed { get; set; } = false;

        ~SelectMinecraftBedrockArchive()
        {
            IsDisposed = true;
        }

        public static readonly DependencyProperty IsFinishedProperty = DependencyProperty.Register(
            nameof(IsFinished),
            typeof(bool),
            typeof(SelectMinecraftBedrockArchive),
            new PropertyMetadata(false, OnStatusChanged));

        private static void OnStatusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not SelectMinecraftBedrockArchive control) return;
            if (e.NewValue is not bool status) return;
            control.StatusShow.Visibility = status switch
            {
                true => Visibility.Visible,
                false => Visibility.Hidden,
            };
        }

        public bool IsFinished
        {
            get => (bool)GetValue(IsFinishedProperty);
            private set => SetValue(IsFinishedProperty, value);
        }

        public CreateInstanceData ActualData
        {
            get => new()
            {
                Type = CreateInstanceDataType.Path,
                Data = ArchiveTextBox.Text,
            };
        }
    }
}
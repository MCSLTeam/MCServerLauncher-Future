using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using static MCServerLauncher.WPF.Modules.Constants;

namespace MCServerLauncher.WPF.View.Components.CreateInstance
{
    /// <summary>
    ///    SelectMinecraftJavaCore.xaml 的交互逻辑
    /// </summary>
    public partial class SelectMinecraftJavaCore : ICreateInstanceStep
    {
        public SelectMinecraftJavaCore()
        {
            InitializeComponent();

            void initialHandler(object sender, TextChangedEventArgs args)
            {
                if (!IsDisposed)
                {
                    SetValue(IsFinishedProperty, !string.IsNullOrWhiteSpace(CoreTextBox.Text));
                }
            }

            CoreTextBox.TextChanged += initialHandler;

            // As you can see, we have to trigger it manually
            CoreTextBox.Text = "1";
            CoreTextBox.Text = string.Empty;

            CoreTextBox.TextChanged -= initialHandler;

            CoreTextBox.TextChanged += async (sender, args) =>
            {
                await Task.Delay(80);
                if (!IsDisposed)
                {
                    SetValue(IsFinishedProperty, !string.IsNullOrWhiteSpace(CoreTextBox.Text));
                }
            };
        }

        private bool IsDisposed { get; set; } = false;

        ~SelectMinecraftJavaCore()
        {
            IsDisposed = true;
        }

        public static readonly DependencyProperty IsFinishedProperty = DependencyProperty.Register(
            nameof(IsFinished),
            typeof(bool),
            typeof(SelectMinecraftJavaCore),
            new PropertyMetadata(false, OnStatusChanged));

        private static void OnStatusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not SelectMinecraftJavaCore control) return;
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
                Data = CoreTextBox.Text,
            };
        }
    }
}
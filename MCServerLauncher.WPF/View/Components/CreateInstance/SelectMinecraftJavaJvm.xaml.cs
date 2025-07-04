using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using static MCServerLauncher.WPF.Modules.Constants;

namespace MCServerLauncher.WPF.View.Components.CreateInstance
{
    /// <summary>
    ///    SelectMinecraftJavaJvm.xaml 的交互逻辑
    /// </summary>
    public partial class SelectMinecraftJavaJvm : ICreateInstanceStep
    {
        public SelectMinecraftJavaJvm()
        {
            InitializeComponent();

            void initialHandler(object sender, TextChangedEventArgs args)
            {
                if (!IsDisposed)
                {
                    SetValue(IsFinishedProperty, !string.IsNullOrWhiteSpace(JavaRuntimeTextBox.Text));
                }
            }

            JavaRuntimeTextBox.TextChanged += initialHandler;

            // As you can see, we have to trigger it manually
            JavaRuntimeTextBox.Text = "1";
            JavaRuntimeTextBox.Text = string.Empty;

            JavaRuntimeTextBox.TextChanged -= initialHandler;

            JavaRuntimeTextBox.TextChanged += async (sender, args) =>
            {
                await Task.Delay(80);
                if (!IsDisposed)
                {
                    SetValue(IsFinishedProperty, !string.IsNullOrWhiteSpace(JavaRuntimeTextBox.Text));
                }
            };
        }

        private bool IsDisposed { get; set; } = false;

        ~SelectMinecraftJavaJvm()
        {
            IsDisposed = true;
        }

        public static readonly DependencyProperty IsFinishedProperty = DependencyProperty.Register(
            nameof(IsFinished),
            typeof(bool),
            typeof(SelectMinecraftJavaJvm),
            new PropertyMetadata(false, OnStatusChanged));

        private static void OnStatusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not SelectMinecraftJavaJvm control) return;
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
                Data = JavaRuntimeTextBox.Text,
            };
        }
    }
}
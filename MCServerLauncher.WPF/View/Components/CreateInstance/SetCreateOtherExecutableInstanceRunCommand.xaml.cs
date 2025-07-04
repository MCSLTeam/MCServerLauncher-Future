using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using static MCServerLauncher.WPF.Modules.Constants;

namespace MCServerLauncher.WPF.View.Components.CreateInstance
{
    /// <summary>
    ///    SetCreateOtherExecutableInstanceRunCommand.xaml 的交互逻辑
    /// </summary>
    public partial class SetCreateOtherExecutableInstanceRunCommand : ICreateInstanceStep
    {
        public SetCreateOtherExecutableInstanceRunCommand()
        {
            InitializeComponent();

            void initialHandler(object sender, TextChangedEventArgs args)
            {
                if (!IsDisposed)
                {
                    SetValue(IsFinishedProperty, !string.IsNullOrWhiteSpace(RunCommandTextBox.Text));
                }
            }

            RunCommandTextBox.TextChanged += initialHandler;

            // As you can see, we have to trigger it manually
            RunCommandTextBox.Text = "1";
            RunCommandTextBox.Text = string.Empty;

            RunCommandTextBox.TextChanged -= initialHandler;

            RunCommandTextBox.TextChanged += async (sender, args) =>
            {
                await Task.Delay(80);
                if (!IsDisposed)
                {
                    SetValue(IsFinishedProperty, !string.IsNullOrWhiteSpace(RunCommandTextBox.Text));
                }
            };
        }

        private bool IsDisposed { get; set; } = false;

        ~SetCreateOtherExecutableInstanceRunCommand()
        {
            IsDisposed = true;
        }

        public static readonly DependencyProperty IsFinishedProperty = DependencyProperty.Register(
            nameof(IsFinished),
            typeof(bool),
            typeof(SetCreateOtherExecutableInstanceRunCommand),
            new PropertyMetadata(false, OnStatusChanged));

        private static void OnStatusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not SetCreateOtherExecutableInstanceRunCommand control) return;
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
                Type = CreateInstanceDataType.CommandLine,
                Data = RunCommandTextBox.Text,
            };
        }
    }
}
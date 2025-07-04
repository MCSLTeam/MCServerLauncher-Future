using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using static MCServerLauncher.WPF.Modules.Constants;

namespace MCServerLauncher.WPF.View.Components.CreateInstance
{
    /// <summary>
    ///    SelectOtherExecutableInstanceDependency.xaml 的交互逻辑
    /// </summary>
    public partial class SelectOtherExecutableInstanceDependency : ICreateInstanceStep
    {
        public SelectOtherExecutableInstanceDependency()
        {
            InitializeComponent();

            void initialHandler(object sender, TextChangedEventArgs args)
            {
                if (!IsDisposed)
                {
                    SetValue(IsFinishedProperty, !string.IsNullOrWhiteSpace(DepsTextBox.Text));
                }
            }

            DepsTextBox.TextChanged += initialHandler;

            // As you can see, we have to trigger it manually
            DepsTextBox.Text = "1";
            DepsTextBox.Text = string.Empty;

            DepsTextBox.TextChanged -= initialHandler;

            DepsTextBox.TextChanged += async (sender, args) =>
            {
                await Task.Delay(80);
                if (!IsDisposed)
                {
                    SetValue(IsFinishedProperty, !string.IsNullOrWhiteSpace(DepsTextBox.Text));
                }
            };
        }

        private bool IsDisposed { get; set; } = false;

        ~SelectOtherExecutableInstanceDependency()
        {
            IsDisposed = true;
        }

        public static readonly DependencyProperty IsFinishedProperty = DependencyProperty.Register(
            nameof(IsFinished),
            typeof(bool),
            typeof(SelectOtherExecutableInstanceDependency),
            new PropertyMetadata(false, OnStatusChanged));

        private static void OnStatusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not SelectOtherExecutableInstanceDependency control) return;
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
                Data = DepsTextBox.Text,
            };
        }
    }
}
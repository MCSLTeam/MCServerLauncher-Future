using MCServerLauncher.WPF.Modules;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using static MCServerLauncher.WPF.Modules.Constants;

namespace MCServerLauncher.WPF.View.Components.CreateInstance
{
    /// <summary>
    ///    SelectTerrariaArchive.xaml 的交互逻辑
    /// </summary>
    public partial class SelectTerrariaArchive : ICreateInstanceStep
    {
        public SelectTerrariaArchive()
        {
            InitializeComponent();

            void initialHandler(object sender, TextChangedEventArgs args)
            {
                if (!IsDisposed)
                {
                    SetValue(IsFinishedProperty, !string.IsNullOrWhiteSpace(TerrariaExeTextBox.Text));
                }
            }

            TerrariaExeTextBox.TextChanged += initialHandler;

            // As you can see, we have to trigger it manually
            VisualTreeHelper.InitStepState(TerrariaExeTextBox);
        }

        private bool IsDisposed { get; set; } = false;

        ~SelectTerrariaArchive()
        {
            IsDisposed = true;
        }

        public static readonly DependencyProperty IsFinishedProperty = DependencyProperty.Register(
            nameof(IsFinished),
            typeof(bool),
            typeof(SelectTerrariaArchive),
            new PropertyMetadata(false, OnStatusChanged));

        private static void OnStatusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not SelectTerrariaArchive control) return;
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
                Data = TerrariaExeTextBox.Text,
            };
        }
    }
}
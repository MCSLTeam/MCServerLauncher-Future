using MCServerLauncher.Common.ProtoType.Instance;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MCServerLauncher.WPF.View.Components.InstanceManager
{
    /// <summary>
    ///    InstanceCard.xaml 的交互逻辑
    /// </summary>
    public partial class InstanceCard : INotifyPropertyChanged
    {
        private Guid _instanceId;
        private string _instanceName;
        private string _instanceType;
        private string _mcVersion;
        private InstanceStatus _status;
        private int _playerCount;
        private double _cpuUsage;
        private long _memoryUsage;

        public InstanceCard()
        {
            InitializeComponent();
            DataContext = this;
        }

        public Guid InstanceId
        {
            get => _instanceId;
            set
            {
                if (_instanceId != value)
                {
                    _instanceId = value;
                    OnPropertyChanged();
                }
            }
        }

        public string InstanceName
        {
            get => _instanceName;
            set
            {
                if (_instanceName != value)
                {
                    _instanceName = value;
                    InstanceNameTextBlock.Text = value;
                    OnPropertyChanged();
                }
            }
        }

        public string InstanceType
        {
            get => _instanceType;
            set
            {
                if (_instanceType != value)
                {
                    _instanceType = value;
                    InstanceTypeTextBlock.Text = value;
                    OnPropertyChanged();
                }
            }
        }

        public string McVersion
        {
            get => _mcVersion;
            set
            {
                if (_mcVersion != value)
                {
                    _mcVersion = value;
                    OnPropertyChanged();
                }
            }
        }

        public InstanceStatus Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StatusText));
                }
            }
        }

        public string StatusText => Status.ToString();

        public int PlayerCount
        {
            get => _playerCount;
            set
            {
                if (_playerCount != value)
                {
                    _playerCount = value;
                    OnPropertyChanged();
                }
            }
        }

        public double CpuUsage
        {
            get => _cpuUsage;
            set
            {
                if (Math.Abs(_cpuUsage - value) > 0.01)
                {
                    _cpuUsage = value;
                    OnPropertyChanged();
                }
            }
        }

        public long MemoryUsage
        {
            get => _memoryUsage;
            set
            {
                if (_memoryUsage != value)
                {
                    _memoryUsage = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
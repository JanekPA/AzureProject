using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IIoTSim.Desktop.Model;
using IIoTSim.Desktop.Model.Enums;
using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;

namespace IIoTSim.Desktop.ViewModel
{
    public partial class MainWindowViewModel : ObservableObject
    {
        private readonly CancellationTokenSource cancellationTokenSource = new();
        private bool simulationStopped = false;
        private int deviceCount = 0;

        [NotifyCanExecuteChangedFor(nameof(StartProductionCommand), nameof(StopProductionCommand), nameof(RemoveSelectedCommand), nameof(IncreaseProductionRateCommand), nameof(DecreaseProductionRateCommand))]
        [ObservableProperty]
        private DeviceModel? selectedDevice;

        private bool serverStopped = false;

        public ObservableCollection<DeviceModel> Devices { get; }

        #region Commands

        public RelayCommand StartProductionCommand { get; }

        public RelayCommand StopProductionCommand { get; }

        public RelayCommand NewDeviceCommand { get; }

        public RelayCommand RemoveSelectedCommand { get; }

        public RelayCommand IncreaseProductionRateCommand { get; }

        public RelayCommand DecreaseProductionRateCommand { get; }

        #endregion Commands

        public MainWindowViewModel()
        {
            Devices = new ObservableCollection<DeviceModel>();

            StartProductionCommand = new RelayCommand(StartProduction, () => this.SelectedDevice != null);
            StopProductionCommand = new RelayCommand(StopProduction, () => this.SelectedDevice != null);
            RemoveSelectedCommand = new RelayCommand(RemoveSelected, () => this.SelectedDevice != null);
            NewDeviceCommand = new RelayCommand(NewDevice);
            IncreaseProductionRateCommand = new RelayCommand(IncreaseProduction, () => SelectedDevice != null);
            DecreaseProductionRateCommand = new RelayCommand(DecreaseProduction, () => SelectedDevice != null);

            var engine = new SimulationEngine(Devices);
            engine.RunAsync(cancellationTokenSource.Token).ContinueWith(
                (s) => simulationStopped = true);

            var server = new SimulationServer(Devices);
            server.RunAsync(cancellationTokenSource.Token).ContinueWith(
                (s) => serverStopped = true);
        }

        #region Commands implementation

        public void StartProduction()
        {
            if (SelectedDevice != null)
            {
                SelectedDevice.ProductionStatus = ProductionStatus.Running;
                SelectedDevice.WorkorderId = Guid.NewGuid();
            }
        }

        public void StopProduction()
        {
            if (SelectedDevice != null)
            {
                SelectedDevice.ProductionStatus = ProductionStatus.Stopped;
                SelectedDevice.WorkorderId = Guid.Empty;
            }
        }

        public void NewDevice()
        {
            var device = new DeviceModel($"Device {++deviceCount}");
            Devices.Add(device);
        }

        public void RemoveSelected()
        {
            if (SelectedDevice != null)
            {
                Devices.Remove(SelectedDevice);
            }
        }

        public void IncreaseProduction()
        {
            if (SelectedDevice != null && SelectedDevice.ProductionRate < 100)
            {
                SelectedDevice.ProductionRate += 10;
            }
        }

        public void DecreaseProduction()
        {
            if (SelectedDevice != null && SelectedDevice.ProductionRate >= 10)
            {
                SelectedDevice.ProductionRate -= 10;
            }
        }

        #endregion Commands implementation

        public async void WindowClosing()
        {
            cancellationTokenSource.Cancel();
            await Task.Run(() =>
            {
                while (!simulationStopped || !serverStopped)
                {
                }
            });
        }
    }
}
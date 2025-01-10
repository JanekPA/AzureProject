using CommunityToolkit.Mvvm.ComponentModel;
using IIoTSim.Desktop.Model.Enums;
using System;

namespace IIoTSim.Desktop.Model
{
    public partial class DeviceModel : ObservableObject
    {
        [ObservableProperty]
        private string name;

        [ObservableProperty]
        private ProductionStatus productionStatus;

        [ObservableProperty]
        private Guid workorderId;

        [ObservableProperty]
        private int productionRate;

        [ObservableProperty]
        private long goodCount;

        [ObservableProperty]
        private long badCount;

        [ObservableProperty]
        private double temperature;

        [NotifyPropertyChangedFor(nameof(IsEmergencyStop), nameof(IsPowerFailure), nameof(IsSensorFailure), nameof(IsUnknownError))]
        [ObservableProperty]
        private DeviceError deviceError;

        public DeviceModel(string name)
        {
            this.name = name;
            ProductionRate = 0;
            WorkorderId = Guid.Empty;
        }

        #region IIndustrialDevice

        public void EmergencyStop()
        {
            ProductionStatus = ProductionStatus.Stopped;
            DeviceError = DeviceError.EmergencyStop;
        }

        public void ResetErrorStatus()
        {
            DeviceError = DeviceError.None;
        }

        #endregion IIndustrialDevice

        public bool IsEmergencyStop
        {
            get => DeviceError.HasFlag(DeviceError.EmergencyStop);
            set
            {
                if (value)
                {
                    DeviceError |= DeviceError.EmergencyStop;
                }
                else
                {
                    DeviceError &= ~DeviceError.EmergencyStop;
                }
            }
        }

        public bool IsPowerFailure
        {
            get => DeviceError.HasFlag(DeviceError.PowerFailure);
            set
            {
                if (value)
                {
                    DeviceError |= DeviceError.PowerFailure;
                }
                else
                {
                    DeviceError &= ~DeviceError.PowerFailure;
                }
            }
        }

        public bool IsSensorFailure
        {
            get => DeviceError.HasFlag(DeviceError.SensorFailure);
            set
            {
                if (value)
                {
                    DeviceError |= DeviceError.SensorFailure;
                }
                else
                {
                    DeviceError &= ~DeviceError.SensorFailure;
                }
            }
        }

        public bool IsUnknownError
        {
            get => DeviceError.HasFlag(DeviceError.Unknown);
            set
            {
                if (value)
                {
                    DeviceError |= DeviceError.Unknown;
                }
                else
                {
                    DeviceError &= ~DeviceError.Unknown;
                }
            }
        }

        public override string ToString()
        {
            return Name;
        }
    }
}
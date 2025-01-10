namespace IIoTSim.Desktop.Model.Enums
{
    [Flags]
    public enum DeviceError
    {
        None = 0,
        EmergencyStop = 1,
        PowerFailure = 2,
        SensorFailure = 4,
        Unknown = 8
    }
}
using IIoTSim.Desktop.Model;
using Opc.UaFx;
using Opc.UaFx.Server;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IIoTSim.Desktop
{
    internal class SimulationServer : IDisposable
    {
        private readonly object _lock = new object();

        private const string dictSeparator = ":::";

        private readonly ObservableCollection<DeviceModel> devices;
        private List<OpcFolderNode> folderNodes = new();
        private OpcServer server;

        private readonly Dictionary<string, OpcDataVariableNode> nodeIds = new();

        public SimulationServer(ObservableCollection<DeviceModel> devices)
        {
            this.devices = devices;
            Initialize();

            devices.CollectionChanged += Devices_CollectionChanged;
        }

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                lock (_lock)
                {
                    var tmpDevices = new List<DeviceModel>(devices);
                    foreach (var device in tmpDevices)
                    {
                        var productionStatusNode = (OpcDataVariableNode<int>)nodeIds[$"{device.Name}{dictSeparator}{nameof(device.ProductionStatus)}"];
                        if (productionStatusNode.Value != (int)device.ProductionStatus)
                        {
                            productionStatusNode.Value = (int)device.ProductionStatus;
                            productionStatusNode.Status.Update(OpcStatusCode.Good);
                            productionStatusNode.Timestamp = DateTime.UtcNow;
                            productionStatusNode.ApplyChanges(server.SystemContext);
                        }

                        var workorderIdNode = (OpcDataVariableNode<string>)nodeIds[$"{device.Name}{dictSeparator}{nameof(device.WorkorderId)}"];
                        if (workorderIdNode.Value != device.WorkorderId.ToString())
                        {
                            workorderIdNode.Value = device.WorkorderId.ToString();
                            workorderIdNode.Status.Update(OpcStatusCode.Good);
                            workorderIdNode.Timestamp = DateTime.UtcNow;
                            workorderIdNode.ApplyChanges(server.SystemContext);
                        }

                        var productionRateNode = (OpcDataVariableNode<int>)nodeIds[$"{device.Name}{dictSeparator}{nameof(device.ProductionRate)}"];
                        if (productionRateNode.Value != device.ProductionRate)
                        {
                            productionRateNode.Value = device.ProductionRate;
                            productionRateNode.Status.Update(OpcStatusCode.Good);
                            productionRateNode.Timestamp = DateTime.UtcNow;
                            productionRateNode.ApplyChanges(server.SystemContext);
                        }

                        var goodCountNode = (OpcDataVariableNode<long>)nodeIds[$"{device.Name}{dictSeparator}{nameof(device.GoodCount)}"];
                        if (goodCountNode.Value != device.GoodCount)
                        {
                            goodCountNode.Value = device.GoodCount;
                            goodCountNode.Status.Update(OpcStatusCode.Good);
                            goodCountNode.Timestamp = DateTime.UtcNow;
                            goodCountNode.ApplyChanges(server.SystemContext);
                        }

                        var badCountNode = (OpcDataVariableNode<long>)nodeIds[$"{device.Name}{dictSeparator}{nameof(device.BadCount)}"];
                        if (badCountNode.Value != device.BadCount)
                        {
                            badCountNode.Value = device.BadCount;
                            badCountNode.Status.Update(OpcStatusCode.Good);
                            badCountNode.Timestamp = DateTime.UtcNow;
                            badCountNode.ApplyChanges(server.SystemContext);
                        }

                        var temperatureNode = (OpcDataVariableNode<double>)nodeIds[$"{device.Name}{dictSeparator}{nameof(device.Temperature)}"];
                        if (temperatureNode.Value != device.Temperature)
                        {
                            temperatureNode.Value = device.Temperature;
                            temperatureNode.Status.Update(OpcStatusCode.Good);
                            temperatureNode.Timestamp = DateTime.UtcNow;
                            temperatureNode.ApplyChanges(server.SystemContext);
                        }

                        var deviceErrorNode = (OpcDataVariableNode<int>)nodeIds[$"{device.Name}{dictSeparator}{nameof(device.DeviceError)}"];
                        if (deviceErrorNode.Value != (int)device.DeviceError)
                        {
                            deviceErrorNode.Value = (int)device.DeviceError;
                            deviceErrorNode.Status.Update(OpcStatusCode.Good);
                            deviceErrorNode.Timestamp = DateTime.UtcNow;
                            deviceErrorNode.ApplyChanges(server.SystemContext);
                        }
                    }
                }
                await Task.Delay(1000, cancellationToken);

            }
        }

        private void Devices_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (DeviceModel newDevice in e.NewItems)
                {
                    Console.WriteLine($"New device added: {newDevice.Name}");
                    var machineNode = AddDevice(newDevice);
                    folderNodes.Add(machineNode);
                }
            }
            Initialize();
        }

        private void Initialize()
        {
            lock (_lock)
            {
                server?.Stop();
                server?.Dispose();

                nodeIds.Clear();
                folderNodes.Clear();

                var tmpDevices = new List<DeviceModel>(devices);

                foreach (var device in tmpDevices)
                {
                    var machineNode = AddDevice(device);
                    folderNodes.Add(machineNode);
                }

                server = new OpcServer("opc.tcp://localhost:4840/", folderNodes);
                server.Start();
            }
        }

        public OpcFolderNode AddDevice(DeviceModel device)
        {
            var machineNode = new OpcFolderNode(device.Name);

            var productionStatusNode = new OpcDataVariableNode<int>(machineNode, nameof(device.ProductionStatus));
            nodeIds.Add($"{device.Name}{dictSeparator}{nameof(device.ProductionStatus)}", productionStatusNode);

            var workorderIdNode = new OpcDataVariableNode<string>(machineNode, nameof(device.WorkorderId));
            nodeIds.Add($"{device.Name}{dictSeparator}{nameof(device.WorkorderId)}", workorderIdNode);

            var productionRateNode = new OpcDataVariableNode<int>(machineNode, nameof(device.ProductionRate));
            nodeIds.Add($"{device.Name}{dictSeparator}{nameof(device.ProductionRate)}", productionRateNode);
            productionRateNode.WriteVariableValueCallback = HandleWriteProductionRateValue;

            var goodCountNode = new OpcDataVariableNode<long>(machineNode, nameof(device.GoodCount));
            nodeIds.Add($"{device.Name}{dictSeparator}{nameof(device.GoodCount)}", goodCountNode);

            var badCountNode = new OpcDataVariableNode<long>(machineNode, nameof(device.BadCount));
            nodeIds.Add($"{device.Name}{dictSeparator}{nameof(device.BadCount)}", badCountNode);

            var temperatureNode = new OpcDataVariableNode<double>(machineNode, nameof(device.Temperature));
            nodeIds.Add($"{device.Name}{dictSeparator}{nameof(device.Temperature)}", temperatureNode);

            var deviceErrorNode = new OpcDataVariableNode<int>(machineNode, nameof(device.DeviceError));
            nodeIds.Add($"{device.Name}{dictSeparator}{nameof(device.DeviceError)}", deviceErrorNode);

            var emergencyStopMethodNode = new OpcMethodNode(machineNode, nameof(device.EmergencyStop), new Action(device.EmergencyStop));
            var resetErrorMethodNode = new OpcMethodNode(machineNode, nameof(device.ResetErrorStatus), new Action(device.ResetErrorStatus));
            
            return machineNode;
        }

        private OpcVariableValue<object> HandleWriteProductionRateValue(
        OpcWriteVariableValueContext context,
        OpcVariableValue<object> value)
        {
            var productionRateNode = (OpcDataVariableNode<int>)nodeIds[$"{context.Node.Parent.Name.Value}{dictSeparator}ProductionRate"];
            var deviceName = context.Node.Parent.Name.Value;
            var device = devices.Where(d => d.Name == deviceName).Single();

            int newValue = (int)value.Value;
            if (newValue > 100) newValue = 100;
            if (newValue < 0) newValue = 0;
            device.ProductionRate = newValue;
            Console.WriteLine($"Device {device.Name}: ProductionStatus={device.ProductionStatus}, GoodCount={device.GoodCount}, BadCount={device.BadCount}, Temperature={device.Temperature}");
            return new OpcVariableValue<object>(newValue, value.Timestamp ?? DateTime.MinValue, value.Status);
        }

        #region IDisposable

        private bool disposedValue;

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    server?.Dispose();
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                disposedValue = true;
            }
        }

        // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
        // ~SimulationServer()
        // {
        //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        //     Dispose(disposing: false);
        // }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        #endregion IDisposable
    }
}
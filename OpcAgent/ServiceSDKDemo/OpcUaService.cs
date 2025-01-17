// OpcUaService.cs
using Opc.UaFx;
using Opc.UaFx.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using IoTAgent.Services;
using Microsoft.Azure.Devices;

namespace IoTAgent.Services
{
    public class OpcUaService
    {
        private readonly OpcClient _client;
        private bool _isConnected = false; // Flaga monitorująca stan połączenia
        private Dictionary<int, int> _lastDeviceErrors = new();
        
        public OpcUaService(string endpoint)
        {
            _client = new OpcClient(endpoint);
        }
        public OpcValue ReadNode(string nodeId)
        {
            return _client.ReadNode(nodeId);
        }
        public void Connect()
        {
            if (!_isConnected)
            {
                try
                {
                    Console.WriteLine("Connecting to OPC UA server...");
                    _client.Connect();
                    _isConnected = true;
                    Console.WriteLine("Connected to OPC UA server.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error connecting to OPC UA server: {ex.Message}");
                    _isConnected = false;
                }
            }
        }
        public async Task CheckAndUpdateDeviceErrorsAsync(int deviceId, Func<int, Task> updateReportedTwinAsync)
        {
            try
            {
                var currentErrors = (int)_client.ReadNode($"ns=2;s=Device {deviceId}/DeviceError").Value;

                if (!_lastDeviceErrors.TryGetValue(deviceId, out var lastErrors) || currentErrors != lastErrors)
                {
                    _lastDeviceErrors[deviceId] = currentErrors;
                    Console.WriteLine($"Device {deviceId} errors changed: {AnalyzeErrors(currentErrors)}");

                    // Aktualizacja Reported Device Twin
                    await updateReportedTwinAsync(currentErrors);

                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating device errors for Device {deviceId}: {ex.Message}");
            }
        }
        public Program.DeviceState ReadDeviceState(int deviceId)
        {
            try
            {
                var deviceErrors = (int)_client.ReadNode($"ns=2;s=Device {deviceId}/DeviceError").Value;
                var productionRate = (int)_client.ReadNode($"ns=2;s=Device {deviceId}/ProductionRate").Value;
                var productionStatusNode = _client.ReadNode($"ns=2;s=Device {deviceId}/ProductionStatus");
                bool isOperational;


                // Konwersja ProductionStatus na bool, jeśli to konieczne
                if (productionStatusNode.Value is bool statusAsBool)
                {
                    isOperational = statusAsBool;
                }
                else if (productionStatusNode.Value is int statusAsInt)
                {
                    // Zakładamy, że 1 oznacza "true", a 0 oznacza "false"
                    isOperational = statusAsInt != 0;
                }
                else
                {
                    // Jeśli typ jest nieoczekiwany, zgłoś wyjątek lub zignoruj błąd
                    throw new InvalidOperationException($"Unexpected ProductionStatus type: {productionStatusNode.Value?.GetType()}");
                }

                return new Program.DeviceState
                {
                    DeviceId = deviceId,
                    DeviceErrors = AnalyzeErrors(deviceErrors),
                    IsOperational = isOperational,
                    CurrentProductionRate = productionRate
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading device state for Device {deviceId}: {ex.Message}");
                return null;
            }
        }

        private string AnalyzeErrors(int deviceErrors)
        {
            List<string> errors = new();

            if ((deviceErrors & Convert.ToInt32(Errors.Unknown)) != 0) errors.Add("Unknown");
            if ((deviceErrors & Convert.ToInt32(Errors.SensorFailue)) != 0) errors.Add("Sensor Failure");
            if ((deviceErrors & Convert.ToInt32(Errors.PowerFailure)) != 0) errors.Add("Power Failure");
            if ((deviceErrors & Convert.ToInt32(Errors.EmergencyStop)) != 0) errors.Add("Emergency Stop");

            return errors.Count > 0 ? string.Join(", ", errors) : "No errors";
        }

        public void Disconnect()
        {
            if (_isConnected)
            {
                try
                {
                    _client.Disconnect();
                    _isConnected = false;
                    Console.WriteLine("Disconnected from OPC UA server.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error disconnecting from OPC UA server: {ex.Message}");
                }
            }
        }
        public IEnumerable<string> GetDeviceNodes()
        {
            try
            {
                var rootNode = _client.BrowseNode(OpcObjectTypes.ObjectsFolder);

                var devices = new List<string>();
                foreach (var node in rootNode.Children())
                {
                    if (node.DisplayName.Value.Contains("Device"))
                    {
                        devices.Add(node.DisplayName.Value);
                    }
                }

                return devices;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error browsing nodes: {ex.Message}");
                return Enumerable.Empty<string>();
            }
        }
        public dynamic ReadTelemetryData(int deviceId)
        {
            try
            {
                return new
                {
                    ProductionStatus = _client.ReadNode($"ns=2;s=Device {deviceId}/ProductionStatus").Value,
                    GoodCount = _client.ReadNode($"ns=2;s=Device {deviceId}/GoodCount").Value,
                    BadCount = _client.ReadNode($"ns=2;s=Device {deviceId}/BadCount").Value,
                    Temperature = _client.ReadNode($"ns=2;s=Device {deviceId}/Temperature").Value,
                    ProductionRate = _client.ReadNode($"ns=2;s=Device {deviceId}/ProductionRate").Value,
                    WorkorderId = _client.ReadNode($"ns=2;s=Device {deviceId}/WorkorderId").Value
            };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading telemetry data: {ex.Message}");
                return null;
            }
        }

        public void SetProductionRate(int deviceId, int productionRate)
        {
            Console.WriteLine($"Setting ProductionRate for Device {deviceId} to {productionRate}.");
            _client.WriteNode($"ns=2;s=Device {deviceId}/ProductionRate", productionRate);
            Console.WriteLine($"ProductionRate for Device {deviceId} set to {productionRate}%.");
        }

        public void EmergencyStop(int deviceId)
        {
            Console.WriteLine($"Setting EmergencyStop to work...");
            _client.CallMethod($"ns=2;s=Device {deviceId}", $"ns=2;s=Device {deviceId}/EmergencyStop");
            Console.WriteLine($"Emergency stop working...");
        }

        public void ResetErrorStatus(int deviceId)
        {
            _client.CallMethod($"ns=2;s=Device {deviceId}", $"ns=2;s=Device {deviceId}/ResetErrorStatus");
        }
    }
}
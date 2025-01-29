// OpcUaService.cs
using Azure;
using Azure.Communication.Email;
using Opc.UaFx;
using Opc.UaFx.Client;

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
        public int GetProductionRate(int deviceId)
        {
            try
            {
                var productionRateNode = _client.ReadNode($"ns=2;s=Device {deviceId}/ProductionRate");
                return Convert.ToInt32(productionRateNode.Value);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading ProductionRate for Device {deviceId}: {ex.Message}");
                return 0; // W razie błędu ustaw domyślną wartość 0
            }
        }
        public async Task SendEmailAsync(string toEmail, string subject, string body, string communicationString)
        {
            // Podaj Connection String z Azure Communication Services
            string connectionString = communicationString;

            var emailClient = new EmailClient(connectionString);
            var emailContent = new EmailContent(subject)
            {
                PlainText = body
            };

            var emailMessage = new EmailMessage("DoNotReply@bf4cbd14-ada5-474c-9381-f7e73f53926d.azurecomm.net", toEmail, emailContent);

            try
            {
                // Poprawne wywołanie bez użycia typów powodujących błędy
                var response = await emailClient.SendAsync(WaitUntil.Completed, emailMessage);
                Console.WriteLine($"Email sent successfully. Status: {response.HasCompleted}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to send email: {ex.Message}");
            }
        }

        public async Task MonitorDeviceErrorsAsync(int deviceId, Func<int, Task> onDeviceErrorsChanged)
        {
            try
            {
                var currentErrors = (int)_client.ReadNode($"ns=2;s=Device {deviceId}/DeviceError").Value;

                if (!_lastDeviceErrors.TryGetValue(deviceId, out var lastErrors) || currentErrors != lastErrors)
                {
                    _lastDeviceErrors[deviceId] = currentErrors;
                    var errorDescriptions = string.Join(", ", AnalyzeErrors(currentErrors));
                    if (errorDescriptions != "No errors")
                    {
                        Console.WriteLine($"Device {deviceId} errors changed: {errorDescriptions}");

                        // Wywołanie funkcji, gdy wykryto zmianę w DeviceErrors
                        await onDeviceErrorsChanged(currentErrors);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error monitoring device errors for Device {deviceId}: {ex.Message}");
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

        public List<string> AnalyzeErrors(int deviceErrors)
        {
            List<string> errors = new();

            if ((deviceErrors & Convert.ToInt32(Errors.Unknown)) != 0) errors.Add("Unknown");
            if ((deviceErrors & Convert.ToInt32(Errors.SensorFailure)) != 0) errors.Add("Sensor Failure");
            if ((deviceErrors & Convert.ToInt32(Errors.PowerFailure)) != 0) errors.Add("Power Failure");
            if ((deviceErrors & Convert.ToInt32(Errors.EmergencyStop)) != 0) errors.Add("Emergency Stop");

            return errors.Count > 0 ? errors : new List<string> { "No errors" };
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
                int deviceIndex = 1;
                foreach (var node in rootNode.Children())
                {
                    if (node.DisplayName.Value.Contains("Device"))
                    {
                        string deviceId = $"DeviceDemoSdk{deviceIndex}";
                        devices.Add(deviceId);
                        deviceIndex++;
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
                    DeviceID=deviceId,
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
            Console.WriteLine($"Setting EmergencyStop for Device {deviceId} to work...");
            _client.CallMethod($"ns=2;s=Device {deviceId}", $"ns=2;s=Device {deviceId}/EmergencyStop");
            Console.WriteLine($"Emergency stop working...");
        }

        public void ResetErrorStatus(int deviceId)
        {
            _client.CallMethod($"ns=2;s=Device {deviceId}", $"ns=2;s=Device {deviceId}/ResetErrorStatus");
        }
    }
}
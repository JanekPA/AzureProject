using IoTAgent.Services;
using Microsoft.Azure.Devices;
using Microsoft.Azure.Devices.Shared;
using Opc.UaFx.Client;
using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace IoTAgent.Services
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            // Load configuration
            var config = JsonSerializer.Deserialize<Config>(File.ReadAllText("appsettings.json"));
            if (config == null)
            {
                Console.WriteLine("Configuration loading failed.");
                return;
            }
            else
            {
                Console.WriteLine("Configuration loaded succesfully.");
            }

            // Initialize services
            var opcUaService = new OpcUaService(config.OpcUaEndpoint);
            var iotHubService = new IoTHubService(config.IoTHubConnectionString);


            CancellationTokenSource cts = new();
            // W programie Main, po utworzeniu IoTHubService, dodajemy wywołanie tej metody.
            var receiveMessagesTask = Task.Run(() => iotHubService.ReceiveCloudToDeviceMessagesAsync(cts.Token));
            try
            {
                
                // Connect to OPC UA server
                opcUaService.Connect();

                // Initialize IoT Hub handlers using InitializeDirectMethodsAsync
                await iotHubService.InitializeDirectMethodsAsync(
                    async deviceId => opcUaService.EmergencyStop(int.Parse(deviceId)),
                    async () => opcUaService.ResetErrorStatus(config.DeviceId),
                    async newRate => opcUaService.SetProductionRate(config.DeviceId, newRate));

                // Start monitoring device twin properties (run in parallel)
                Console.WriteLine("Starting monitoring device twin properties...");

                var monitorTwinTask = iotHubService.MonitorDeviceTwinAsync(

                    async (key, value) =>
                    {
                        if (key == "ProductionRate" && value is int productionRate)
                        {
                            opcUaService.SetProductionRate(config.DeviceId, productionRate);
                        }
                    });
                // Start telemetry loop
                Console.WriteLine("Starting telemetry loop...");
                var knownDevices = new HashSet<int>();
                while (!cts.Token.IsCancellationRequested)
                {
                    // Odświeżenie listy urządzeń
                    var deviceNodes = opcUaService.GetDeviceNodes();

                    if (!deviceNodes.Any())  // Jeśli brak urządzeń
                    {
                        Console.WriteLine("No devices detected. Waiting for new devices...");
                        await Task.Delay(1000); // Czekaj sekundę przed ponownym sprawdzeniem
                        continue; // Przejdź do kolejnej iteracji
                    }

                    //var telemetryData = opcUaService.ReadTelemetryData(config.DeviceId);
                   

                    foreach (var deviceNode in deviceNodes)
                    {
                        if (int.TryParse(deviceNode.Replace("Device ", ""), out int deviceId))
                        {
                            if (!knownDevices.Contains(deviceId))
                            {
                                knownDevices.Add(deviceId);
                                Console.WriteLine($"New device detected: Device {deviceId}");
                                
                            }
                            var telemetryData = opcUaService.ReadTelemetryData(deviceId);
                            if (telemetryData != null)
                            {
                                await iotHubService.SendTelemetryAsync(telemetryData);

                            }
                            // Initialize monitoring desired properties
                            await iotHubService.MonitorDesiredPropertiesAsync(async (key, value) =>
                            {
                                if (key == "ProductionRate" && value is int productionRate)
                                {
                                    opcUaService.SetProductionRate(config.DeviceId, productionRate);
                                    await iotHubService.UpdateReportedPropertiesAsync("ProductionRate", productionRate);
                                    Console.WriteLine("Attempted to update Reported Properties: ProductionRate = " + productionRate);
                                }
                            });
                            var deviceState = opcUaService.ReadDeviceState(deviceId);
                            Console.WriteLine("    Device State    ");
                            Console.WriteLine($"Device ID: {deviceState.DeviceId}");
                            Console.WriteLine($"Device Errors: {deviceState.DeviceErrors}");
                            Console.WriteLine($"Is Operational: {deviceState.IsOperational}");
                            Console.WriteLine($"Current Production Rate: {deviceState.CurrentProductionRate}\n");
                            await Task.Delay(config.TelemetryInterval);
                        }
                        if (!deviceNodes.Any())
                        {
                            Console.WriteLine("No devices detected. Waiting for new devices...");
                        }
                    }
                }
                // Wait for tasks to finish
                await Task.WhenAny(monitorTwinTask,receiveMessagesTask);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
            finally
            {
                // Cleanup
                opcUaService.Disconnect();
            }
        }
        public class DeviceState
        {
            public int DeviceId { get; set; }
            public string DeviceErrors { get; set; } = string.Empty;
            public bool IsOperational { get; set; }
            public int CurrentProductionRate { get; set; }
        }
        public class Config
        {
            public string OpcUaEndpoint { get; set; } = "opc.tcp://localhost:4840";
            public string IoTHubConnectionString { get; set; } = string.Empty;
            public int DeviceId { get; set; } = 1;
            public int TelemetryInterval { get; set; } = 5000;
        }
    }
}
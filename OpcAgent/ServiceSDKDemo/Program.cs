using Azure.Storage.Blobs;
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
            Console.WriteLine("Configuration loaded successfully.");

            // Initialize services
            var opcUaService = new OpcUaService(config.OpcUaEndpoint);
            opcUaService.Connect();

            var devices = opcUaService.GetDeviceNodes().ToList();
            if (!devices.Any())
            {
                Console.WriteLine("No devices found on the OPC UA server.");
                return;
            }
            Console.WriteLine("Available devices:");
            for (int i = 0; i < devices.Count; i++)
            {
                Console.WriteLine($"{i + 1}. {devices[i]}");
            }

            Console.Write("Select a device by entering the corresponding number: ");
            if (!int.TryParse(Console.ReadLine(), out int selectedDeviceIndex) || selectedDeviceIndex < 1 || selectedDeviceIndex > devices.Count)
            {
                Console.WriteLine("Invalid selection.");
                return;
            }

            string selectedDeviceId = devices[selectedDeviceIndex - 1];
            int deviceId = int.Parse(selectedDeviceId.Replace("DeviceDemoSdk", ""));
            Console.WriteLine($"You selected: {selectedDeviceId}");

            if (!config.IoTHubConnectionStrings.TryGetValue(selectedDeviceId, out string deviceConnectionString))
            {
                Console.WriteLine($"No IoT Hub connection string found for {selectedDeviceId}.");
                return;
            }

            var iotHubService = new IoTHubService(deviceConnectionString);
            await iotHubService.InitializeDirectMethodsAsync(
                async () => opcUaService.EmergencyStop(deviceId),
                async () => opcUaService.ResetErrorStatus(deviceId),
                async rate => opcUaService.SetProductionRate(deviceId, rate)
            );

            CancellationTokenSource cts = new();
            Task telemetryTask = Task.Run(async () =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        await opcUaService.MonitorDeviceErrorsAsync(deviceId, async (newErrors) =>
                        {
                            // Wyślij wiadomość D2C z nowymi błędami
                            var analyzedErrors = opcUaService.AnalyzeErrors(newErrors);
                            await iotHubService.SendDeviceErrorsTelemetryAsync(deviceId, analyzedErrors);

                            // Aktualizacja reported properties na podstawie nowych błędów
                            var reportedProperties = new Dictionary<string, object>
                            {
                                { "DeviceErrors", analyzedErrors }
                            };

                            // Aktualizacja Device Twin w IoT Hub
                            await iotHubService.UpdateReportedPropertiesAsync(reportedProperties);
                        });
                        var telemetryData = opcUaService.ReadTelemetryData(deviceId);
                        if (telemetryData != null)
                        {
                            await iotHubService.SendTelemetryAsync(telemetryData, new AzureStorageService(config.StorageConnectionString), config.BlobContainerName, int.Parse(selectedDeviceId.Replace("DeviceDemoSdk", "")));
                        }

                        var deviceState = opcUaService.ReadDeviceState(deviceId);
                        if (deviceState != null)
                        {
                            await iotHubService.MonitorDeviceTwinAsync(opcUaService, deviceState.DeviceId);
                            string stateBlobName = $"Device{deviceState.DeviceId}_state_{DateTime.UtcNow:yyyyMMddHHmmss}.json";
                            string stateJsonData = JsonSerializer.Serialize(deviceState);
                            var storageService = new AzureStorageService(config.StorageConnectionString);
                            await storageService.UploadJsonAsync(config.BlobContainerName, stateBlobName, stateJsonData);
                        }

                        iotHubService.ReceiveCloudToDeviceMessagesAsync(cts.Token);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error during telemetry task: {ex.Message}");
                    }

                    await Task.Delay(config.TelemetryInterval);
                }
            }, cts.Token);

            Console.WriteLine("Press Enter to stop...");
            Console.ReadLine();

            cts.Cancel();
            try
            {
                await telemetryTask;
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Telemetry task canceled.");
            }
            finally
            {
                opcUaService.Disconnect();
            }
        }
        public class DeviceState
        {
            public int DeviceId { get; set; }
            public List<string> DeviceErrors { get; set; } = null;
            public bool IsOperational { get; set; }
            public int CurrentProductionRate { get; set; }
        }
        public class Config
        {
            public string OpcUaEndpoint { get; set; } = "";
            public Dictionary<string, string> IoTHubConnectionStrings { get; set; } = new();
            public int DeviceId { get; set; } = 1;
            public string StorageConnectionString { get; set; } = string.Empty;

            public int TelemetryInterval { get; set; } = 5000;
            public string BlobContainerName { get; set; } = string.Empty;
        }
    }
}
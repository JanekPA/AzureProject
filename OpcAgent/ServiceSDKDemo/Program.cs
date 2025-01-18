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
            else
            {
                Console.WriteLine("Configuration loaded succesfully.");
            }
            var storageService = new AzureStorageService(config.StorageConnectionString);
            // Initialize devices and services
            var devices = new Dictionary<string, (OpcUaService opcUaService, IoTHubService iotHubService)>();
            foreach (var (deviceId, connectionString) in config.IoTHubConnectionStrings)
            {
                var opcUaService = new OpcUaService(config.OpcUaEndpoint);
                var iotHubService = new IoTHubService(connectionString);


                opcUaService.Connect();
                await iotHubService.InitializeDirectMethodsAsync(
                    async () => opcUaService.EmergencyStop(int.Parse(deviceId.Replace("DeviceDemoSdk",""))),
                    async () => opcUaService.ResetErrorStatus(int.Parse(deviceId.Replace("DeviceDemoSdk", ""))),
                    async rate => opcUaService.SetProductionRate(int.Parse(deviceId.Replace("DeviceDemoSdk", "")), rate)
                );

                devices.Add(deviceId, (opcUaService, iotHubService));
            }

            // Start telemetry and twin monitoring for each device
            CancellationTokenSource cts = new();
            var tasks = new List<Task>();

            foreach (var kvp in devices)
            {
                var deviceId = kvp.Key;
                var (opcUaService, iotHubService) = kvp.Value;

                tasks.Add(Task.Run(async () =>
                {
                    while (!cts.Token.IsCancellationRequested)
                    {
                        // Read telemetry data
                        var telemetryData = opcUaService.ReadTelemetryData(int.Parse(deviceId.Replace("DeviceDemoSdk", "")));
                        if (telemetryData != null)
                        {
                            await iotHubService.SendTelemetryAsync(telemetryData, storageService, config.BlobContainerName, deviceId);
                        }
                        iotHubService.ReceiveCloudToDeviceMessagesAsync(cts.Token);

                        // Monitor device twin properties
                        var deviceState = opcUaService.ReadDeviceState(int.Parse(deviceId.Replace("DeviceDemoSdk", "")));
                        if (deviceState != null)
                        {
                            await iotHubService.MonitorDeviceTwinAsync(opcUaService, deviceState.DeviceId);
                            string stateBlobName = $"Device {deviceId}: state_{DateTime.UtcNow:yyyyMMddHHmmss}.json";
                            Console.WriteLine("StorageConnectionString: " + config.StorageConnectionString);
                            string stateJsonData = JsonSerializer.Serialize(deviceState);
                            await storageService.UploadJsonAsync(config.BlobContainerName, stateBlobName, stateJsonData);
                        }

                        await Task.Delay(config.TelemetryInterval);
                    }
                }, cts.Token));
            }

            // Await tasks and handle cancellation
            try
            {
                await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
            finally
            {
                // Cleanup
                foreach (var (_, (opcUaService, _)) in devices)
                {
                    opcUaService.Disconnect();
                }
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
            public string OpcUaEndpoint { get; set; } = "";
            public Dictionary<string, string> IoTHubConnectionStrings { get; set; } = new();
            public int DeviceId { get; set; } = 1;
            public string StorageConnectionString { get; set; } = string.Empty;

            public int TelemetryInterval { get; set; } = 5000;
            public string BlobContainerName { get; set; } = string.Empty;
        }
    }
}
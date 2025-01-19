// IoTHubService.cs
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Shared;
using Newtonsoft.Json;
using Opc.UaFx.Client;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IoTAgent;
using static IoTAgent.Services.Program;
using System.Linq.Expressions;
using Microsoft.Win32;
using Microsoft.Azure.Devices;
using Azure.Storage.Blobs;
using System.Text.Json;

namespace IoTAgent.Services
{
    public class IoTHubService
    {
        private readonly DeviceClient _deviceClient;
        private readonly RegistryManager registry;



        public IoTHubService(string connectionString)
        {
            _deviceClient = DeviceClient.CreateFromConnectionString(connectionString, Microsoft.Azure.Devices.Client.TransportType.Mqtt);
            Console.WriteLine("Device client created successfully.");
        }

        // This method sends telemetry data to the cloud
        public async Task SendTelemetryAsync(dynamic telemetryData, AzureStorageService storageService, string BlobContainerName, int deviceId)
        {
            // Przygotowanie stringa do wyświetlenia danych telemetrycznych po spacji
            string telemetryDataFormatted = string.Join(" ", new[]
            {
        $"\nProductionRate = {telemetryData.ProductionRate}",
        $"\nProductionStatus = {telemetryData.ProductionStatus}",
        $"\nGoodCount = {telemetryData.GoodCount}",
        $"\nBadCount = {telemetryData.BadCount}",
        $"\nTemperature = {telemetryData.Temperature}",
        $"\nWorkorderId = {telemetryData.WorkorderId}"
    });
            // Wyświetlenie danych telemetrycznych
            Console.WriteLine(telemetryDataFormatted);
            var messageString = JsonConvert.SerializeObject(telemetryData);
            var message = new Microsoft.Azure.Devices.Client.Message(Encoding.UTF8.GetBytes(messageString))
            {
                ContentType = "application/json",
                ContentEncoding = "utf-8"
            };

            await _deviceClient.SendEventAsync(message);
            string blobName = $"Device{deviceId}telemetry_{DateTime.UtcNow:yyyyMMddHHmmss}.json";
            await storageService.UploadJsonAsync(BlobContainerName, blobName, messageString);
            //Console.WriteLine("\nTelemetry sent! ");
        }

        public async Task ReceiveCloudToDeviceMessagesAsync(CancellationToken cancellationToken)
        {

            try
            {
                Console.WriteLine("Listening for Cloud-to-Device messages...");
                while (!cancellationToken.IsCancellationRequested)
                {
                    var message = await _deviceClient.ReceiveAsync(TimeSpan.FromSeconds(2));
                    if (message != null)
                    {
                        string messageData = Encoding.UTF8.GetString(message.GetBytes());
                        Console.WriteLine($"Received message: {messageData}");

                        await _deviceClient.CompleteAsync(message);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error receiving message: {ex.Message}");
            }
        }

        public async Task InitializeDirectMethodsAsync(Func<Task> handleEmergencyStop, Func<Task> handleResetErrorStatus, Func<int, Task> handleSetProductionRate)
        {
            await _deviceClient.SetMethodHandlerAsync("EmergencyStop", async (request, context) =>
            {
                Console.WriteLine("Emergency Stop triggered.");
                await handleEmergencyStop();
                return new MethodResponse(200);
            }, null);

            await _deviceClient.SetMethodHandlerAsync("ResetErrorStatus", async (request, context) =>
            {
                Console.WriteLine("Reset Error Status triggered.");
                await handleResetErrorStatus();
                return new MethodResponse(200);
            }, null);

            await _deviceClient.SetMethodHandlerAsync("SetProductionRate", async (request, context) =>
            {
                int newRate = int.Parse(request.DataAsJson);
                Console.WriteLine($"Set Production Rate to {newRate}.");
                await handleSetProductionRate(newRate);
                return new MethodResponse(200);
            }, null);

            Console.WriteLine("Direct methods initialized.");
        }


        // This method listens to changes in device twin properties
        public async Task MonitorDeviceTwinAsync(OpcUaService opcService, int deviceId)
        {

            await _deviceClient.SetDesiredPropertyUpdateCallbackAsync(async (desiredProperties, context) =>
            {
                foreach (KeyValuePair<string, object> property in desiredProperties)
                {
                    Console.WriteLine($"Key: {property.Key}, Value: {property.Value}");
                    if (property.Key == "ProductionRate" && int.TryParse(property.Value.ToString(), out int newRate))
                    {
                        opcService.SetProductionRate(deviceId, newRate);
                        Console.WriteLine($"Production rate updated to {newRate}%.");


                        // Teraz aktualizujemy "reported" z nową wartością

                        var reportedProperties = new TwinCollection
                        {
                            ["ProductionRate"] = newRate
                        };
                        //Console.WriteLine("Updating reported properties...");
                        await _deviceClient.UpdateReportedPropertiesAsync(reportedProperties);
                        opcService.SetProductionRate(deviceId, newRate);
                        Console.WriteLine("Reported properties updated.");
                    }
                }
            }, null);
            var twin = await _deviceClient.GetTwinAsync();
            //Console.WriteLine("Device Twin monitoring initialized.");
        }

        public async Task SendDeviceErrorsTelemetryAsync(int deviceId, List<string> deviceErrors)
        {
            var telemetryData = new
            {
                DeviceId = deviceId,
                Timestamp = DateTime.UtcNow,
                DeviceErrors = deviceErrors
            };

            string messageString = JsonConvert.SerializeObject(telemetryData);
            var message = new Microsoft.Azure.Devices.Client.Message(Encoding.UTF8.GetBytes(messageString))
            {
                ContentType = "application/json",
                ContentEncoding = "utf-8"
            };

            await _deviceClient.SendEventAsync(message);
            Console.WriteLine($"Device {deviceId} error telemetry sent: {messageString}");
        }

        // Update reported properties for ProductionRate and DeviceErrors
        public async Task UpdateReportedPropertiesAsync(Dictionary<string, object> reportedProperties)
        {
            if (reportedProperties == null)
            {
                throw new ArgumentNullException(nameof(reportedProperties), "Reported properties cannot be null.");
            }

            var twinCollection = new TwinCollection();
            foreach (var property in reportedProperties)
            {
                twinCollection[property.Key] = property.Value;
            }

            await _deviceClient.UpdateReportedPropertiesAsync(twinCollection);

            //Console.WriteLine("Reported properties updated: " + twinCollection.ToJson());
        }
        // Handle changes to desired properties

    }
        [Flags]
        public enum Errors
        {
            None = 0,
            EmergencyStop = 1,
            PowerFailure = 2,
            SensorFailure = 4,
            Unknown = 8
        }
        public class AzureStorageService
        {
            private readonly BlobServiceClient _blobServiceClient;

            public AzureStorageService(string connectionString)
            {
                _blobServiceClient = new BlobServiceClient(connectionString);
            }

            public async Task UploadJsonAsync(string containerName, string blobName, string jsonData)
            {
                // Uzyskaj klienta kontenera
                var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);

                // Upewnij się, że kontener istnieje
                await containerClient.CreateIfNotExistsAsync();

                // Utwórz klienta blobu
                var blobClient = containerClient.GetBlobClient(blobName);

                // Prześlij dane
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(jsonData));
                await blobClient.UploadAsync(stream, overwrite: true);

                //Console.WriteLine($"Blob uploaded: {blobName}");
            }
        }


    }


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
        public async Task SendTelemetryAsync(dynamic telemetryData)
        {
            var messageString = JsonConvert.SerializeObject(telemetryData);
            var message = new Microsoft.Azure.Devices.Client.Message(Encoding.UTF8.GetBytes(messageString))
            {
                ContentType = "application/json",
                ContentEncoding = "utf-8"
            };

            await _deviceClient.SendEventAsync(message);
            Console.WriteLine("Telemetry sent: " + messageString);
        }

        public async Task ReceiveCloudToDeviceMessagesAsync(CancellationToken cancellationToken)
        {

            try
            {
                Console.WriteLine("Starting to listen for Cloud-to-Device messages...");
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

        public async Task InitializeDirectMethodsAsync(Func<string, Task> handleEmergencyStop, Func<Task> handleResetErrorStatus, Func<int, Task> handleSetProductionRate)
        {
            await _deviceClient.SetMethodHandlerAsync("EmergencyStop", async (request, context) =>
            {
                Console.WriteLine("Emergency Stop triggered.");
                await handleEmergencyStop(request.DataAsJson);
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
        public async Task MonitorProductionRateAsync(int deviceId, Func<int, Task> updateReportedTwinAsync)
        {
            try
            {
                //var currentRate = (int)_deviceClient.ReadNode($"ns=2;s=Device {deviceId}/ProductionRate").Value;
                //Console.WriteLine($"Current ProductionRate for Device {deviceId}: {currentRate}");
                //await updateReportedTwinAsync(currentRate);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error monitoring ProductionRate for Device {deviceId}: {ex.Message}");
            }
        }
        public async Task UpdateReportedProductionRateAsync(int productionRate)
        {
            var reportedProperties = new TwinCollection
            {
                ["ProductionRate"] = productionRate
            };
            await _deviceClient.UpdateReportedPropertiesAsync(reportedProperties);
            Console.WriteLine($"Reported ProductionRate updated to {productionRate}.");
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
                        Console.WriteLine("Updating reported properties...");
                        await _deviceClient.UpdateReportedPropertiesAsync(reportedProperties);
                        opcService.SetProductionRate(deviceId,newRate);
                        Console.WriteLine("Reported properties updated.");
                    }
                }
            }, null);
            var twin = await _deviceClient.GetTwinAsync();
            Console.WriteLine($"Current twin properties: {twin.ToJson()}");
            Console.WriteLine("Device Twin monitoring initialized.");
        }

        public async Task UpdateReportedDeviceErrorsAsync(int deviceErrors)
        {
            var reportedProperties = new TwinCollection
            {
                ["DeviceErrors"] = deviceErrors
            };
            await _deviceClient.UpdateReportedPropertiesAsync(reportedProperties);
            Console.WriteLine($"Reported DeviceErrors updated to {deviceErrors}.");
        }
        private string AnalyzeErrors(int deviceErrors)
        {
            List<string> errors = new();

            if ((deviceErrors & Convert.ToInt32(Errors.Unknown)) != 0) errors.Add("Unknown");
            if ((deviceErrors & Convert.ToInt32(Errors.SensorFailue)) != 0) errors.Add("Sensor Failure");
            if ((deviceErrors & Convert.ToInt32(Errors.PowerFailure)) != 0) errors.Add("Power Failure");
            if ((deviceErrors & Convert.ToInt32(Errors.EmergencyStop)) != 0) errors.Add("Emergency Stop");

            var result = errors.Count > 0 ? string.Join(", ", errors) : "No errors";
            Console.WriteLine($"Analyzed errors for Device: {result}");
            return result;
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
            
            Console.WriteLine("Reported properties updated: " + twinCollection.ToJson());
        }
        // Handle changes to desired properties

    }

    [Flags]
    public enum Errors
    {
        None = 0,
        EmergencyStop = 1,
        PowerFailure = 2,
        SensorFailue = 4,
        Unknown = 8
    }
}

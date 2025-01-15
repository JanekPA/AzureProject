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

namespace IoTAgent.Services
{
    public class IoTHubService
    {
        private readonly DeviceClient _deviceClient;

        public IoTHubService(string connectionString)
        {
            _deviceClient = DeviceClient.CreateFromConnectionString(connectionString, TransportType.Mqtt);
            Console.WriteLine("Device client created successfully.");
        }

        // This method sends telemetry data to the cloud
        public async Task SendTelemetryAsync(dynamic telemetryData)
        {
            var messageString = JsonConvert.SerializeObject(telemetryData);
            var message = new Message(Encoding.UTF8.GetBytes(messageString))
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

        // This method listens to changes in device twin properties
        public async Task MonitorDeviceTwinAsync(Func<string, object, Task> onPropertyChanged)
        {
            // Monitor changes on device twin properties
            var twin = await _deviceClient.GetTwinAsync();

            // Check for changes in desired properties periodically (simplified approach)
            while (true)
            {
                twin = await _deviceClient.GetTwinAsync();
                foreach (var property in twin.Properties.Desired)
                {
                    // Rzutowanie na typ KeyValuePair
                    if (property is KeyValuePair<string, object> propertyKeyValue)
                    {
                        await onPropertyChanged(propertyKeyValue.Key, propertyKeyValue.Value);
                    }
                }

                await Task.Delay(5000); // delay for checking updates
            }
        }
        // Update reported properties for ProductionRate and DeviceErrors
        public async Task UpdateReportedPropertiesAsync(string key, object value)
        {
            try
            {
                var reportedProperties = new TwinCollection();
                reportedProperties[key] = value;
                await _deviceClient.UpdateReportedPropertiesAsync(reportedProperties);
                Console.WriteLine($"Updated reported property: {key} = {value}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating reported property {key}: {ex.Message}");
            }
        }

        // Handle changes to desired properties
        public async Task MonitorDesiredPropertiesAsync(Func<string, object, Task> onDesiredPropertyChanged)
        {
            try
            {
                await _deviceClient.SetDesiredPropertyUpdateCallbackAsync((desiredProperties, context) =>
                {
                    foreach (var property in desiredProperties)
                    {
                        if (property is KeyValuePair<string, object> propertyKeyValue)
                        {
                            if (propertyKeyValue.Key != null && propertyKeyValue.Value != null)
                            {
                                Console.WriteLine($"Desired property changed: {propertyKeyValue.Key} = {propertyKeyValue.Value}");
                                onDesiredPropertyChanged(propertyKeyValue.Key, propertyKeyValue.Value).Wait();
                            }
                        }
                    }
                    return Task.CompletedTask;
                }, null);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error monitoring desired properties: {ex.Message}");
            }
        }
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

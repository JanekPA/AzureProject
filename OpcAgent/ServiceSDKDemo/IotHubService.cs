// IoTHubService.cs
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Shared;
using Newtonsoft.Json;
using System.Text;
using Microsoft.Azure.Devices;
using Azure.Storage.Blobs;
using Azure.Messaging.ServiceBus;
using System;

namespace IoTAgent.Services
{
    public class IoTHubService
    {
        private readonly DeviceClient _deviceClient;
        private readonly RegistryManager registry;
        private readonly Dictionary<string, ServiceBusProcessor> _processors = new();
        private readonly ServiceBusClient _serviceBusClient;
        private readonly ServiceBusProcessor _processor;
        private readonly string _queueName;


        public IoTHubService(string connectionString, Dictionary<string, string> queueNames, string communicationString)
        {
            _deviceClient = DeviceClient.CreateFromConnectionString(connectionString, Microsoft.Azure.Devices.Client.TransportType.Mqtt);
            _serviceBusClient = new ServiceBusClient(communicationString);

            foreach (var queueName in queueNames.Keys)
            {
                var processor = _serviceBusClient.CreateProcessor(queueName, new ServiceBusProcessorOptions());
                _processors.Add(queueName, processor);
            }
            Console.WriteLine("Service bus processors created for all queues.");
            Console.WriteLine("Device client created successfully.");
        }


        public void RegisterQueueHandlers(Dictionary<string, Func<ServiceBusReceivedMessage, Task>> queueHandlers)
        {

            foreach (var kvp in queueHandlers)
            {
                if (_processors.TryGetValue(kvp.Key, out var processor))
                {
                    processor.ProcessMessageAsync += async args =>
                    {
                        await kvp.Value(args.Message);
                        await args.CompleteMessageAsync(args.Message);
                    };

                    processor.ProcessErrorAsync += args =>
                    {
                        Console.WriteLine($"Error processing queue {kvp.Key}: {args.Exception.Message}");
                        return Task.CompletedTask;
                    };
                }
            }

            Console.WriteLine("Handlers registered for all queues.");
        }
        public async Task<List<ServiceBusReceivedMessage>> ReceiveQueueMessagesAsync(string queueName)
        {
            var messagesList = new List<ServiceBusReceivedMessage>();

            if (_processors.TryGetValue(queueName, out var processor))
            {
                var receiver = _serviceBusClient.CreateReceiver(queueName);

                try
                {
                    var receivedMessages = await receiver.ReceiveMessagesAsync(maxMessages: 10, maxWaitTime: TimeSpan.FromSeconds(2)); 

                    if (receivedMessages.Any())
                    {
                        foreach (var message in receivedMessages)
                        {
                            messagesList.Add(message);
                            await receiver.CompleteMessageAsync(message); 
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error receiving messages from {queueName}: {ex.Message}");
                }
            }

            return messagesList;
        }


        public async Task ProcessQueueMessage(ServiceBusReceivedMessage message, OpcUaService opcUaService, string AlertEmail, string CommString)
        {
            
            var messageBody = Encoding.UTF8.GetString(message.Body);
            var data = JsonConvert.DeserializeObject<Dictionary<string, string>>(messageBody);

            if (data.TryGetValue("ActionType", out string actionType) && data.TryGetValue("DeviceId", out string deviceIdStr))
            {

                int deviceId = int.Parse(deviceIdStr);
                switch (actionType)
                {
                    case "EmergencyStop":
                        opcUaService.EmergencyStop(deviceId);
                        Console.WriteLine($"EmergencyStop executed for Device {deviceId}");
                        break;

                    case "DecreaseProductionRate":
                        int currentRate = opcUaService.GetProductionRate(deviceId);

                        opcUaService.SetProductionRate(deviceId, currentRate-10);
                        Console.WriteLine($"ProductionRate decreased from {currentRate} to {currentRate-10} for Device {deviceId}");
                        break;

                    case "SendEmail":
                        string errorMessage = $"Device {deviceId} reported {data["total_error_count"]} errors.";
                        await opcUaService.SendEmailAsync(AlertEmail, $"Device {deviceId} Error Alert", errorMessage, CommString);
                        Console.WriteLine($"Email sent for Device {deviceId}");
                        break;

                    default:
                        Console.WriteLine($"Unknown ActionType: {actionType}");
                        break;
                }

            }

        }


        public async Task StartQueueListenerAsync(string queueName)
        {
            if (_processors.TryGetValue(queueName, out var processor))
            {
                await processor.StartProcessingAsync();
                Console.WriteLine($"Listening to queue: {queueName}");
            }
            else
            {
                Console.WriteLine($"Queue {queueName} not found.");
            }
        }

        public async Task StopQueueListenerAsync()
        {
            foreach (var processor in _processors.Values)
            {
                await processor.StopProcessingAsync();
                
            }
        }

      
        public async Task SendTelemetryAsync(dynamic telemetryData, AzureStorageService storageService, string BlobContainerName, int deviceId)
        {
            
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
            string blobName = $"Device{deviceId}_telemetry.json";
            await storageService.UploadJsonAsync(BlobContainerName, blobName, messageString);
            //Console.WriteLine("\nTelemetry sent! ");
        }
        public async Task ReceiveCloudToDeviceMessagesAsync(CancellationToken cancellationToken)
        {

            try
            {
               
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


                        

                        var reportedProperties = new TwinCollection
                        {
                            ["ProductionRate"] = newRate
                        };
                        
                        await _deviceClient.UpdateReportedPropertiesAsync(reportedProperties);
                        opcService.SetProductionRate(deviceId, newRate);
                        Console.WriteLine("Reported properties updated.");
                    }
                }
            }, null);
            var twin = await _deviceClient.GetTwinAsync();
            
        }

        public async Task SendDeviceErrorsTelemetryAsync(int deviceId, List<string> deviceErrors)
        {
            if (deviceErrors == null || !deviceErrors.Any())
            {
                Console.WriteLine("No errors to report.");
                return;
            }
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

            
        }
        

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
                
                var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);

                
                await containerClient.CreateIfNotExistsAsync();

               
                var blobClient = containerClient.GetBlobClient(blobName);

               
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(jsonData));
                await blobClient.UploadAsync(stream, overwrite: true);

             
            }
        }


    }


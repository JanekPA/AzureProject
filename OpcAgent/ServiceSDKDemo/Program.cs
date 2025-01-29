using System.Text;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Newtonsoft.Json;



namespace IoTAgent.Services
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            // Load configuration
            var config = System.Text.Json.JsonSerializer.Deserialize<Config>(File.ReadAllText("appsettings.json"));

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

            var queueNames = new Dictionary<string, string>
{
                { "blogicqueueprate", "DecreaseProductionRateQueue" },
                { "blogicqueueerror", "EmergencyStopQueue" },
                { "blogicqueue", "SendEmailQueue" }
            };
            var iotHubService = new IoTHubService(deviceConnectionString, queueNames, config.CommunicationString);

            await iotHubService.InitializeDirectMethodsAsync(
                async () => opcUaService.EmergencyStop(deviceId),
                async () => opcUaService.ResetErrorStatus(deviceId),
                async rate => opcUaService.SetProductionRate(deviceId, rate)
            );
            var queueHandlers = new Dictionary<string, Func<ServiceBusReceivedMessage, Task>>
            {
                { "blogicqueueprate", async message =>
                    {
                        Console.WriteLine("Processing message from blogicqueueprate...");
                        await iotHubService.ProcessQueueMessage(message, opcUaService, config.AlertEmail, config.CommString);
                    }
                },
                { "blogicqueueerror", async message =>
                    {
                        Console.WriteLine("Processing message from blogicqueueerror...");
                        await iotHubService.ProcessQueueMessage(message, opcUaService, config.AlertEmail, config.CommString);
                    }
                },
                { "blogicqueue", async message =>
                    {
                        Console.WriteLine("Processing message from blogicqueue...");
                        await iotHubService.ProcessQueueMessage(message, opcUaService, config.AlertEmail, config.CommString);
                    }
                }
            };

            // Zarejestruj handlery dla każdej kolejki
            iotHubService.RegisterQueueHandlers(queueHandlers);



            CancellationTokenSource cts = new();
            Task telemetryTask = Task.Run(async () =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        // Uruchom procesory dla każdej kolejki
                        foreach (var queueName in queueHandlers.Keys)
                        {
                            await iotHubService.StartQueueListenerAsync(queueName);
                        }
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
                            string stateBlobName = $"Device{deviceState.DeviceId}_state.json";
                            string stateJsonData = System.Text.Json.JsonSerializer.Serialize(deviceState);
                            var storageService = new AzureStorageService(config.StorageConnectionString);
                            await storageService.UploadJsonAsync(config.BlobContainerName, stateBlobName, stateJsonData);
                        }
                        Console.WriteLine("Listening for Cloud-to-Devices messages for "+ selectedDeviceId);
                        iotHubService.ReceiveCloudToDeviceMessagesAsync(cts.Token);
                        bool messagesProcessed = false;
                        foreach (var queueName in queueHandlers.Keys)
                        {
                            var messages = await iotHubService.ReceiveQueueMessagesAsync(queueName); // Pobieramy wiadomości z Service Bus
                            if(messages.Any()) messagesProcessed = true;
                            foreach (var message in messages)
                            {
                                Console.WriteLine($"Processing message from {queueName}...");
                                await iotHubService.ProcessQueueMessage(message, opcUaService, config.AlertEmail, config.CommString); // Przetwarzamy wiadomość
                            }
                        }
                       
                        if (!messagesProcessed)
                        {
                            await Task.Delay(500); // Oczekujemy 0.5 sekundy, żeby nie sprawdzać pustych kolejek w kółko
                        }
                        //Console.ReadLine();
                        await iotHubService.StopQueueListenerAsync();

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
            public string AlertEmail { get; set; } = "";
            public string CommunicationString { get; set; } = "";
            public string CommString { get; set; } = "";
            public string StorageConnectionString { get; set; } = string.Empty;

            public int TelemetryInterval { get; set; } = 5000;
            public string BlobContainerName { get; set; } = string.Empty;
        }
    }
}
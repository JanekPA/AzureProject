using System;
using System.Linq.Expressions;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.Devices;
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Shared;

using Opc.UaFx;
using Opc.UaFx.Client;

namespace IoTAgent
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var config = ConfigLoader.Load();

            Console.WriteLine("Starting IoT Agent...");

            var opcService = new OpcUaService(config.OpcUaEndpoint);
            var iotHubService = new IoTHubService(config.IoTHubConnectionString);

            try
            {
                opcService.Connect();
                Console.WriteLine("Connected to OPC UA server.");

                await iotHubService.InitializeDirectMethodsAsync(opcService);
                await iotHubService.MonitorDeviceTwinAsync(opcService);

                while (true)
                {
                    var telemetryData = opcService.ReadTelemetryData();
                    await iotHubService.SendTelemetryAsync(telemetryData);
                    await iotHubService.UpdateReportedPropertiesAsync(opcService.GetDeviceState());
                    await Task.Delay(config.TelemetryInterval);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
            finally
            {
                opcService.Disconnect();
                Console.WriteLine("Disconnected from OPC UA server.");
            }
        }
    }
    public static class ConfigLoader
    {
        public static Config Load()
        {
            return new Config
            {
                OpcUaEndpoint = "opc.tcp://localhost:4840",
                IoTHubConnectionString = "HostName=IoTProject2025.azure-devices.net;DeviceId=DeviceDemoSdk1;SharedAccessKey=pBotxwQFaKgccxxgnks/ZF5gxXgNE++kH3N4rIboxJg=",
                TelemetryInterval = 5000
            };
        }
    }

    public class Config
    {
        public string OpcUaEndpoint { get; set; }
        public string IoTHubConnectionString { get; set; }
        public int TelemetryInterval { get; set; }
    }
    public class OpcUaService
    {
        private readonly OpcClient _client;

        public OpcUaService(string endpoint)
        {
            _client = new OpcClient(endpoint);
        }

        public void Connect()
        {
            _client.Connect();
        }

        public void Disconnect()
        {
            _client.Disconnect();
        }

        public object ReadTelemetryData()
        {
            try
            {
                return new
                {
                    ProductionStatus = Convert.ToInt32(_client.ReadNode("ns=2;s=ProductionStatus")),
                    GoodCount = Convert.ToInt32(_client.ReadNode("ns=2;s=GoodCount")),
                    BadCount = Convert.ToInt32(_client.ReadNode("ns=2;s=BadCount")),
                    Temperature = Convert.ToDouble(_client.ReadNode("ns=2;s=Temperature"))
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading telemetry data: {ex.Message}");
                return null; // Możesz również zwrócić wartości domyślne
            }
        }

        public object GetDeviceState()
        {
            try
            {
                return new
                {
                    ProductionRate = Convert.ToInt32(_client.ReadNode("ns=2;s=ProductionRate")),
                    DeviceErrors = Convert.ToInt32(_client.ReadNode("ns=2;s=DeviceErrors"))
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading device state: {ex.Message}");
                return null;
            }
        }

        public void SetDesiredProductionRate(int productionRate)
        {
            _client.WriteNode("ns=2;s=ProductionRate", productionRate);
        }
    }

    public class IoTHubService
    {
        private readonly DeviceClient _deviceClient;

        public IoTHubService(string connectionString)
        {
            _deviceClient = DeviceClient.CreateFromConnectionString(connectionString, Microsoft.Azure.Devices.Client.TransportType.Mqtt);
        }

        public async Task InitializeDirectMethodsAsync(OpcUaService opcService)
        {
            await _deviceClient.SetMethodHandlerAsync("EmergencyStop", async (request, context) =>
            {
                Console.WriteLine("Emergency Stop triggered.");
                return await Task.FromResult(new MethodResponse(200));
            }, null);

            await _deviceClient.SetMethodHandlerAsync("ResetErrorStatus", async (request, context) =>
            {
                Console.WriteLine("Error status reset.");
                return await Task.FromResult(new MethodResponse(200));
            }, null);

            Console.WriteLine("Direct methods initialized.");
        }

        public async Task MonitorDeviceTwinAsync(OpcUaService opcService)
        {
            await _deviceClient.SetDesiredPropertyUpdateCallbackAsync((desiredProperties, context) =>
            {
                foreach (KeyValuePair<string, object> property in desiredProperties)
                {
                    if (property.Key == "ProductionRate" && property.Value is int newRate)
                    {
                        opcService.SetDesiredProductionRate(newRate);
                        Console.WriteLine($"Production rate updated to {newRate}%.");
                    }
                }
                return Task.CompletedTask;
            }, null);

            Console.WriteLine("Device Twin monitoring initialized.");
        }

        public async Task SendTelemetryAsync(object telemetryData)
        {
            var messageString = JsonSerializer.Serialize(telemetryData);
            var message = new Microsoft.Azure.Devices.Client.Message(Encoding.UTF8.GetBytes(messageString));
            await _deviceClient.SendEventAsync(message);
            Console.WriteLine("Telemetry sent: " + messageString);
        }

        public async Task UpdateReportedPropertiesAsync(object reportedProperties)
        {
            var twinCollection = new TwinCollection(JsonSerializer.Serialize(reportedProperties));
            await _deviceClient.UpdateReportedPropertiesAsync(twinCollection);
            Console.WriteLine("Reported properties updated: " + twinCollection.ToJson());
        }
    }
}

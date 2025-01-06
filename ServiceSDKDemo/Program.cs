using Microsoft.Azure.Devices;
using ServiceSDKDemo.Console;
using ServiceSDKDemo.Library;

string serviceConnectionString = "HostName=IoTProject2025.azure-devices.net;SharedAccessKeyName=iothubowner;SharedAccessKey=DBYODTeGXuh7hbFCZ56jS9nVTmWcIx8erAIoTAVjeT8=";

using var serviceClient = ServiceClient.CreateFromConnectionString(serviceConnectionString);
using var registryManager = RegistryManager.CreateFromConnectionString(serviceConnectionString);

var manager = new IoTHubManager(serviceClient, registryManager);

int input;
do
{
    FeatureSelector.PrintMenu();
    input = FeatureSelector.ReadInput();
    await FeatureSelector.Execute(input, manager);
} while (input != 0); 
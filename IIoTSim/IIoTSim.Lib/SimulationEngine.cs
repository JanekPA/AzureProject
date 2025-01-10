using IIoTSim.Desktop.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IIoTSim.Desktop
{
    internal class SimulationEngine
    {
        private readonly IEnumerable<DeviceModel> devices;
        private readonly Random random;

        public SimulationEngine(IEnumerable<DeviceModel> devices)
        {
            this.devices = devices;
            this.random = new Random();
        }

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var tmpDevices = new List<DeviceModel>(devices);

                foreach (var device in tmpDevices)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    if (device.ProductionStatus == Model.Enums.ProductionStatus.Running
                        && device.ProductionRate > 0
                        && !device.IsEmergencyStop)
                    {
                        int errorRate = device.IsPowerFailure ? device.ProductionRate / 2 : device.ProductionRate;
                        int adjRate = 100 - errorRate;

                        bool p = (random.Next(100) > adjRate) && (random.NextDouble() > 0.05);
                        device.GoodCount += p ? 1 : 0;

                        bool p2 = device.IsUnknownError ? (random.Next(100) > adjRate) && (random.NextDouble() > 0.5) : (random.Next(100) > adjRate) && (random.NextDouble() > 0.9);
                        device.BadCount += p2 ? 1 : 0;

                        device.Temperature = 60 + random.Next(errorRate / 2, errorRate) * random.NextDouble();
                    }
                    else
                    {
                        device.Temperature = 25 + random.Next(-1, 2) * random.NextDouble();
                    }

                    if (device.IsSensorFailure)
                    {
                        device.Temperature = random.Next(-1000, 1000);
                    }
                }
                await Task.Delay(200, cancellationToken);
            }
        }
    }
}
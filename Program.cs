using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Shared;
using Newtonsoft.Json;
using System;
using System.Text;
using System.Threading.Tasks;

namespace IotHubSdkDemo
{
    class Program
    {   // klucz do device 1
        private static string deviceConnectionString = "HostName=iot-IoTHubForProject.azure-devices.net;DeviceId=Device1;SharedAccessKey=iZQobC6oMBdrdBJJLJSAjRzUHNLXcDc8PmV0wkN4P0k=";

        private static DeviceClient deviceClient = null;

        private const int TEMPERATURE_THRESHOLD = 30;

        static async Task Main(string[] args)
        {
            try
            {
                deviceClient = DeviceClient.CreateFromConnectionString(deviceConnectionString, TransportType.Mqtt);
                await deviceClient.OpenAsync();

                await deviceClient.SetMethodHandlerAsync("SendMessages", SendMessagesHandler, null);
                await deviceClient.SetMethodDefaultHandlerAsync(DefaultServiceHandler, null);
                ReceiveCommands();

                await DeviceTwinDemo();
                await deviceClient.SetDesiredPropertyUpdateCallbackAsync(OnDesiredPropertyChanged, null);

                Console.WriteLine("Connection Open, press enter to send messages...");
                Console.ReadLine();

                await SendMessages(5, 2000);
            }
            catch (AggregateException ex)
            {
                foreach (Exception exception in ex.InnerExceptions)
                {
                    Console.WriteLine();
                    Console.WriteLine("Error in sample: {0}", exception);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine("Error in sample: {0}", ex.Message);
            }

            Console.WriteLine("All messages sent, press enter to exit...");
            Console.ReadLine();
            Console.WriteLine("Closing, please wait...");
            await deviceClient.CloseAsync();
        }


        private static async Task SendMessages(int nrOfMessages, int delay)
        {
            var rnd = new Random();

            Console.WriteLine("Device sending {0} messages to IoTHub...\n", nrOfMessages);

            for (int count = 0; count < nrOfMessages; count++)
            {
                var data = new
                {
                    temperature = rnd.Next(20, 35),
                    humidity = rnd.Next(60, 80),
                    msgCount = count
                };

                var dataString = JsonConvert.SerializeObject(data);

                Message eventMessage = new Message(Encoding.UTF8.GetBytes(dataString));
                eventMessage.Properties.Add("temperatureAlert", (data.temperature > TEMPERATURE_THRESHOLD) ? "true" : "false");
                Console.WriteLine($"\t{DateTime.Now.ToLocalTime()}> Sending message: {count}, Data: [{dataString}]");

                await deviceClient.SendEventAsync(eventMessage).ConfigureAwait(false);

                if (count < nrOfMessages - 1)
                    await Task.Delay(delay);
            }
            Console.WriteLine();
        }

        private static async Task<MethodResponse> SendMessagesHandler(MethodRequest methodRequest, object userContext)
        {
            var payload = JsonConvert.DeserializeAnonymousType(methodRequest.DataAsJson, new { nrOfMessages = default(int), delay = default(int) });

            await SendMessages(payload.nrOfMessages, payload.delay);

            return new MethodResponse(0);
        }

        private static async Task<MethodResponse> DefaultServiceHandler(MethodRequest methodRequest, object userContext)
        {
            Console.WriteLine("SERVICE EXECUTED: " + methodRequest.Name);

            await Task.Delay(1000);

            return new MethodResponse(0);
        }

        private static async Task ReceiveCommands()
        {
            Console.WriteLine("\nDevice waiting for commands from IoTHub...\n");

            while (true)
            {
                using (Message receivedMessage = await deviceClient.ReceiveAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false))
                {
                    if (receivedMessage != null)
                    {
                        string messageData = Encoding.ASCII.GetString(receivedMessage.GetBytes());
                        Console.WriteLine("\t{0}> Received message: {1}", DateTime.Now.ToLocalTime(), messageData);

                        int propCount = 0;
                        foreach (var prop in receivedMessage.Properties)
                        {
                            Console.WriteLine("\t\tProperty[{0}> Key={1} : Value={2}", propCount++, prop.Key, prop.Value);
                        }

                        await deviceClient.CompleteAsync(receivedMessage).ConfigureAwait(false);
                    }
                }
            }
        }

        private static async Task DeviceTwinDemo()
        {
            var twin = await deviceClient.GetTwinAsync();

            Console.WriteLine("\nInitial twin value received:");
            Console.WriteLine(JsonConvert.SerializeObject(twin, Formatting.Indented));

            var reportedProperties = new TwinCollection();
            reportedProperties["DateTimeLastAppLaunch"] = DateTime.Now;

            await deviceClient.UpdateReportedPropertiesAsync(reportedProperties);
        }

        private static async Task OnDesiredPropertyChanged(TwinCollection desiredProperties, object userContext)
        {
            Console.WriteLine("Desired property change:");
            Console.WriteLine(JsonConvert.SerializeObject(desiredProperties));

            Console.WriteLine("Sending current time as reported property");
            TwinCollection reportedProperties = new TwinCollection();
            reportedProperties["DateTimeLastDesiredPropertyChangeReceived"] = DateTime.Now;

            await deviceClient.UpdateReportedPropertiesAsync(reportedProperties).ConfigureAwait(false);
        }
    }
}

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
        private static string deviceConnectionString = "HostName=iot-projectIoTHub.azure-devices.net;DeviceId=device1;SharedAccessKey=rcQYHsMFEzkLCOfEkjUKa+tKeDdKaLRKVioWmrpHY/w=";

        private static DeviceClient deviceClient = null;
        public static int nrOfMessages = 5;
        public static int delay = 5000;
        public static int irigationStatusDefault = 0;
        public static bool AlertStatus = false;
        static async Task Main(string[] args)
        {
            try
            {
                deviceClient = DeviceClient.CreateFromConnectionString(deviceConnectionString, TransportType.Mqtt);
                await deviceClient.OpenAsync();

                await deviceClient.SetMethodHandlerAsync("SendMessages", ChangeingStatusOfIrigation, null);
                await deviceClient.SetMethodDefaultHandlerAsync(DefaultServiceHandler, null);
                await deviceClient.SetMethodDefaultHandlerAsync(ResetLastAlert, null);
                ReceiveCommands();

                await DeviceTwinDemo();
                await deviceClient.SetDesiredPropertyUpdateCallbackAsync(OnDesiredPropertyChanged, null);

                Console.WriteLine("Connection Open, press enter to send messages...");
                Console.ReadLine();

                await SendMessages(nrOfMessages, delay );
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

        private static async Task SendMessages(int nrOfMessages, int delay) // dzieje sie gdy wowłam metodę send messages bez zmiany statusu polewaczki
        {
            await SendMessages(nrOfMessages, delay, irigationStatusDefault);
        }

        private static async Task SendMessages(int irigationStatus) // dzieje sie gdy wywołam zmiane statusu polewaczki
        {
            await SendMessages(nrOfMessages, delay, irigationStatus);
        }

        private static async Task SendMessages(int nrOfMessages, int delay,  int irigationStatus)
        {
            var rnd = new Random();
            

            Console.WriteLine("Device sending {0} messages to IoTHub...\n", nrOfMessages);
            var waterPreasure = 1.0;
            if (irigationStatus == 0)//irigation off
            {
                waterPreasure = 0.1;
            }
            else if (irigationStatus == 1) // irygation on 
            {
                waterPreasure = rnd.Next(0, 11);//unit -> bar   value 11 means error-0, value 0 means error-1
            }
            else
            {
               waterPreasure = 11; // it's error-0
            }

                for (int count = 0; count < nrOfMessages; count++)
            {
                var energy = rnd.Next(0, 1); //if device using bulid-in battery pick 1 if not pick 0
                if ( waterPreasure > 10 || waterPreasure == 0 || energy == 0)
                {
                    AlertStatus = true;
                }
                var data = new
                {
                        waterPreasure = waterPreasure,//unit -> bar
                        irigationStatus = irigationStatus, // if device do irrigation chosse 1 otherwise 0
                        energy,
                        msgCount = count,
                        AlertStatus
                    };

                var dataString = JsonConvert.SerializeObject(data);


                Message eventMessage = new Message(Encoding.UTF8.GetBytes(dataString));
                eventMessage.Properties.Add("ERROR-0", (data.waterPreasure > 10) ? "true =unidentified error" : "false"); //unidentified error - do poprawy
                eventMessage.Properties.Add("ERROR-1", (data.waterPreasure == 0) ? "lack of water" : "we have water"); // lack of water
                eventMessage.Properties.Add("ERROR-2", (data.energy == 0) ? "lack of energy, we are using battery" : "we don't use battery"); // lack of energy, we useing battery
                Console.WriteLine($"\t{DateTime.Now.ToLocalTime()}> Sending message: {count}, Data: [{dataString}]");

                await deviceClient.SendEventAsync(eventMessage).ConfigureAwait(false);

                if (count < nrOfMessages - 1)
                    await Task.Delay(delay); //5 seconds
            }
            Console.WriteLine();
            irigationStatusDefault = irigationStatus;
        }

        //private static async Task<MethodResponse> SendMessagesHandler(MethodRequest methodRequest, object userContext)
        //{
        //    var payload = JsonConvert.DeserializeAnonymousType(methodRequest.DataAsJson, new { nrOfMessages = default(int), delay = default(int) });

        //    await SendMessages(payload.nrOfMessages, payload.delay);

        //    return new MethodResponse(0);
        //}
        // metoda do włączania i wyłączania irygacji
        // {  "irigationStatus" : 0 } direct metoda działa
        private static async Task<MethodResponse> ChangeingStatusOfIrigation (MethodRequest methodRequest, object userContext)
        {
            var payload = JsonConvert.DeserializeAnonymousType(methodRequest.DataAsJson, new {irigationStatus = default(int) });

            await SendMessages(payload.irigationStatus);

            return new MethodResponse(0);
        }
        private static async Task<MethodResponse> ResetLastAlert (MethodRequest methodRequest, object userContext)
        {
            Console.WriteLine("Alert reset");
            AlertStatus = false;

            await Task.Delay(1000);

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

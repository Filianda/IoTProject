using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Shared;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Text;
using System.Threading.Tasks;

namespace IotHubSdkDemo
{
    class Program
    {   // klucz do device 1
        private static string deviceConnectionString = "HostName=iot-projectIoTHub.azure-devices.net;DeviceId=device2;SharedAccessKey=e31n0VSWgYRoQbklec/PwmjRmejyht54vYVFqUcefxI=";

        private static float MINIMUM_WATER_PREASURE_LEVEL = 1.0f;

        private static DeviceClient deviceClient = null;
        public static int nrOfMessages = 10000;
        public static int delay = 1000; // 1 seconds
        public static bool alertStatus = false;
        public static bool functionStatus = false;

        public static float desiredIrigation = 0; // czego chce rolnik
        public static float currentIrigation = 0; // co jest teraz
        public static float waterPreasure = 100;
        public static bool powerOn = true;


       
        static async Task Main(string[] args)
        {
            
            try
            {
                deviceClient = DeviceClient.CreateFromConnectionString(deviceConnectionString, TransportType.Mqtt);
                await deviceClient.OpenAsync();

                await deviceClient.SetMethodHandlerAsync("ChangeStatusOfIrigation", ChangeStatusOfIrigation, null);
                await deviceClient.SetMethodHandlerAsync("ResetLastAlert", ResetLastAlert, null);
               // await deviceClient.SetMethodHandlerAsync("ChangeStatusOfIrigationAlert", ChangeStatusOfIrigationAlert, null);
                await deviceClient.SetMethodDefaultHandlerAsync(DefaultServiceHandler, null);
                ReceiveCommands();

                //await DeviceTwinDemo();
                await deviceClient.SetDesiredPropertyUpdateCallbackAsync(OnDesiredPropertyChanged, null);

                mainLoop();

                Console.WriteLine("Connection Open, press enter to send messages...");
                Console.ReadLine();

                await SendMessages(nrOfMessages, delay);
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

        private static async Task mainLoop()
        {
            while (true)
            {
                // SYMULACJA ZMIAN OTOCZENIA - POCZĄTEK
                var rnd = new Random();

                if (powerOn)
                {
                    if (rnd.Next(0, 100) >= 90) // 10 % chance for power blackout
                    {
                        powerOn = false;
                    }
                }
                else
                {
                    if (rnd.Next(0, 100) >= 75) // 25% change for power restore after blackout
                    {
                        powerOn = true;
                    }
                }
                // SYMULACJA ZMIAN OTOCZENIA - KONIEC

                float differenceValue = desiredIrigation - currentIrigation;
                if (waterPreasure - MINIMUM_WATER_PREASURE_LEVEL > differenceValue)
                {
                    currentIrigation = currentIrigation + differenceValue;
                    waterPreasure = waterPreasure - differenceValue;
                }
                else
                {
                    currentIrigation = currentIrigation + waterPreasure - MINIMUM_WATER_PREASURE_LEVEL;
                    waterPreasure = MINIMUM_WATER_PREASURE_LEVEL;
                }

                TwinCollection reportedProperties = new TwinCollection();
                reportedProperties["current_irrigation_power"] = currentIrigation;
                reportedProperties["alert_status"] = alertStatus;
                await deviceClient.UpdateReportedPropertiesAsync(reportedProperties).ConfigureAwait(false);

                await Task.Delay(1000);
            }
        }

        private static async Task SendMessages(int nrOfMessages, int delay)
        {
            Console.WriteLine("Device sending {0} messages to IoTHub...\n", nrOfMessages);

            for (int count = 0; count < nrOfMessages; count++)
            {
                if (!powerOn || waterPreasure == 0)
                {
                    alertStatus = true;
                    functionStatus = true;
                }

                var data = new
                {
                    waterPreasure = waterPreasure,
                    currentIrigation = currentIrigation,
                    energy = powerOn,
                    msgCount = count,
                    alertStatus = alertStatus,
                    functionStatus = functionStatus
                };

                var dataString = JsonConvert.SerializeObject(data);

                Message eventMessage = new Message(Encoding.UTF8.GetBytes(dataString));
                eventMessage.Properties.Add("ERROR-0", (data.waterPreasure > 100) ? "unidentified error" : "false"); //unidentified error - do poprawy
                eventMessage.Properties.Add("ERROR-1", (data.waterPreasure == 0) ? "lack of water" : "we have water"); // lack of water
                eventMessage.Properties.Add("ERROR-2", (data.energy) ? "we don't use battery" : "lack of energy, we are using battery");
                Console.WriteLine($"\t{DateTime.Now.ToLocalTime()}> Sending message: {count}, Data: [{dataString}]");

                await deviceClient.SendEventAsync(eventMessage).ConfigureAwait(false);

                if (count < nrOfMessages - 1)
                    await Task.Delay(delay);
            }
            Console.WriteLine();
        }

        //private static async Task<MethodResponse> SendMessagesHandler(MethodRequest methodRequest, object userContext)
        //{
        //    var payload = JsonConvert.DeserializeAnonymousType(methodRequest.DataAsJson, new { nrOfMessages = default(int), delay = default(int) });

        //    await SendMessages(payload.nrOfMessages, payload.delay);

        //    return new MethodResponse(0);
        //}
        // metoda do włączania i wyłączania irygacji
        // {  "irigationStatus" : 0 } direct metoda działa
        private static async Task<MethodResponse> ChangeStatusOfIrigation(MethodRequest methodRequest, object userContext)
        {
            var payload = JsonConvert.DeserializeAnonymousType(methodRequest.DataAsJson, new { irigationStatus = default(int) });
            if (payload == null) {
                desiredIrigation = 0;
                functionStatus = false;
                Console.WriteLine("Irigation Stop");
            }
            else
            {
                desiredIrigation = payload.irigationStatus;
            }

            return new MethodResponse(0);
        }

         private static async Task<MethodResponse> ResetLastAlert(MethodRequest methodRequest, object userContext)
        {
            Console.WriteLine("Alert reset");
            alertStatus = false;
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

        //private static async Task DeviceTwinDemo()
        //{
        //    var twin = await deviceClient.GetTwinAsync();

        //    Console.WriteLine("\nInitial twin value received:");
        //    Console.WriteLine(JsonConvert.SerializeObject(twin, Formatting.Indented));

        //    var reportedProperties = new TwinCollection();
        //    reportedProperties["DateTimeLastAppLaunch"] = DateTime.Now;
        //    reportedProperties["current_irrigation_power"] = irigationStatusDefault;
        //    reportedProperties["alert_status"] = AlertStatus;

        //    var variable = twin.Properties.Desired.ToJson(Formatting.Indented);
        //    dynamic data = JObject.Parse(variable);
        //    Console.WriteLine(data.current_irrigation_power.ToObject(typeof(int)));
        //    irigationStatusDefault = data.current_irrigation_power.ToObject(typeof(int));
        //    Console.WriteLine(data.alert_status.ToObject(typeof(bool)));
        //    AlertStatus = data.alert_status.ToObject(typeof(bool));

        //    await deviceClient.UpdateReportedPropertiesAsync(reportedProperties);
        //}


        private static async Task OnDesiredPropertyChanged(TwinCollection desiredProperties, object userContext)
        {
            Console.WriteLine("Desired property change:");
            Console.WriteLine(JsonConvert.SerializeObject(desiredProperties));

            desiredIrigation = desiredProperties["current_irrigation_power"];

            Console.WriteLine("Sending current time as reported property");
            TwinCollection reportedProperties = new TwinCollection();
            reportedProperties["DateTimeLastDesiredPropertyChangeReceived"] = DateTime.Now;

            await deviceClient.UpdateReportedPropertiesAsync(reportedProperties).ConfigureAwait(false);
        }
    }
}
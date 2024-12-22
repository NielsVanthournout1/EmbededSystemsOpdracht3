using System.Device.Gpio;
using System.Device.I2c;
using Iot.Device.Bmxx80.ReadResult;
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Shared;
using System.Text;

namespace CheeseCaveDotnet;

class Device
{
    private static readonly int s_alarmPin = 23; // Alarm output GPIO pin & input pin website
    private static GpioController s_gpio;
    private static I2cDevice s_i2cDevice;

    const double DesiredTempLimit = 20;
    const int IntervalInMilliseconds = 5000;

    private static DeviceClient s_deviceClient;
    private static bool s_alarmFlag = false;

    private static readonly string s_deviceConnectionString = "HostName=IOThubNielsVatnhournout.azure-devices.net;DeviceId=rpi-niels;SharedAccessKey=2IxU1dayt6helHgU8L/m4treOa9yQOmh3i7SWaandI4=";

    enum stateEnum
    {
        off,
        on,
        failed
    }

    private static void Main(string[] args)
    {
        s_gpio = new GpioController();
        s_gpio.OpenPin(s_alarmPin, PinMode.Output);

        var i2cSettings = new I2cConnectionSettings(1, 0x48);
        s_i2cDevice = I2cDevice.Create(i2cSettings);

        ColorMessage("Cheese Cave device app.\n", ConsoleColor.Yellow);

        s_deviceClient = DeviceClient.CreateFromConnectionString(s_deviceConnectionString, TransportType.Mqtt);
        Console.WriteLine("Device connected to IoT Hub");

        s_deviceClient.SetMethodHandlerAsync("alarmreset", ResetAlarm, null).Wait();

        MonitorConditionsAndUpdateTwinAsync();
        ReceiveC2dAsync();

        Console.ReadLine();
        s_gpio.ClosePin(s_alarmPin);
    }

    private static async void ReceiveC2dAsync()
    {
        Console.WriteLine("\nReceiving cloud to device messages from service");
        while (true)
        {
            Message receivedMessage = await s_deviceClient.ReceiveAsync();
            if (receivedMessage == null) continue;

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Received message: {0}",
            Encoding.ASCII.GetString(receivedMessage.GetBytes()));
            Console.ResetColor();

            await s_deviceClient.CompleteAsync(receivedMessage);
        }
    }

    private static async void MonitorConditionsAndUpdateTwinAsync()
    {
        while (true)
        {
            byte temp = s_i2cDevice.ReadByte();
            bool alarmTriggered = temp > DesiredTempLimit;

            s_alarmFlag = alarmTriggered;
            s_gpio.Write(s_alarmPin, alarmTriggered ? PinValue.High : PinValue.Low);

            await UpdateTwin(temp);
            await SendTemperatureTelemetryAsync(temp);

            await Task.Delay(IntervalInMilliseconds);
        }
    }

    private static Task<MethodResponse> ResetAlarm(MethodRequest methodRequest, object userContext)
    {
        s_alarmFlag = false;
        s_gpio.Write(s_alarmPin, PinValue.Low);

        string result = "{\"result\":\"Alarm reset successful\"}";
        GreenMessage(result);
        return Task.FromResult(new MethodResponse(Encoding.UTF8.GetBytes(result), 200));
    }

    private static async Task UpdateTwin(double currentTemperature)
    {
        var reportedProperties = new TwinCollection
        {
            ["temperature"] = Math.Round(currentTemperature, 2),
            ["alarmFlag"] = s_alarmFlag
        };
        await s_deviceClient.UpdateReportedPropertiesAsync(reportedProperties);

        GreenMessage("Twin state reported: " + reportedProperties.ToJson());
    }

    private static async Task SendTemperatureTelemetryAsync(double currentTemperature)
    {
        const string telemetryName = "temperature";

        string telemetryPayload = $"{{ \"{telemetryName}\": {currentTemperature} }}";
        using var message = new Message(Encoding.UTF8.GetBytes(telemetryPayload))
        {
            ContentEncoding = "utf-8",
            ContentType = "application/json",
        };

        await s_deviceClient.SendEventAsync(message);
        GreenMessage($"Telemetry: Sent - {{ \"{telemetryName}\": {currentTemperature}°C }}.");
    }

    private static void ColorMessage(string text, ConsoleColor clr)
    {
        Console.ForegroundColor = clr;
        Console.WriteLine(text);
        Console.ResetColor();
    }

    private static void GreenMessage(string text) => 
        ColorMessage(text, ConsoleColor.Green);

    private static void RedMessage(string text) => 
        ColorMessage(text, ConsoleColor.Red);
}
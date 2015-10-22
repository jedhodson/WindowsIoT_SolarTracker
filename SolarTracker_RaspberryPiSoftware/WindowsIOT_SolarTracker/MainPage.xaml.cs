/*
    Windows IoT Solar Tracker with Live Data Feed - Main Program
    Released under GNU GENERAL PUBLIC LICENSE VERSION 3+

    Full project details available on Hackster.io

    Created By Jed Hodson 2015
*/

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Windows.Devices.Gpio;
using Windows.Devices.Spi;
using Windows.Devices.Enumeration;
using Windows.Devices.I2c;
using Windows.System;

namespace WindowsIOT_SolarTracker
{
    public sealed partial class MainPage : Page
    {
        //Set Variables throughout project
        //I2C Variables
        private const byte REMOTE_I2C_ADDRESS = 0x0A;
        private const double DELAY_INTERVAL = 3000;

        //ADC Variables
        enum AdcDevice { NONE, MCP3002, MCP3208 };
        /* Important! Change this to either AdcDevice.MCP3002 or AdcDevice.MCP3208 depending on which ADC you chose     */
        private AdcDevice ADC_DEVICE = AdcDevice.MCP3208;


        private const string SPI_CONTROLLER_NAME = "SPI0";  /* Friendly name for Raspberry Pi 2 SPI controller          */
        private const Int32 SPI_CHIP_SELECT_LINE = 0;       /* Line 0 maps to physical pin number 24 on the Rpi2        */
        private SpiDevice SpiADC;

        private const byte MCP3002_CONFIG = 0x68; /* 01101000 channel configuration data for the MCP3002 */
        private const byte MCP3208_CONFIG = 0x06; /* 00000110 channel configuration data for the MCP3208 */
        private int adcResolution = 0;

        private I2cDevice i2cDeviceConnection;
        private const string I2C_CONTROLLER_NAME = "I2C1";

        private double Hpos = 0;
        private double Vpos = 0;

        //Set variables for power calculations
        private const int avgSampleLength = 25;
        private double readAmps = 0;
        private int ampSampleVal = 0;
        private int avgAmps = 0;
        private double mAmpsOut = 0;
        int adcValueCH0 = 0;
        int adcValueCH1 = 0;
        int adcValueCH2 = 0;
        double readBattVolt = 0;
        double readSolarVolt = 0;
        float amps = 0;
        float totalCharge = 0;
        float averageAmps = 0;
        float ampSeconds = 0;
        float ampHoursCal = 0;
        float wattHoursCal = 0;
        int sampleVal = 0;

        private string byteConvertCatch = "ERROR: Overflow in double to byte conversion"; //Error message

        public DispatcherTimer ADCUpdateTimer { get; set; }
        public DispatcherTimer sendTrackCMD { get; set; }
        private const int TIMER_DELAY = 500; // Interval for loop
        private const int TIMER_TRACK_DELAY = 25; //Interval to send tracking (Tell arduino to follow sun pos) CMD

        DateTime beingTime = DateTime.Now;
        double timeSinceStart = 0;
        private DateTime currentDate;
        private DateTime currentTimeDate;
        private TimeSpan elapsedSpan;

        public MainPage()
        {
            this.InitializeComponent();
            Unloaded += MainPage_Unloaded;

            timeCalculations();

            outputTextBox.Text = "System: RUNNING";
            errorTextBox.Text = "No Errors";

            //Begin ADC (SPI) and I2C
            InitADC();
            InitI2C();

            //Begin main loop after system starts
            ADCUpdateTimer = new DispatcherTimer();
            ADCUpdateTimer.Interval = TimeSpan.FromMilliseconds(TIMER_DELAY);
            ADCUpdateTimer.Tick += TimerLoop;
            ADCUpdateTimer.Start();

            //Create and start loop for sending track command
            sendTrackCMD = new DispatcherTimer();
            sendTrackCMD.Interval = TimeSpan.FromMilliseconds(TIMER_TRACK_DELAY);
            sendTrackCMD.Tick += sendTrack;
            sendTrackCMD.Start();
        }

        private void MainPage_Unloaded(object sender, object args)
        {
            /* Cleanup */
            i2cDeviceConnection.Dispose();
        }


        private async void InitADC()
        {
            if (ADC_DEVICE == AdcDevice.NONE)
            {
                errorTextBox.Text = "Please change the ADC_DEVICE variable to either MCP3002 or MCP3208";
                return;
            }

            try
            {
                await InitSPI();    /* Initialize the SPI bus for communicating with the ADC      */

            }
            catch (Exception ex)
            {
                errorTextBox.Text = ex.Message;
                return;
            }

            /* Now that everything is initialized, create a timer so we read data every 500mS */
            //ADCUpdateTimer = new Timer(this.Timer_Tick, null, 0, 100);

            //Change ADC Resolution depending on ADC Device
            switch (ADC_DEVICE)
            {
                case AdcDevice.MCP3002:
                    adcResolution = 1024;
                    break;
                case AdcDevice.MCP3208:
                    adcResolution = 4096;
                    break;
            }
        }

        private async Task InitSPI()
        {
            try
            {
                var settings = new SpiConnectionSettings(SPI_CHIP_SELECT_LINE);
                settings.ClockFrequency = 500000;   /* 0.5MHz clock rate                                        */
                settings.Mode = SpiMode.Mode0;      /* The ADC expects idle-low clock polarity so we use Mode0  */

                string spiAqs = SpiDevice.GetDeviceSelector(SPI_CONTROLLER_NAME);
                var deviceInfo = await DeviceInformation.FindAllAsync(spiAqs);
                SpiADC = await SpiDevice.FromIdAsync(deviceInfo[0].Id, settings);
            }

            /* If initialization fails, display the exception and stop running */
            catch (Exception ex)
            {
                throw new Exception("SPI Initialization Failed", ex);
            }
        }

        private async void InitI2C()
        {
            try
            {
                var i2cSettings = new I2cConnectionSettings(REMOTE_I2C_ADDRESS);
                i2cSettings.BusSpeed = I2cBusSpeed.FastMode;
                string deviceSelector = I2cDevice.GetDeviceSelector(I2C_CONTROLLER_NAME);
                var i2cDeviceControllers = await DeviceInformation.FindAllAsync(deviceSelector);
                i2cDeviceConnection = await I2cDevice.FromIdAsync(i2cDeviceControllers[0].Id, i2cSettings);
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine("Exeption: {0}", e.Message);
                errorTextBox.Text = "I2C Init Failed";
                return;
            }
        }

        private void TimerLoop(object sender, object e)
        {
            //Loop code
            ReadADC_CH0();
            ReadADC_CH1();
            sampleAmpsValue();
            powerVariables();
        }

        private void sendTrack(object sender, object e)
        {
            parsei2cData(new byte[] { 0x0F });
            prefDateTime();
        }

        //CH0 - Solar   -   0 - 30 Volts
        //CH1 - Battery -   0 - 15 Volts
        //CH2 - Current -   0 - 30 Amps
        //Run and print analog data for CH0
        public void ReadADC_CH0()
        {
            adcValueCH0 = 0;
            byte[] readBuffer = new byte[3]; /* Buffer to hold read data*/
            byte[] writeBuffer = new byte[3] { 1, 0x00, 0x00 };

            /* Setup the appropriate ADC configuration byte */

            switch (ADC_DEVICE)
            {
                case AdcDevice.MCP3002:
                    writeBuffer[0] = MCP3002_CONFIG;
                    break;
                case AdcDevice.MCP3208:
                    writeBuffer[0] = MCP3208_CONFIG;
                    break;
            }

            SpiADC.TransferFullDuplex(writeBuffer, readBuffer); /* Read data from the ADC                           */
            adcValueCH0 = convertToInt(readBuffer);                /* Convert the returned bytes into an integer value */

            //Map Voltage
            readSolarVolt = adcValueCH0;
            readSolarVolt = map(readSolarVolt, 0, adcResolution, 0, 30.00);
            readSolarVolt = Math.Round(readSolarVolt, 1);  //Round double to 1 dec. pl.


            /* UI updates must be invoked on the UI thread */
            var task = this.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                //RAW Data
                String CH0DataRAW = "";
                CH0DataRAW += adcValueCH0.ToString();
                CH0_DataRaw.Text = CH0DataRAW; //Report Value to its Text Block

                //CAL Data
                String CH0DataCal = "";
                CH0DataCal += readSolarVolt.ToString();
                CH0_DataCal.Text = CH0DataCal; //Report Value to its Text Block
            });
        }
        //Run and print analog data for CH1
        public void ReadADC_CH1()
        {
            adcValueCH1 = 0;
            byte[] readBuffer = new byte[3]; /* Buffer to hold read data*/
            byte[] writeBuffer = new byte[3] { 1, 0x40, 0x00 };

            /* Setup the appropriate ADC configuration byte */

            switch (ADC_DEVICE)
            {
                case AdcDevice.MCP3002:
                    writeBuffer[0] = MCP3002_CONFIG;
                    break;
                case AdcDevice.MCP3208:
                    writeBuffer[0] = MCP3208_CONFIG;
                    break;
            }

            SpiADC.TransferFullDuplex(writeBuffer, readBuffer); /* Read data from the ADC                           */
            adcValueCH1 = convertToInt(readBuffer);                /* Convert the returned bytes into an integer value */

            //Map Voltage
            readBattVolt = adcValueCH1;
            readBattVolt = map(readBattVolt, 0, adcResolution, 0, 15.00);
            readBattVolt = Math.Round(readBattVolt, 2);  //Round double to 1 dec. pl.


            /* UI updates must be invoked on the UI thread */
            var task = this.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {

                //RAW Data
                String CH1DataRAW = "";
                CH1DataRAW += adcValueCH1.ToString();
                CH1_DataRaw.Text = CH1DataRAW; //Report Value to its Text Block

                //CAL Data
                String CH1DataCal = "";
                CH1DataCal += readBattVolt.ToString();
                CH1_DataCal.Text = CH1DataCal; //Report Value to its Text Block
            });
        }
        //Run and Print analog data for CH2
        public void ReadADC_CH2()
        {
            adcValueCH2 = 0;

            byte[] readBuffer = new byte[3]; /* Buffer to hold read data*/
            byte[] writeBuffer = new byte[3] { 1, 0x80, 0x00 };

            /* Setup the appropriate ADC configuration byte */

            switch (ADC_DEVICE)
            {
                case AdcDevice.MCP3002:
                    writeBuffer[0] = MCP3002_CONFIG;
                    break;
                case AdcDevice.MCP3208:
                    writeBuffer[0] = MCP3208_CONFIG;
                    break;
            }

            SpiADC.TransferFullDuplex(writeBuffer, readBuffer); /* Read data from the ADC                           */
            adcValueCH2 = convertToInt(readBuffer);                /* Convert the returned bytes into an integer value */

            //Map Voltage
            readAmps = adcValueCH2;
            //readVolt = map(readVolt, 0, 4096, 0, 30.00); // 30 amp board
            //readAmps = (((long)readAmps * 5000 / adcResolution) - 500) * 1000 / 133;
            //readAmps = Math.Round(readAmps, 3);  //Round double to 3 dec. pl.
        }

        private void displayCH2() // Display CH2 Data
        {
            var task = this.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                //RAW Data
                String CH2DataRAW = "";
                CH2DataRAW += adcValueCH2.ToString();
                CH2_DataRaw.Text = CH2DataRAW; //Report Value to its Text Block
                //CAL Data
                String CH2DataCal = "";
                CH2DataCal += mAmpsOut.ToString();
                CH2_DataCal.Text = CH2DataCal; //Report Value to its Text Block
            });
        }

        //Map function for mapping values
        public double map(double x, double in_min, double in_max, double out_min, double out_max)
        {
            return (x - in_min) * (out_max - out_min) / (in_max - in_min) + out_min;
        }

        /* Convert the raw ADC bytes to an integer */
        public int convertToInt(byte[] data)
        {
            int result = 0;
            switch (ADC_DEVICE)
            {
                case AdcDevice.MCP3002:
                    result = data[0] & 0x03;
                    result <<= 8;
                    result += data[1];
                    break;
                case AdcDevice.MCP3208:
                    result = data[1] & 0x0F;
                    result <<= 8;
                    result += data[2];
                    break;
            }
            return result;
        }

        private void parsei2cData(byte[] reTrans)
        {
            try
            {
                i2cDeviceConnection.Write(reTrans);
            }
            catch (Exception)
            {
                errorTextBox.Text = "ERROR: I2C Parse Failed";
            }
        }

        private void KILL_Click(object sender, RoutedEventArgs e)
        {
            parsei2cData(new byte[] { 0x00 });

            outputTextBox.Text = "Servos Killed";
        }

        private void RESUME_Click(object sender, RoutedEventArgs e)
        {
            parsei2cData(new byte[] { 0x01 });

            outputTextBox.Text = "Servos Resumed";
        }

        private void FORCEPOS_H_Click(object sender, RoutedEventArgs e)
        {
            forceHpos();

            string posUpdateH = String.Format("Servo H Pos Updated: {0}", Hpos);
            outputTextBox.Text = posUpdateH;
        }

        private void FORCEPOS_V_Click(object sender, RoutedEventArgs e)
        {
            forceVpos();

            string posUpdateV = String.Format("Servo V Pos Updated: {0}", Vpos);
            outputTextBox.Text = posUpdateV;
        }

        private void HOME_Click(object sender, RoutedEventArgs e) //Home Servo Postions TODO
        {
            parsei2cData(new byte[] { 0x02 }); //Send Data
            outputTextBox.Text = "Servos Homed. Safe to shutdown";
        }

        private void ForceHSilder_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            Hpos = Math.Round(e.NewValue, 0);
            forceHpos();
        }

        private void ForceVSilder_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            Vpos = Math.Round(e.NewValue, 0);
            forceVpos();
        }

        private void forceHpos() // Force H Position Update on Arduino (Send data)
        {
            byte toParseHpos = 0;
            try
            {
                toParseHpos = System.Convert.ToByte(Hpos);
            }
            catch (System.OverflowException)
            {
                errorTextBox.Text = byteConvertCatch;
            }

            string posUpdatedH = String.Format("Servo H Pos Updated: {0}", Hpos);
            outputTextBox.Text = posUpdatedH;

            parsei2cData(new byte[] { 0x0A, 0x0B, toParseHpos }); //Send data
        }

        private void forceVpos() // Force V Position Update on Arduino (Send data)
        {
            byte toParseVpos = 0;
            try
            {
                toParseVpos = System.Convert.ToByte(Vpos);
            }
            catch (System.OverflowException)
            {
                outputTextBox.Text = byteConvertCatch;
            }

            string posUpdatedV = String.Format("Servo V Pos Updated: {0}", Vpos);
            outputTextBox.Text = posUpdatedV;

            parsei2cData(new byte[] { 0x0A, 0x0C, toParseVpos }); //Send Data
        }

        private void sampleAmpsValue()
        {
            ampSampleVal = 0;
            for (int x = 0; x < avgSampleLength; x++) //Run X times (avgSamepleLength)
            {
                ReadADC_CH2(); //Sample Data

                ampSampleVal += adcValueCH2;
            }

            avgAmps = ampSampleVal / avgSampleLength;
            mAmpsOut = (((long)avgAmps * 5000 / adcResolution) - 500) * 1000 / 133;
            //ampsOut = Math.Round(ampsOut, 2);
            //ampsOut = map(avgAmps, 405, adcResolution, 0, 3000);

            displayCH2();
        }

        private void powerVariables()
        {
            amps = (float)mAmpsOut / 1000;
            float watts = amps * (float)readSolarVolt;
            wattsOut.Text = watts.ToString();

            sampleVal += 1;

            timeCalculations();

            totalCharge += amps;
            averageAmps = totalCharge / sampleVal;
            ampSeconds = averageAmps * (float)timeSinceStart;
            ampHoursCal = ampSeconds / 3600;

            wattHoursCal = (float)readSolarVolt * ampHoursCal;
            wattHours.Text = wattHoursCal.ToString();
        }

        private void timeCalculations()
        {
            currentDate = DateTime.Now;
            long elapsedTicks = currentDate.Ticks - beingTime.Ticks;
            elapsedSpan = new TimeSpan(elapsedTicks);
            timeSinceStart = elapsedSpan.TotalSeconds;
        }

        private void prefDateTime()
        {
            currentTimeDate = DateTime.Now.ToLocalTime();
            String dateTimeString = string.Format("Current Time: {0}:{1}:{2} {3} {4}/{5}/{6}", 
                currentTimeDate.Hour, 
                currentTimeDate.Minute,
                currentTimeDate.Second,
                currentTimeDate.DayOfWeek,
                currentTimeDate.Day,
                currentTimeDate.Month,
                currentTimeDate.Year
                );
            timeText.Text = dateTimeString;

            uptime();
        }

        private void uptime()
        {
            long days = 0;
            long hours = 0;
            long mins = 0;
            long secs = 0;

            secs = (long)timeSinceStart;
            mins = secs / 60; //convert seconds to minutes
            hours = mins / 60; //convert minutes to hours
            days = hours / 24; //convert hours to days
            secs = secs - (mins * 60); //subtract the coverted seconds to minutes in order to display 59 secs max
            mins = mins - (hours * 60); //subtract the coverted minutes to hours in order to display 59 minutes max
            hours = hours - (days * 24); //subtract the coverted hours to days in order to display 23 hours max
                                         //Create String to Display
            String uptimeData = "Uptime: ";
            if (days > 0)
            {
                uptimeData += days;
                uptimeData += "d ";
            }
            uptimeData += hours;
            uptimeData += "h ";
            uptimeData += mins;
            uptimeData += "m ";
            uptimeData += secs;
            uptimeData += "s";

            uptimeText.Text = uptimeData;
        }
    }
}

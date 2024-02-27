﻿using Microsoft.Toolkit.Uwp.Notifications;
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Security.Policy;
using System.Windows.Forms;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;
using Windows.Storage.Streams;

namespace BatteryStatus
{
    public class MyApplicationContext : ApplicationContext
    {
        private string version = "V1.2.1";
        private string releaseUrl = @"https://github.com/ozcho/BatteryStatus/releases";

        NotifyIcon notifyIcon = new NotifyIcon();
        private ContextMenuStrip contextMenu;
        private ToolStripMenuItem themeButton;
        private ToolStripMenuItem hideButton;

        private Timer timer1;

        public BluetoothLEDevice pairedDevice;
        public GattCharacteristic batteryCharacteristic;
        public Radio bluetoothRadio;

        private int lastBattery = 100;

        private bool lightMode = false;

        public MyApplicationContext()
        {
            lightMode = IsLightMode();
            notifyIcon.Icon = GetIcon(Properties.Resources.iconQ);
            notifyIcon.Text = "BatteryStatus: Looking for paired device";
            notifyIcon.Visible = true;

            contextMenu = new ContextMenuStrip();
            themeButton = new ToolStripMenuItem("Theme");
            themeButton.DropDownItems.Add("Auto", null, ThemeClicked);
            themeButton.DropDownItems.Add("Light", null, ThemeClicked);
            themeButton.DropDownItems.Add("Dark", null, ThemeClicked);
            UpdateThemeButton();
            contextMenu.Items.Add(themeButton);
            hideButton = new ToolStripMenuItem("Auto Hide", null, HideClicked);
            UpdateHideButton();
            contextMenu.Items.Add(hideButton);
            ToolStripMenuItem versionButton = new ToolStripMenuItem(version, null, new EventHandler(VersionClicked));
            contextMenu.Items.Add(versionButton);
            ToolStripMenuItem exitButton = new ToolStripMenuItem("Exit", null, new EventHandler(ExitClicked));
            contextMenu.Items.Add(exitButton);
            notifyIcon.ContextMenuStrip = contextMenu;

            var radios = Radio.GetRadiosAsync().GetResults();
            bluetoothRadio = radios.FirstOrDefault(radio => radio.Kind == RadioKind.Bluetooth);
            if (bluetoothRadio != null)
            {
                bluetoothRadio.StateChanged += BluetoothRadio_StateChanged;
            }


            FindBleController();

            timer1 = new Timer();
            timer1.Tick += new EventHandler(timer1_Tick);
            timer1.Interval = 10000;
            timer1.Start();

            try
            {
                Octokit.GitHubClient github = new Octokit.GitHubClient(new Octokit.ProductHeaderValue("BatteryStatus"));
                var all = github.Repository.Release.GetAll("ozcho", "BatteryStatus").Result.Where(x => x.Prerelease == false).ToList();
                var latest = all.OrderByDescending(x => Int32.Parse(x.TagName.Substring(1).Replace(".", ""))).FirstOrDefault();

                if (latest != null && Int32.Parse(version.Substring(1).Replace(".", "")) < Int32.Parse(latest.TagName.Substring(1).Replace(".", "")))
                {
                    if (Properties.Settings.Default.updateVersion != latest.TagName)
                    {
                        Properties.Settings.Default.updateVersion = latest.TagName;
                        Properties.Settings.Default.reminderCount = 0;
                    }

                    if (Properties.Settings.Default.reminderCount < 3)
                    {
                        Properties.Settings.Default.reminderCount++;
                        Properties.Settings.Default.Save();

                        new ToastContentBuilder()
                        .AddText("BatteryStatus")
                        .AddText("New Version Available on GitHub")
                        .AddButton(new ToastButton()
                                .SetContent("Download")
                                .SetProtocolActivation(new Uri(releaseUrl)))
                        .AddButton(new ToastButton()
                                .SetContent("Dismiss")
                                .SetDismissActivation())
                        .Show();
                    }
                }
            }
            catch { }
        }


        async private void FindBleController()
        {
            if (bluetoothRadio?.State == RadioState.On)
            {
                int count = 0;
                var devices = (await DeviceInformation.FindAllAsync())
                    .Where(x => x.IsEnabled)
                    .Where(x => x.Id.Contains("00805f9b34fb")).ToArray();
                foreach (var device in devices)
                {
                    try
                    {
                        BluetoothLEDevice bleDevice = await BluetoothLEDevice.FromIdAsync(device.Id);

                        GattDeviceService service = bleDevice.GetGattService(new Guid("0000180f-0000-1000-8000-00805f9b34fb"));
                        GattCharacteristic characteristic = service.GetCharacteristics(new Guid("00002a19-0000-1000-8000-00805f9b34fb")).First();

                        if (service != null && characteristic != null)//get the devices with battery status
                        {
                            bleDevice.ConnectionStatusChanged -= ConnectionStatusChanged;
                            bleDevice.ConnectionStatusChanged += ConnectionStatusChanged;
                            count++;
                            if (bleDevice.ConnectionStatus == BluetoothConnectionStatus.Connected)
                            {
                                ConnectDevice(bleDevice);
                            }
                        }
                       
                    }
                    catch { }
                }

                if (count == 0)
                {
                    notifyIcon.Icon = GetIcon(Properties.Resources.iconE);
                    notifyIcon.Text = "BatteryStatus: No paired device with battery service found";
                }
                else
                {
                    Update();
                }
            }
            else
            {
                notifyIcon.Icon = GetIcon(Properties.Resources.iconE);
                notifyIcon.Text = "BatteryStatus: Bluetooth is turned off";
                Update();
            }
        }

        private void BluetoothRadio_StateChanged(Radio sender, object args)
        {
            FindBleController();
        }

        private void ConnectionStatusChanged(BluetoothLEDevice sender, object args)
        {
            if (sender.ConnectionStatus == BluetoothConnectionStatus.Connected)
            {
                ConnectDevice(sender);
            }
            else if (sender == pairedDevice)
            {
                Update();
            }
        }

        public void ConnectDevice(BluetoothLEDevice device)
        {
            if (pairedDevice == null || pairedDevice.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
            {
                try
                {
                    GattDeviceService service = device.GetGattService(new Guid("0000180f-0000-1000-8000-00805f9b34fb"));
                    GattCharacteristic characteristic = service.GetCharacteristics(new Guid("00002a19-0000-1000-8000-00805f9b34fb")).First();

                    if (service != null && characteristic != null)
                    {
                        pairedDevice = device;
                        batteryCharacteristic = characteristic;
                        Update();
                    }
                }
                catch { }
            }
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            Update();
        }

        public void Update()
        {
            bool enabled = bluetoothRadio?.State == RadioState.On && pairedDevice?.ConnectionStatus == BluetoothConnectionStatus.Connected;
            notifyIcon.Visible = Properties.Settings.Default.hide ? enabled : true;
            if (enabled)
            {
                ReadBattery();
            }
        }

        private async void ReadBattery()
        {
            if (pairedDevice?.ConnectionStatus == BluetoothConnectionStatus.Connected && batteryCharacteristic != null)
            {
                GattReadResult result = await batteryCharacteristic.ReadValueAsync();

                if (result.Status == GattCommunicationStatus.Success)
                {
                    var reader = DataReader.FromBuffer(result.Value);
                    int val = reader.ReadByte();
                    string notify = val.ToString() + "% - " + pairedDevice.Name;
                    notifyIcon.Text = "BatteryStatus: " + notify;
                    Icon icon = Properties.Resources.icon100;
                    if (val < 5) icon = Properties.Resources.icon00;
                    else if (val < 15) icon = Properties.Resources.icon10;
                    else if (val < 25) icon = Properties.Resources.icon20;
                    else if (val < 35) icon = Properties.Resources.icon30;
                    else if (val < 45) icon = Properties.Resources.icon40;
                    else if (val < 55) icon = Properties.Resources.icon50;
                    else if (val < 65) icon = Properties.Resources.icon60;
                    else if (val < 75) icon = Properties.Resources.icon70;
                    else if (val < 85) icon = Properties.Resources.icon80;
                    else if (val < 95) icon = Properties.Resources.icon90;

                    notifyIcon.Icon = GetIcon(icon);

                    if ((lastBattery > 15 && val <= 15) || (lastBattery > 10 && val <= 10) || (lastBattery > 5 && val <= 5))
                    {
                        new ToastContentBuilder().AddText("Low Battery").AddText(notify)
                            .Show();
                    }
                    lastBattery = val;
                }
            }
        }

        private void ExitClicked(object sender, EventArgs e)
        {
            Application.Exit();
        }

        private void OnApplicationExit(object sender, EventArgs e)
        {
            notifyIcon.Visible = false;
        }

        private void ThemeClicked(object sender, EventArgs e)
        {
            if (sender == themeButton.DropDownItems[1]) { Properties.Settings.Default.theme = 1; }
            else if (sender == themeButton.DropDownItems[2]) { Properties.Settings.Default.theme = 2; }
            else { Properties.Settings.Default.theme = 0; }
            Properties.Settings.Default.Save();
            UpdateThemeButton();
        }

        private void UpdateThemeButton()
        {
            if (Properties.Settings.Default.theme == 1)
            {
                ((ToolStripMenuItem)themeButton.DropDownItems[0]).Checked = false;
                ((ToolStripMenuItem)themeButton.DropDownItems[1]).Checked = true;
                ((ToolStripMenuItem)themeButton.DropDownItems[2]).Checked = false;
            }
            else if (Properties.Settings.Default.theme == 2)
            {
                ((ToolStripMenuItem)themeButton.DropDownItems[0]).Checked = false;
                ((ToolStripMenuItem)themeButton.DropDownItems[1]).Checked = false;
                ((ToolStripMenuItem)themeButton.DropDownItems[2]).Checked = true;
            }
            else
            {
                ((ToolStripMenuItem)themeButton.DropDownItems[0]).Checked = true;
                ((ToolStripMenuItem)themeButton.DropDownItems[1]).Checked = false;
                ((ToolStripMenuItem)themeButton.DropDownItems[2]).Checked = false;
            }

            FindBleController();
        }

        private void HideClicked(object sender, EventArgs e)
        {
            Properties.Settings.Default.hide = !Properties.Settings.Default.hide;
            Properties.Settings.Default.Save();
            UpdateHideButton();
        }

        private void UpdateHideButton()
        {
            hideButton.Checked = Properties.Settings.Default.hide;

            Update();
        }

        public bool IsLightMode()
        {
            RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");

            if (key != null)
            {
                object registryValueObject = key.GetValue("AppsUseLightTheme");

                if (registryValueObject != null)
                {
                    int registryValue = (int)registryValueObject;
                    return registryValue == 1;
                }
            }

            return true;
        }

        public Icon GetIcon(Icon darkModeIcon)
        {
            if ((Properties.Settings.Default.theme == 0 && !lightMode) || Properties.Settings.Default.theme == 1)
            {
                return darkModeIcon;
            }
            else
            {
                using (Bitmap bitmap = darkModeIcon.ToBitmap())
                {

                    for (int y = 0; y < bitmap.Height; y++)
                    {
                        for (int x = 0; x < bitmap.Width; x++)
                        {
                            Color pixelColor = bitmap.GetPixel(x, y);
                            Color invertedColor = Color.FromArgb(pixelColor.A, 255 - pixelColor.R, 255 - pixelColor.G, 255 - pixelColor.B);
                            bitmap.SetPixel(x, y, invertedColor);
                        }
                    }

                    IntPtr Hicon = bitmap.GetHicon();
                    return Icon.FromHandle(Hicon);
                }
            }
        }

        private void VersionClicked(object sender, EventArgs e)
        {
            Process.Start(new ProcessStartInfo(releaseUrl) { UseShellExecute = true });
        }

    }
}

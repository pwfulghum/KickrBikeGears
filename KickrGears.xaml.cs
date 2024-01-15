using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows;
using System.Windows.Media;

using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

using System.Windows.Interop;

namespace KickrBikeGears
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        public MainWindow()
        {
            InitializeComponent();

            this.DataContext = this;
            Gears = "Scanning";

            Setup();
        }

        private string _gears;
        public string Gears
        {
            get
            {
                return _gears;
            }
            set
            {
                _gears = value;
                OnPropertyChanged();
            }
        }

        private uint _power;
        public uint Power
        {
            get
            {
                return _power;
            }
            set
            {
                _power = value;
                OnPropertyChanged();
            }
        }

        private bool _locked;
        public bool Locked
        {
            get
            {
                return _locked;
            }
            set
            {
                _locked = value;
                if (_locked)
                {
                    LockedString = "\uE72E";
                    tbLock.Foreground = new SolidColorBrush(Colors.Red);
                } else
                {
                    tbLock.Foreground = new SolidColorBrush(Colors.Green);
                    LockedString = "\uE785";
                }
                OnPropertyChanged();
            }
        }

        string _lockedString;
        public string LockedString
        {
            get
            {
                return _lockedString;
            }
            set
            {
                _lockedString = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;


        ConcurrentBag<ulong> _btDevices = new ConcurrentBag<ulong>();

        public enum GattServiceUuid : UInt16
        {
            None = 0,
            DeviceInformation = 0x180A,
            HeartRate = 0x180D,
            UserData = 0x181C,
            CyclingPower = 0x1818,
            FitnessMachine = 0x1826
        }

        private static Guid _kickrBikeInfoServiceUUID = Guid.ParseExact("A026EE0D-0A7D-4AB3-97FA-F1500F9FEB8B", "d");
        private static Guid _kickrBikeGearsUUID = Guid.ParseExact("A026E03A-0A7D-4AB3-97FA-F1500F9FEB8B", "d");
        private static Guid _kickrBikeButtonStatusUUID = Guid.ParseExact("A026E03C-0A7D-4AB3-97FA-F1500F9FEB8B", "d");

        private static Guid _cyclingPowerServiceUUID = GetSigUuid((ushort)0x1818);
        private static Guid _cyclingPowerMeasurementUUID = GetSigUuid((ushort)0x2A63);

        private static Guid _kickrBikeGradeServiceUUID = Guid.ParseExact("A026EE0B-0A7D-4AB3-97FA-F1500F9FEB8B", "d");
        private static Guid _kickrBikeGradeUUID = Guid.ParseExact("A026E037-0A7D-4AB3-97FA-F1500F9FEB8B", "d");

        bool _bikeFound = false;
        bool _closing = false;

        GattCharacteristic _gearsCharacteristic;
        GattCharacteristic _powerCharacteristic;
        GattCharacteristic _gradeCharacteristic;

        object synclock = new object();

        private async Task Setup()
        {
            var watcher = new BluetoothLEAdvertisementWatcher()
            {
                ScanningMode = BluetoothLEScanningMode.Active
            };

            watcher.Received += Watcher_Received;
            watcher.Start();

            int timeout = 60;

            while (timeout > 0)
            {
                await Task.Delay(1000);

                if (_bikeFound)
                {
                    break;
                }

                UpdateGears($"Scan {timeout}");

                timeout--;
            }

            watcher.Stop();

            if (!_bikeFound)
            {
                UpdateGears("No Bike");
            }
        }

        public static Guid GetSigUuid(ushort specific)
        {
            var bluetoothBaseUuid = new Guid("00000000-0000-1000-8000-00805F9B34FB");

            var bytes = bluetoothBaseUuid.ToByteArray();
            bytes[0] = (byte)(specific & 255);
            bytes[1] = (byte)(specific >> 8);

            return new Guid(bytes);
        }


        private async void Watcher_Received(
            BluetoothLEAdvertisementWatcher sender,
            BluetoothLEAdvertisementReceivedEventArgs args)
        {
            ulong btAddress = args.BluetoothAddress;
            string localName = args.Advertisement.LocalName;

            try
            {
                if (localName.Contains("KICKR", StringComparison.InvariantCultureIgnoreCase))
                {
                    bool found = false;

                    lock (synclock)
                    {
                        found = _btDevices.Contains(btAddress);
                        if (!found)
                        {
                            _btDevices.Add(btAddress);
                        }
                    }

                    if (!found)
                    {
                        Debug.WriteLine($"Discovering: {localName} - {btAddress}");

                        BluetoothLEDevice device = await BluetoothLEDevice.FromBluetoothAddressAsync(btAddress);

                        if (device != null)
                        {
                            //
                            // https://stackoverflow.com/questions/35420940/windows-uwp-connect-to-ble-device-after-discovery
                            //

                            GattDeviceServicesResult gattDeviceServicesResult = await device.GetGattServicesAsync();

                            if ((gattDeviceServicesResult != null) && (gattDeviceServicesResult.Status == GattCommunicationStatus.Success))
                            {
                                var service = gattDeviceServicesResult.Services.FirstOrDefault(e => e.Uuid == _kickrBikeInfoServiceUUID);

                                if (service != null)
                                {
                                    Debug.WriteLine($"Service Sharing: {service.SharingMode}");

                                    await service.OpenAsync(GattSharingMode.SharedReadAndWrite);

                                    Debug.WriteLine($"Service Sharing: {service.SharingMode}");

                                    _bikeFound = true;

                                    GattCharacteristicsResult gcr = await service.GetCharacteristicsForUuidAsync(_kickrBikeGearsUUID);

                                    if ((gcr != null) && (gcr.Characteristics.Count > 0) && (gcr.Status == GattCommunicationStatus.Success))
                                    {
                                        _gearsCharacteristic = gcr.Characteristics.First();

                                        if (_gearsCharacteristic != null)
                                        {
                                            if (_gearsCharacteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Read))
                                            {
                                                var val = await _gearsCharacteristic.ReadValueAsync();
                                                if (val.Status == GattCommunicationStatus.Success)
                                                {
                                                    ParseGearData(val.Value.ToArray());
                                                }
                                            }
                                            if (_gearsCharacteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify))
                                            {
                                                _gearsCharacteristic.ValueChanged += GearsValueChanged;
                                                await _gearsCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify);
                                            }
                                        }
                                    }
                                    else
                                    {
                                        UpdateGears("No Access");
                                        Debug.WriteLine($"Oops - {gcr?.Status}");
                                    }
                                }


                                service = gattDeviceServicesResult.Services.FirstOrDefault(e => e.Uuid == _kickrBikeGradeServiceUUID);

                                if (service != null)
                                {
                                    Debug.WriteLine($"Service Sharing: {service.SharingMode}");

                                    await service.OpenAsync(GattSharingMode.SharedReadAndWrite);

                                    Debug.WriteLine($"Service Sharing: {service.SharingMode}");

                                    GattCharacteristicsResult gcr = await service.GetCharacteristicsForUuidAsync(_kickrBikeGradeUUID);

                                    if ((gcr != null) && (gcr.Characteristics.Count > 0) && (gcr.Status == GattCommunicationStatus.Success))
                                    {
                                        _gradeCharacteristic = gcr.Characteristics.First();

                                        if (_gradeCharacteristic != null)
                                        {
                                            if (_gradeCharacteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Read))
                                            {
                                                var val = await _gradeCharacteristic.ReadValueAsync();
                                                if (val.Status == GattCommunicationStatus.Success)
                                                {
                                                    ParseGradeData(val.Value.ToArray());
                                                }
                                            }
                                            if (_gradeCharacteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify))
                                            {
                                                _gradeCharacteristic.ValueChanged += GradeValueChanged;
                                                await _gradeCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify);
                                            }
                                        }
                                    }
                                    else
                                    {
                                        UpdateGears("No Access");
                                        Debug.WriteLine($"Oops - {gcr?.Status}");
                                    }
                                }


#if false
                                // No longer needed...

                                service = gattDeviceServicesResult.Services.FirstOrDefault(e => e.Uuid == _cyclingPowerServiceUUID);

                                if (service != null)
                                {
                                    await service.OpenAsync(GattSharingMode.SharedReadAndWrite);

                                    GattCharacteristicsResult gcr = await service.GetCharacteristicsForUuidAsync(_cyclingPowerMeasurementUUID);

                                    if ((gcr != null) && (gcr.Characteristics.Count > 0) && (gcr.Status == GattCommunicationStatus.Success))
                                    {

                                        _powerCharacteristic = gcr.Characteristics.First();

                                        if (_powerCharacteristic != null)
                                        {
                                            if (_powerCharacteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify))
                                            {
                                                _powerCharacteristic.ValueChanged += PowerMeasumrent;
                                                await _powerCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify);
                                            }
                                        }
                                    }
                                    else
                                    {
                                        Debug.WriteLine($"Oops - {gcr?.Status}");
                                    }
                                }
#endif 
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Exception: {ex}");
            }
        }

        private void PowerMeasumrent(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            try
            {
                ParsePowerData(args.CharacteristicValue.ToArray());
            }
            catch (Exception ex)
            {
            }
        }

        private void ParsePowerData(byte[] data)
        {
            if (data.Length >= 4)
            {
                // flags in first 2 bytes
                // power in next 2 bytes

                ushort power = (ushort)((ushort)data[3] << 8);
                power |= (ushort)data[2];

                UpdatePower(power);
            }
        }
          
        private void ParseGearData(byte[] data)
        {
            if (data.Length > 5)
            {
                //string s = BitConverter.ToString(bytes);
                //Debug.WriteLine($"Gears: {s}");

                uint frontGear = data[2] + 1u;
                uint rearGear = data[3] + 1u;
                uint frontGearCount = data[4];
                uint rearGearCount = data[5];

                UpdateGears($"{frontGear} - {rearGear}");
            }
        }

        private void ParseGradeData(byte[] data)
        {
            if ((data.Length == 3) && (data[0] == 0xfd) && (data[1] == 0x33))
            {
                bool locked = data[2] == 0x1;
                UpdateLock(locked);
            }
        }


        private void GearsValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            try
            {
                ParseGearData(args.CharacteristicValue.ToArray());
            }
            catch (Exception ex)
            {
            }
        }

        private void GradeValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            try
            {
                ParseGradeData(args.CharacteristicValue.ToArray());
            }
            catch (Exception ex)
            {
            }
        }

        private void UpdateGears(string gearString)
        {
            if (_closing) return;

            try
            {
                Application.Current.Dispatcher.Invoke(new Action(() =>
                {
                    Gears = gearString;
                }));
            }
            catch { };
        }

        private void UpdatePower(ushort power)
        {
            if (_closing) return;

            try
            {
                Application.Current.Dispatcher.Invoke(new Action(() =>
                {
                    Power = power;
                }));
            }
            catch { };
        }

        private void UpdateLock(bool locked)
        {
            if (_closing) return;
            try
            {
                Application.Current.Dispatcher.Invoke(new Action(() =>
                {
                    Locked = locked;
                }));
            }
            catch { };
        }

        protected void OnPropertyChanged(
        [System.Runtime.CompilerServices.CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void CloseAppClick(object sender, RoutedEventArgs e)
        {
            _closing = true;
            App.Current.Shutdown();
        }


        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            try
            {
                // Load window placement details for previous application session from application settings
                // Note - if window was closed on a monitor that is now disconnected from the computer,
                //        SetWindowPlacement will place the window onto a visible monitor.
                var wp = Settings.Default.WindowPlacement;
                wp.length = Marshal.SizeOf(typeof(WindowPlacement));
                wp.flags = 0;
                wp.showCmd = (wp.showCmd == SwShowminimized ? SwShownormal : wp.showCmd);
                var hwnd = new WindowInteropHelper(this).Handle;

                if ((wp.normalPosition.Top == 0) && 
                    (wp.normalPosition.Bottom == 0) &&
                    (wp.normalPosition.Left == 0) &&
                    (wp.normalPosition.Right ==0))
                {
                    // Don't place it.
                }
                else
                {
                    SetWindowPlacement(hwnd, ref wp);
                }
            }
            catch
            {
                // ignored
            }
        }

        // WARNING - Not fired when Application.SessionEnding is fired
        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);

            // Persist window placement details to application settings
            WindowPlacement wp;
            var hwnd = new WindowInteropHelper(this).Handle;
            GetWindowPlacement(hwnd, out wp);
            Settings.Default.WindowPlacement = wp;
            Settings.Default.Save();
        }

        #region Win32 API declarations to set and get window placement

        [DllImport("user32.dll")]
        private static extern bool SetWindowPlacement(IntPtr hWnd, [In] ref WindowPlacement lpwndpl);

        [DllImport("user32.dll")]
        private static extern bool GetWindowPlacement(IntPtr hWnd, out WindowPlacement lpwndpl);

        private const int SwShownormal = 1;
        private const int SwShowminimized = 2;

        #endregion
    }
}
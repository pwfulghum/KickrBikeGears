using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows;

using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

using Windows.Storage.Streams;


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
            GearsString = "Scanning";

            Setup();
        }

        public string _gearsString;
        public string GearsString
        {
            get
            {
                return _gearsString;
            }
            set
            {
                _gearsString = value;
                OnPropertyChanged();
            }
        }

        public string _powerString;
        public string PowerString
        {
            get
            {
                return _powerString;
            }
            set
            {
                _powerString = value;
                OnPropertyChanged();
            }
        }


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

        GattCharacteristic _gears;
        GattCharacteristic _cpm;
        GattCharacteristic _grade;

        object synclock = new object();

        public event PropertyChangedEventHandler? PropertyChanged;

        private async Task Setup()
        {
            var watcher = new BluetoothLEAdvertisementWatcher()
            {
                ScanningMode = BluetoothLEScanningMode.Passive
            };

            watcher.Received += Watcher_Received;
            watcher.Start();

            await Task.Delay(30000);

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
                if (localName != String.Empty)
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
                        Debug.WriteLine($"Found {localName}");

                        BluetoothLEDevice device = await BluetoothLEDevice.FromBluetoothAddressAsync(btAddress);

                        if (device != null)
                        {
                            //
                            // https://stackoverflow.com/questions/35420940/windows-uwp-connect-to-ble-device-after-discovery
                            //

                            GattDeviceServicesResult gattDeviceServicesResult = await device.GetGattServicesAsync();


                            if ((gattDeviceServicesResult != null) && (gattDeviceServicesResult.Status == GattCommunicationStatus.Success))
                            {
                                var result = await device.GetGattServicesForUuidAsync(_kickrBikeInfoServiceUUID);

                                if ((result != null) && (result.Status == GattCommunicationStatus.Success))
                                {
                                    var service = result.Services.FirstOrDefault();

                                    if (service != null)
                                    {
                                        await service.OpenAsync(GattSharingMode.SharedReadAndWrite);

                                        GattCharacteristicsResult gcr = await service.GetCharacteristicsForUuidAsync(_kickrBikeGearsUUID);

                                        if ((gcr != null) && (gcr.Characteristics.Count > 0) && (gcr.Status == GattCommunicationStatus.Success))
                                        {
                                            _gears = gcr.Characteristics.First();

                                            if (_gears != null)
                                            {
                                                if (_gears.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Read))
                                                {
                                                    var val = await _gears.ReadValueAsync();
                                                    if (val.Status == GattCommunicationStatus.Success)
                                                    {
                                                        ParseGearData(val.Value.ToArray());
                                                    }
                                                }
                                                if (_gears.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify))
                                                {
                                                    _gears.ValueChanged += GearsValueChanged;
                                                    await _gears.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify);

                                                    _bikeFound = true;
                                                }
                                            }
                                        }
                                        else
                                        {
                                            Debug.WriteLine($"Oops - {gcr?.Status}");
                                        }
                                    }
                                }
                            }

                            if ((gattDeviceServicesResult != null) && (gattDeviceServicesResult.Status == GattCommunicationStatus.Success))
                            {
                                var result = await device.GetGattServicesForUuidAsync(_cyclingPowerServiceUUID);

                                if ((result != null) && (result.Status == GattCommunicationStatus.Success))
                                {
                                    var service = result.Services.FirstOrDefault();

                                    if (service != null)
                                    {
                                        await service.OpenAsync(GattSharingMode.SharedReadAndWrite);

                                        GattCharacteristicsResult gcr = await service.GetCharacteristicsForUuidAsync(_cyclingPowerMeasurementUUID);

                                        if ((gcr != null) && (gcr.Characteristics.Count > 0) && (gcr.Status == GattCommunicationStatus.Success))
                                        {

                                            _cpm = gcr.Characteristics.First();

                                            if (_cpm != null)
                                            {
                                                if (_cpm.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify))
                                                {
                                                    _cpm.ValueChanged += PowerMeasumrent;
                                                    await _cpm.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify);
                                                }
                                            }
                                        }
                                        else
                                        {
                                            Debug.WriteLine($"Oops - {gcr?.Status}");
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{ex}");
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

        private void UpdateGears(string message)
        {
            Application.Current.Dispatcher.Invoke(new Action(() => {
                GearsString = message;
            }));
        }

        private void UpdatePower(ushort power)
        {
            Application.Current.Dispatcher.Invoke(new Action(() => {
                PowerString = power.ToString();
            }));
        }

        protected void OnPropertyChanged(
        [System.Runtime.CompilerServices.CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
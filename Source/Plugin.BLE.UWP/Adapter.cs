using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Toolkit.Uwp.Connectivity;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;

using Plugin.BLE.Abstractions;
using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.Extensions;

namespace Plugin.BLE.UWP
{
    public class Adapter : AdapterBase
    {
        private BluetoothLEHelper _bluetoothHelper;
        private BluetoothLEAdvertisementWatcher _bleWatcher;

        public Adapter(BluetoothLEHelper bluetoothHelper)
        {
            _bluetoothHelper = bluetoothHelper;
        }

        protected override Task StartScanningForDevicesNativeAsync(Guid[] serviceUuids, bool allowDuplicatesKey, CancellationToken scanCancellationToken)
        {
            var hasFilter = serviceUuids?.Any() ?? false;

            DiscoveredDevices.Clear();
            _BleWatcher = new BluetoothLEAdvertisementWatcher
            {
                ScanningMode = ScanMode.ToNative()
            };
            _prevScannedDevices = new List<ulong>();
            Trace.Message("Starting a scan for devices.");
            if (hasFilter)
            {
                //adds filter to native scanner if serviceUuids are specified
                foreach (var uuid in serviceUuids)
                {
                    _bleWatcher.AdvertisementFilter.Advertisement.ServiceUuids.Add(uuid);
                }

                Trace.Message($"ScanFilters: {string.Join(", ", serviceUuids)}");
            }

            _bleWatcher.Received -= DeviceFoundAsync;
            _bleWatcher.Received += DeviceFoundAsync;

            _bleWatcher.Start();
            return Task.FromResult(true);
        }

        protected override void StopScanNative()
        {
            if (_bleWatcher != null)
            {
                Trace.Message("Stopping the scan for devices");
                _bleWatcher.Stop();
                _bleWatcher = null;
            }
        }

        protected override async Task ConnectToDeviceNativeAsync(IDevice device, ConnectParameters connectParameters, CancellationToken cancellationToken)
        {
            Trace.Message($"Connecting to device with ID:  {device.Id.ToString()}");

            ObservableBluetoothLEDevice nativeDevice = device.NativeDevice as ObservableBluetoothLEDevice;
            if (nativeDevice == null)
                return;

            nativeDevice.PropertyChanged += Device_PropertyChanged;

            await nativeDevice.ConnectAsync();

            var uwpDevice = (Device)device;
            if (!ConnectedDeviceRegistry.ContainsKey(uwpDevice.Id.ToString()))
                ConnectedDeviceRegistry.Add(uwpDevice.Id.ToString(), device);
        }

        private void Device_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != "IsConnected")
                return;

            ObservableBluetoothLEDevice nativeDevice = sender as ObservableBluetoothLEDevice;
            if (nativeDevice == null)
                return;

            Guid id = new Device(this, nativeDevice.BluetoothLEDevice, 0, String.Empty).Id;

            ConnectedDeviceRegistry.TryGetValue(id.ToString(), out IDevice device);
            if (device == null)
                return;

            if (nativeDevice.IsConnected)
                HandleConnectedDevice(device);
            else
                HandleDisconnectedDevice(false, device);
        }

        protected override void DisconnectDeviceNative(IDevice device)
        {
            // Windows doesn't support disconnecting, so currently just dispose of the device
            Trace.Message($"Disconnected from device with ID:  {device.Id.ToString()}");
            ConnectedDeviceRegistry.Remove(device.Id.ToString());
        }

        public override async Task<IDevice> ConnectToKnownDeviceAsync(Guid deviceGuid, ConnectParameters connectParameters = default, CancellationToken cancellationToken = default)
        {
            //convert GUID to string and take last 12 characters as MAC address
            var guidString = deviceGuid.ToString("N").Substring(20);
            var bluetoothAddress = Convert.ToUInt64(guidString, 16);
            var nativeDevice = await BluetoothLEDevice.FromBluetoothAddressAsync(bluetoothAddress);
            var knownDevice = new Device(this, nativeDevice, 0, deviceGuid);

            await ConnectToDeviceAsync(knownDevice, cancellationToken: cancellationToken);
            return knownDevice;
        }

        public override IReadOnlyList<IDevice> GetSystemConnectedOrPairedDevices(Guid[] services = null)
        {
            //currently no way to retrieve paired and connected devices on windows without using an
            //async method. 
            Trace.Message("Returning devices connected by this app only");
            return ConnectedDevices;
        }

        /// <summary>
        /// Parses a given advertisement for various stored properties
        /// Currently only parses the manufacturer specific data
        /// </summary>
        /// <param name="adv">The advertisement to parse</param>
        /// <returns>List of generic advertisement records</returns>
        public static List<AdvertisementRecord> ParseAdvertisementData(BluetoothLEAdvertisement adv)
        {
            var advList = adv.DataSections;
            return advList.Select(data => new AdvertisementRecord((AdvertisementRecordType)data.DataType, data.Data?.ToArray())).ToList();
        }

        /// <summary>
        /// Handler for devices found when duplicates are not allowed
        /// </summary>
        /// <param name="watcher">The bluetooth advertisement watcher currently being used</param>
        /// <param name="btAdv">The advertisement recieved by the watcher</param>
        private async void DeviceFoundAsync(BluetoothLEAdvertisementWatcher watcher, BluetoothLEAdvertisementReceivedEventArgs btAdv)
        {
            var deviceId = ParseDeviceId(btAdv.BluetoothAddress);

            if (DiscoveredDevicesRegistry.TryGetValue(deviceId, out var device))
            {
                Trace.Message("AdvertisdedPeripheral: {0} Id: {1}, Rssi: {2}", device.Name, device.Id, btAdv.RawSignalStrengthInDBm);
                (device as Device)?.Update(btAdv.RawSignalStrengthInDBm, ParseAdvertisementData(btAdv.Advertisement));
                this.HandleDiscoveredDevice(device);
            }
            else
            {
                var bluetoothLeDevice = await BluetoothLEDevice.FromBluetoothAddressAsync(btAdv.BluetoothAddress);
                if (bluetoothLeDevice != null) //make sure advertisement bluetooth address actually returns a device
                {
                    device = new Device(this, bluetoothLeDevice, btAdv.RawSignalStrengthInDBm, deviceId, ParseAdvertisementData(btAdv.Advertisement));
                    Trace.Message("DiscoveredPeripheral: {0} Id: {1}, Rssi: {2}", device.Name, device.Id, btAdv.RawSignalStrengthInDBm);
                    this.HandleDiscoveredDevice(device);
                }
            }
        }

        /// <summary>
        /// Method to parse the bluetooth address as a hex string to a UUID
        /// </summary>
        /// <param name="bluetoothAddress">BluetoothLEDevice native device address</param>
        /// <returns>a GUID that is padded left with 0 and the last 6 bytes are the bluetooth address</returns>
        private static Guid ParseDeviceId(ulong bluetoothAddress)
        {
            var macWithoutColons = bluetoothAddress.ToString("x");
            macWithoutColons = macWithoutColons.PadLeft(12, '0'); //ensure valid length
            var deviceGuid = new byte[16];
            Array.Clear(deviceGuid, 0, 16);
            var macBytes = Enumerable.Range(0, macWithoutColons.Length)
                .Where(x => x % 2 == 0)
                .Select(x => Convert.ToByte(macWithoutColons.Substring(x, 2), 16))
                .ToArray();
            macBytes.CopyTo(deviceGuid, 10);
            return new Guid(deviceGuid);
        }
    }
}
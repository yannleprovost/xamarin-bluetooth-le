using Windows.Devices.Bluetooth;

using Plugin.BLE.Abstractions;
using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.WPF;

namespace Plugin.BLE
{
    public class BleImplementation : BleImplementationBase
    {
        public static BluetoothCacheMode CacheModeCharacteristicRead { get; set; } = BluetoothCacheMode.Uncached;
        public static BluetoothCacheMode CacheModeDescriptorRead { get; set; } = BluetoothCacheMode.Uncached;
        public static BluetoothCacheMode CacheModeGetDescriptors { get; set; } = BluetoothCacheMode.Cached;
        public static BluetoothCacheMode CacheModeGetCharacteristics { get; set; } = BluetoothCacheMode.Cached;
        public static BluetoothCacheMode CacheModeGetServices { get; set; } = BluetoothCacheMode.Cached;


        protected override IAdapter CreateNativeAdapter()
        {
            return new Adapter();
        }

        protected override BluetoothState GetInitialStateNative()
        {
            //The only way to get the state of bluetooth through windows is by
            //getting the radios for a device. This operation is asynchronous
            //and thus cannot be called in this method. Thus, we are just
            //returning "On" as long as the BluetoothLEHelper is initialized
            return BluetoothState.On;
        }

        protected override void InitializeNative()
        {
        }
    }

}
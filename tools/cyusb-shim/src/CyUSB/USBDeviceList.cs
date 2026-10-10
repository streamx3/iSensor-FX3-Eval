// USBDeviceList — enumerates FX3 boards.
//
// The real CyUSB filters by driver binding (devices bound to CyUSB3.sys).
// libusb has no such notion, so we filter by USB product string, using the
// exact names FX3Api itself compares against:
//
//   "Cypress FX3 USB BootLoader Device"        raw FX3 bootloader
//   "Cypress FX3 USB BootProgrammer Device"
//   "Analog Devices iSensor FX3 Bootloader"    ADI second-stage bootloader
//   "Analog Devices iSensor FX3 Demonstration Platform"   application firmware
//
// Plus VID 0x04B4 (Cypress) as a fallback for boards in raw bootloader mode
// whose product string has not yet been overridden.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using CyUSB.Native;

namespace CyUSB
{
    public class USBDeviceList : IEnumerable, IDisposable
    {
        private static IntPtr _ctx = IntPtr.Zero;
        private static readonly object CtxGate = new object();

        private readonly List<CyFX3Device> _devices = new List<CyFX3Device>();

        /// <summary>Raised on device arrival. Requires PollHotplug() to be pumped.</summary>
        public event EventHandler DeviceAttached;

        /// <summary>Raised on device removal. Requires PollHotplug() to be pumped.</summary>
        public event EventHandler DeviceRemoved;

        private HashSet<string> _lastSeen = new HashSet<string>();

        /// <param name="devClass">Ignored. CyUSB uses 1 == DEVICES_CYUSB.</param>
        public USBDeviceList(byte devClass)
        {
            EnsureContext();
            Refresh();
        }

        public int Count => _devices.Count;

        public CyFX3Device this[int index] => _devices[index];

        public IEnumerator GetEnumerator() => _devices.GetEnumerator();

        private static void EnsureContext()
        {
            lock (CtxGate)
            {
                if (_ctx != IntPtr.Zero) return;
                int rc = LibUsb.Init(out _ctx);
                if (rc != LibUsb.LIBUSB_SUCCESS)
                    throw new InvalidOperationException("libusb_init failed: " + LibUsb.ErrorName(rc));
            }
        }

        private static readonly string[] KnownProducts =
        {
            "Cypress FX3 USB BootLoader Device",
            "Cypress FX3 USB BootProgrammer Device",
            "Analog Devices iSensor FX3 Bootloader",
            "Analog Devices iSensor FX3 Demonstration Platform",
        };

        private static bool IsFx3(string product, ushort vid)
        {
            if (vid == 0x04B4) return true;                       // Cypress
            if (string.IsNullOrEmpty(product)) return false;
            foreach (var p in KnownProducts)
                if (string.Equals(product, p, StringComparison.Ordinal)) return true;
            // ADI application firmware may carry a versioned suffix.
            return product.IndexOf("iSensor FX3", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Re-scan the bus. Called by the constructor and by PollHotplug().</summary>
        public void Refresh()
        {
            foreach (var d in _devices) d.Dispose();
            _devices.Clear();

            IntPtr list;
            IntPtr count = LibUsb.GetDeviceList(_ctx, out list);
            int n = (int)count;
            if (n < 0) throw new InvalidOperationException("libusb_get_device_list failed: " + LibUsb.ErrorName(n));

            try
            {
                for (int i = 0; i < n; i++)
                {
                    IntPtr dev = System.Runtime.InteropServices.Marshal.ReadIntPtr(list, i * IntPtr.Size);
                    if (LibUsb.GetDeviceDescriptor(dev, out var desc) != LibUsb.LIBUSB_SUCCESS) continue;

                    // Open briefly to read string descriptors.
                    if (LibUsb.Open(dev, out IntPtr h) != LibUsb.LIBUSB_SUCCESS) continue;

                    string product = ReadString(h, desc.iProduct);
                    string serial = ReadString(h, desc.iSerialNumber);
                    string manufacturer = ReadString(h, desc.iManufacturer);
                    LibUsb.Close(h);

                    if (!IsFx3(product, desc.idVendor)) continue;

                    int speed = LibUsb.GetDeviceSpeed(dev);
                    var fx3 = new CyFX3Device
                    {
                        Device = LibUsb.RefDevice(dev),
                        SerialNumber = serial ?? string.Empty,
                        Product = product ?? string.Empty,
                        FriendlyName = product ?? string.Empty,
                        Manufacturer = manufacturer ?? string.Empty,
                        VendorID = desc.idVendor,
                        ProductID = desc.idProduct,
                        bHighSpeed = speed == (int)UsbSpeed.High,
                        bSuperSpeed = speed >= (int)UsbSpeed.Super,
                    };
                    fx3.BuildEndpoints();
                    fx3.OpenHandle();
                    _devices.Add(fx3);
                }
            }
            finally
            {
                LibUsb.FreeDeviceList(list, 1);
            }

            UsbTrace.Event("enumerate", $"found={_devices.Count}");
        }

        /// <summary>
        /// Poll for arrivals/removals and raise the corresponding events.
        /// The real CyUSB hooks Windows device notifications; call this on a
        /// timer from the trace harness instead.
        /// </summary>
        public void PollHotplug()
        {
            var before = new HashSet<string>(_lastSeen);
            Refresh();

            var now = new HashSet<string>();
            foreach (var d in _devices) now.Add(d.SerialNumber + "|" + d.Product);

            foreach (var sn in now)
                if (!before.Contains(sn))
                    DeviceAttached?.Invoke(this, new USBEventArgs { Device = Find(sn) });

            foreach (var sn in before)
                if (!now.Contains(sn))
                    DeviceRemoved?.Invoke(this, new USBEventArgs());

            _lastSeen = now;
        }

        private CyFX3Device Find(string key)
        {
            foreach (var d in _devices)
                if (d.SerialNumber + "|" + d.Product == key) return d;
            return null;
        }

        private static string ReadString(IntPtr handle, byte index)
        {
            if (index == 0) return string.Empty;
            var buf = new byte[256];
            int rc = LibUsb.GetStringDescriptorAscii(handle, index, buf, buf.Length);
            return rc <= 0 ? string.Empty : Encoding.ASCII.GetString(buf, 0, rc);
        }

        public void Dispose()
        {
            foreach (var d in _devices) d.Dispose();
            _devices.Clear();
        }
    }
}

// libusb-1.0 P/Invoke surface. Only what the FX3 path needs.
//
// Deliberately synchronous: FX3Api drives every bulk transfer through
// FX3USB.USB.XferData, which we also replace, so the Win32 OVERLAPPED
// machinery in the original never comes into play.

using System;
using System.Runtime.InteropServices;

namespace CyUSB.Native
{
    internal static class LibUsb
    {
        // macOS resolves "libusb-1.0" via DllImport probing to libusb-1.0.dylib;
        // Linux resolves to libusb-1.0.so.0. Set LD_LIBRARY_PATH/DYLD_LIBRARY_PATH
        // if libusb lives outside the default search path.
        private const string Lib = "libusb-1.0";

        public const int LIBUSB_SUCCESS = 0;
        public const int LIBUSB_ERROR_TIMEOUT = -7;
        public const int LIBUSB_ERROR_NO_DEVICE = -4;

        // bmRequestType bits
        public const byte REQUEST_TYPE_VENDOR = 0x40;
        public const byte RECIPIENT_DEVICE = 0x00;
        public const byte ENDPOINT_IN = 0x80;
        public const byte ENDPOINT_OUT = 0x00;

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct DeviceDescriptor
        {
            public byte bLength;
            public byte bDescriptorType;
            public ushort bcdUSB;
            public byte bDeviceClass;
            public byte bDeviceSubClass;
            public byte bDeviceProtocol;
            public byte bMaxPacketSize0;
            public ushort idVendor;
            public ushort idProduct;
            public ushort bcdDevice;
            public byte iManufacturer;
            public byte iProduct;
            public byte iSerialNumber;
            public byte bNumConfigurations;
        }

        [DllImport(Lib, EntryPoint = "libusb_init", CallingConvention = CallingConvention.Cdecl)]
        public static extern int Init(out IntPtr ctx);

        [DllImport(Lib, EntryPoint = "libusb_exit", CallingConvention = CallingConvention.Cdecl)]
        public static extern void Exit(IntPtr ctx);

        [DllImport(Lib, EntryPoint = "libusb_get_device_list", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr GetDeviceList(IntPtr ctx, out IntPtr list);

        [DllImport(Lib, EntryPoint = "libusb_free_device_list", CallingConvention = CallingConvention.Cdecl)]
        public static extern void FreeDeviceList(IntPtr list, int unrefDevices);

        [DllImport(Lib, EntryPoint = "libusb_get_device_descriptor", CallingConvention = CallingConvention.Cdecl)]
        public static extern int GetDeviceDescriptor(IntPtr dev, out DeviceDescriptor desc);

        [DllImport(Lib, EntryPoint = "libusb_open", CallingConvention = CallingConvention.Cdecl)]
        public static extern int Open(IntPtr dev, out IntPtr handle);

        [DllImport(Lib, EntryPoint = "libusb_close", CallingConvention = CallingConvention.Cdecl)]
        public static extern void Close(IntPtr handle);

        [DllImport(Lib, EntryPoint = "libusb_ref_device", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr RefDevice(IntPtr dev);

        [DllImport(Lib, EntryPoint = "libusb_unref_device", CallingConvention = CallingConvention.Cdecl)]
        public static extern void UnrefDevice(IntPtr dev);

        [DllImport(Lib, EntryPoint = "libusb_get_string_descriptor_ascii", CallingConvention = CallingConvention.Cdecl)]
        public static extern int GetStringDescriptorAscii(IntPtr handle, byte index, byte[] data, int length);

        [DllImport(Lib, EntryPoint = "libusb_claim_interface", CallingConvention = CallingConvention.Cdecl)]
        public static extern int ClaimInterface(IntPtr handle, int interfaceNumber);

        [DllImport(Lib, EntryPoint = "libusb_release_interface", CallingConvention = CallingConvention.Cdecl)]
        public static extern int ReleaseInterface(IntPtr handle, int interfaceNumber);

        [DllImport(Lib, EntryPoint = "libusb_set_configuration", CallingConvention = CallingConvention.Cdecl)]
        public static extern int SetConfiguration(IntPtr handle, int configuration);

        [DllImport(Lib, EntryPoint = "libusb_kernel_driver_active", CallingConvention = CallingConvention.Cdecl)]
        public static extern int KernelDriverActive(IntPtr handle, int interfaceNumber);

        [DllImport(Lib, EntryPoint = "libusb_detach_kernel_driver", CallingConvention = CallingConvention.Cdecl)]
        public static extern int DetachKernelDriver(IntPtr handle, int interfaceNumber);

        [DllImport(Lib, EntryPoint = "libusb_reset_device", CallingConvention = CallingConvention.Cdecl)]
        public static extern int ResetDevice(IntPtr handle);

        [DllImport(Lib, EntryPoint = "libusb_clear_halt", CallingConvention = CallingConvention.Cdecl)]
        public static extern int ClearHalt(IntPtr handle, byte endpoint);

        [DllImport(Lib, EntryPoint = "libusb_get_bus_number", CallingConvention = CallingConvention.Cdecl)]
        public static extern byte GetBusNumber(IntPtr dev);

        [DllImport(Lib, EntryPoint = "libusb_get_device_address", CallingConvention = CallingConvention.Cdecl)]
        public static extern byte GetDeviceAddress(IntPtr dev);

        [DllImport(Lib, EntryPoint = "libusb_get_device_speed", CallingConvention = CallingConvention.Cdecl)]
        public static extern int GetDeviceSpeed(IntPtr dev);

        [DllImport(Lib, EntryPoint = "libusb_control_transfer", CallingConvention = CallingConvention.Cdecl)]
        public static extern int ControlTransfer(IntPtr handle, byte bmRequestType, byte bRequest,
                                                 ushort wValue, ushort wIndex,
                                                 byte[] data, ushort wLength, uint timeout);

        [DllImport(Lib, EntryPoint = "libusb_bulk_transfer", CallingConvention = CallingConvention.Cdecl)]
        public static extern int BulkTransfer(IntPtr handle, byte endpoint,
                                              byte[] data, int length,
                                              out int transferred, uint timeout);

        [DllImport(Lib, EntryPoint = "libusb_error_name", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr ErrorNamePtr(int errcode);

        public static string ErrorName(int code)
        {
            IntPtr p = ErrorNamePtr(code);
            return p == IntPtr.Zero ? code.ToString() : Marshal.PtrToStringAnsi(p);
        }
    }

    // libusb_speed
    internal enum UsbSpeed
    {
        Unknown = 0,
        Low = 1,
        Full = 2,
        High = 3,
        Super = 4,
        SuperPlus = 5
    }
}

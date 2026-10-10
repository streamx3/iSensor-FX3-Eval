// Public type surface required by FX3Api.dll and FX3USB.dll.
//
// The member set here is exactly what ADI's (MIT-licensed) assemblies reference
// — derived from their metadata, not from decompiling Cypress's DLL. Anything
// CyUSB.dll exposes that FX3Api never touches is intentionally absent.

using System;

namespace CyUSB
{
    /// <summary>
    /// Firmware download target media.
    ///
    /// Values are load-bearing: FX3Api has the integers compiled in, not the names.
    /// Its call sites pass 1 for RAM (application firmware, flash programmer) and
    /// 2 for I2CE2PROM (ADI bootloader). SPIFLASH is never passed; 3 follows the
    /// C enum sequence. An earlier revision of this shim numbered these from 0,
    /// which made every application-firmware download be read as I2CE2PROM.
    /// </summary>
    public enum FX3_FWDWNLOAD_MEDIA_TYPE
    {
        RAM = 1,
        I2CE2PROM = 2,
        SPIFLASH = 3
    }

    /// <summary>
    /// Firmware download result. Only SUCCESS = 0 is load-bearing: FX3Api treats
    /// any value &gt; 0 as failure. The remaining ordering is unverified.
    /// </summary>
    public enum FX3_FWDWNLOAD_ERROR_CODE
    {
        SUCCESS = 0,
        FAILED = 1,
        INVALID_FILE = 2,
        CORRUPT_FIRMWARE_IMAGE_FILE = 3,
        INCORRECT_IMAGE_LENGTH = 4,
        INVALID_FWSIGNATURE = 5,
        DEVICE_CREATE_FAILED = 6
    }

    /// <summary>Endpoint transfer mode. Present for FX3USB compatibility only.</summary>
    public enum XMODE
    {
        BUFFERED = 0,
        DIRECT = 1
    }

    /// <summary>Raised on device arrival/removal.</summary>
    public class USBEventArgs : EventArgs
    {
        public USBDevice Device { get; internal set; }
    }

    /// <summary>Base USB device. FX3Api only ever handles CyFX3Device instances.</summary>
    public class USBDevice
    {
        public string SerialNumber { get; internal set; } = string.Empty;
        public string FriendlyName { get; internal set; } = string.Empty;
        public string Product { get; internal set; } = string.Empty;
        public string Manufacturer { get; internal set; } = string.Empty;

        public ushort VendorID { get; internal set; }
        public ushort ProductID { get; internal set; }

        public bool bHighSpeed { get; internal set; }
        public bool bSuperSpeed { get; internal set; }

        public override string ToString() => FriendlyName;
    }
}

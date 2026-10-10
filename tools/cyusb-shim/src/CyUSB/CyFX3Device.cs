// CyFX3Device — the device object FX3Api actually holds.
//
// Members FX3Api uses: SerialNumber, FriendlyName, Product, ControlEndPt,
// EndPoints, DownloadFw(path, media), Reset(), bHighSpeed, bSuperSpeed.

using System;
using System.Collections.Generic;
using System.IO;
using CyUSB.Native;

namespace CyUSB
{
    public class CyUSBDevice : USBDevice { }

    public class CyFX3Device : CyUSBDevice, IDisposable
    {
        internal IntPtr Device;   // libusb_device*
        internal IntPtr Handle;   // libusb_device_handle*
        internal int ClaimedInterface = -1;

        private CyControlEndPoint _controlEndPt;
        private CyUSBEndPoint[] _endPoints;

        internal CyFX3Device() { }

        public CyControlEndPoint ControlEndPt => _controlEndPt;

        /// <summary>
        /// Endpoint array. FX3Api scans this for addresses 0x01 / 0x81 / 0x82.
        /// Index 0 is the control endpoint, mirroring CyUSB's layout.
        /// </summary>
        public CyUSBEndPoint[] EndPoints => _endPoints;

        internal void BuildEndpoints()
        {
            _controlEndPt = new CyControlEndPoint(this);
            _endPoints = new CyUSBEndPoint[]
            {
                _controlEndPt,
                new CyUSBEndPoint(this, 0x01), // ADI_FROM_PC_ENDPOINT
                new CyUSBEndPoint(this, 0x81), // ADI_STREAMING_ENDPOINT
                new CyUSBEndPoint(this, 0x82), // ADI_TO_PC_ENDPOINT
            };
        }

        internal bool OpenHandle()
        {
            if (Handle != IntPtr.Zero) return true;

            int rc = LibUsb.Open(Device, out Handle);
            if (rc != LibUsb.LIBUSB_SUCCESS)
            {
                UsbTrace.Event("open_failed", $"sn={SerialNumber} rc={rc} ({LibUsb.ErrorName(rc)})");
                Handle = IntPtr.Zero;
                return false;
            }

            // Linux may have a kernel driver bound; macOS does not.
            try
            {
                if (LibUsb.KernelDriverActive(Handle, 0) == 1)
                    LibUsb.DetachKernelDriver(Handle, 0);
            }
            catch (EntryPointNotFoundException) { /* not available on this platform */ }

            rc = LibUsb.ClaimInterface(Handle, 0);
            if (rc == LibUsb.LIBUSB_SUCCESS) ClaimedInterface = 0;
            else UsbTrace.Event("claim_failed", $"sn={SerialNumber} rc={rc} ({LibUsb.ErrorName(rc)})");

            UsbTrace.Event("open", $"sn={SerialNumber} product=\\\"{Product}\\\" vid=0x{VendorID:X4} pid=0x{ProductID:X4}");
            return true;
        }

        internal void CloseHandle()
        {
            if (Handle == IntPtr.Zero) return;
            if (ClaimedInterface >= 0) { LibUsb.ReleaseInterface(Handle, ClaimedInterface); ClaimedInterface = -1; }
            LibUsb.Close(Handle);
            Handle = IntPtr.Zero;
            UsbTrace.Event("close", $"sn={SerialNumber}");
        }

        /// <summary>USB-level device reset. FX3Api calls this to force re-enumeration.</summary>
        public bool Reset()
        {
            if (Handle == IntPtr.Zero) return false;
            int rc = LibUsb.ResetDevice(Handle);
            UsbTrace.Event("device_reset", $"sn={SerialNumber} rc={rc}");
            return rc == LibUsb.LIBUSB_SUCCESS;
        }

        /// <summary>
        /// Download firmware to the FX3.
        ///
        /// RAM download implements the standard Cypress second-stage boot protocol:
        /// vendor request 0xA0 with the target address in wValue/wIndex, followed by
        /// a zero-length 0xA0 to the entry point. The .img format is
        /// [ 'C' 'Y' flags ] then repeating [ length(u32 words) | address(u32) | data ],
        /// terminated by a zero-length record carrying the entry point.
        ///
        /// This format is publicly documented (Cypress AN76405 / EZ-USB boot) and
        /// implemented by open tools such as fxload — no Cypress code is used here.
        ///
        /// I2CE2PROM / SPIFLASH targets are NOT implemented: the ADI flow only needs
        /// them to reflash a board's bootloader, which is out of scope for tracing.
        /// </summary>
        public FX3_FWDWNLOAD_ERROR_CODE DownloadFw(string path, FX3_FWDWNLOAD_MEDIA_TYPE media)
        {
            UsbTrace.Event("download_fw", $"sn={SerialNumber} media={media} path={path}");

            if (media != FX3_FWDWNLOAD_MEDIA_TYPE.RAM)
            {
                Console.Error.WriteLine($"[cyusb-shim] DownloadFw({media}) not implemented — " +
                                        "flash/EEPROM programming is out of scope. Program the board on Windows once.");
                return FX3_FWDWNLOAD_ERROR_CODE.FAILED;
            }

            byte[] img;
            try { img = File.ReadAllBytes(path); }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[cyusb-shim] firmware read failed: " + ex.Message);
                return FX3_FWDWNLOAD_ERROR_CODE.INVALID_FILE;
            }

            if (img.Length < 8 || img[0] != (byte)'C' || img[1] != (byte)'Y')
                return FX3_FWDWNLOAD_ERROR_CODE.INVALID_FWSIGNATURE;

            if (!OpenHandle()) return FX3_FWDWNLOAD_ERROR_CODE.DEVICE_CREATE_FAILED;

            const byte VENDOR_OUT = LibUsb.ENDPOINT_OUT | LibUsb.REQUEST_TYPE_VENDOR | LibUsb.RECIPIENT_DEVICE;
            const byte RW_INTERNAL = 0xA0;
            const int MAX_CHUNK = 4096;

            int i = 4; // skip 'C','Y', flags byte, image-type byte
            try
            {
                while (i + 8 <= img.Length)
                {
                    uint lengthWords = BitConverter.ToUInt32(img, i);
                    uint address = BitConverter.ToUInt32(img, i + 4);
                    i += 8;

                    if (lengthWords == 0)
                    {
                        // Final record: address is the program entry point.
                        int rcEntry = LibUsb.ControlTransfer(Handle, VENDOR_OUT, RW_INTERNAL,
                            (ushort)(address & 0xFFFF), (ushort)(address >> 16), new byte[0], 0, 2000);
                        UsbTrace.Event("fw_entry", $"addr=0x{address:X8} rc={rcEntry}");
                        return FX3_FWDWNLOAD_ERROR_CODE.SUCCESS;
                    }

                    int byteCount = checked((int)(lengthWords * 4));
                    if (i + byteCount > img.Length) return FX3_FWDWNLOAD_ERROR_CODE.INCORRECT_IMAGE_LENGTH;

                    int offset = 0;
                    while (offset < byteCount)
                    {
                        int chunk = Math.Min(MAX_CHUNK, byteCount - offset);
                        byte[] payload = new byte[chunk];
                        Array.Copy(img, i + offset, payload, 0, chunk);
                        uint target = (uint)(address + offset);

                        int rc = LibUsb.ControlTransfer(Handle, VENDOR_OUT, RW_INTERNAL,
                            (ushort)(target & 0xFFFF), (ushort)(target >> 16),
                            payload, (ushort)chunk, 2000);

                        if (rc != chunk)
                        {
                            UsbTrace.Event("fw_chunk_failed", $"addr=0x{target:X8} rc={rc}");
                            return FX3_FWDWNLOAD_ERROR_CODE.FAILED;
                        }
                        offset += chunk;
                    }
                    i += byteCount;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[cyusb-shim] firmware download error: " + ex.Message);
                return FX3_FWDWNLOAD_ERROR_CODE.CORRUPT_FIRMWARE_IMAGE_FILE;
            }

            return FX3_FWDWNLOAD_ERROR_CODE.CORRUPT_FIRMWARE_IMAGE_FILE;
        }

        public void Dispose()
        {
            CloseHandle();
            if (Device != IntPtr.Zero) { LibUsb.UnrefDevice(Device); Device = IntPtr.Zero; }
        }
    }
}

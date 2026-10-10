// Endpoint objects.
//
// FX3Api's usage, recovered from its metadata:
//   CyControlEndPoint : ReqCode, ReqType, Target, Value, Index, Direction, XferData(ref byte[], ref int)
//   CyUSBEndPoint     : Address, LastError, Reset()
// Bulk data movement goes through FX3USB.USB.XferData(ref buf, ref len, ref ep),
// which we also replace — so no OVERLAPPED / BeginDataXfer machinery is needed.

using System;
using CyUSB.Native;

namespace CyUSB
{
    public class CyUSBEndPoint
    {
        internal CyFX3Device Owner;

        /// <summary>Endpoint address: 0x01 (OUT), 0x81 (streaming IN), 0x82 (data IN).</summary>
        public byte Address { get; internal set; }

        /// <summary>Last libusb error code, surfaced as CyUSB's LastError.</summary>
        public int LastError { get; internal set; }

        /// <summary>Transfer timeout in ms. FX3USB reads this; default matches CyUSB.</summary>
        public uint TimeOut { get; set; } = 1000;

        public XMODE XferMode { get; set; } = XMODE.BUFFERED;

        public int MaxPktSize { get; internal set; } = 1024;

        internal CyUSBEndPoint(CyFX3Device owner, byte address)
        {
            Owner = owner;
            Address = address;
        }

        /// <summary>Clear a stalled endpoint.</summary>
        public bool Reset()
        {
            if (Owner == null || Owner.Handle == IntPtr.Zero) return false;
            int rc = LibUsb.ClearHalt(Owner.Handle, Address);
            LastError = rc;
            UsbTrace.Event("endpoint_reset", $"addr=0x{Address:X2} rc={rc}");
            return rc == LibUsb.LIBUSB_SUCCESS;
        }

        /// <summary>
        /// Synchronous bulk transfer. Called by our FX3USB shim.
        /// Returns true on success; <paramref name="len"/> receives the actual count.
        /// </summary>
        public virtual bool BulkXfer(ref byte[] buf, ref int len)
        {
            if (Owner == null || Owner.Handle == IntPtr.Zero) { LastError = LibUsb.LIBUSB_ERROR_NO_DEVICE; return false; }

            int requested = len;
            int rc = LibUsb.BulkTransfer(Owner.Handle, Address, buf, requested, out int transferred, TimeOut);
            LastError = rc;
            UsbTrace.Bulk(Address, buf, requested, transferred, TimeOut, rc);

            len = transferred;
            return rc == LibUsb.LIBUSB_SUCCESS;
        }
    }

    public class CyControlEndPoint : CyUSBEndPoint
    {
        internal CyControlEndPoint(CyFX3Device owner) : base(owner, 0x00) { }

        /// <summary>bRequest — the ADI vendor command opcode.</summary>
        public byte ReqCode { get; set; }

        /// <summary>Request type bits. FX3Api always sets 0x40 (vendor).</summary>
        public byte ReqType { get; set; } = LibUsb.REQUEST_TYPE_VENDOR;

        /// <summary>Recipient bits. FX3Api always sets 0 (device).</summary>
        public byte Target { get; set; } = LibUsb.RECIPIENT_DEVICE;

        /// <summary>Direction bits: 0x00 host-to-device, 0x80 device-to-host.</summary>
        public byte Direction { get; set; } = LibUsb.ENDPOINT_OUT;

        public ushort Value { get; set; }
        public ushort Index { get; set; }

        /// <summary>
        /// Vendor control transfer on EP0. This is the single most important
        /// method in the shim — every FX3 vendor command flows through it.
        /// </summary>
        public bool XferData(ref byte[] buf, ref int len)
        {
            if (Owner == null || Owner.Handle == IntPtr.Zero) { LastError = LibUsb.LIBUSB_ERROR_NO_DEVICE; return false; }

            // FX3Api composes the request as Direction | ReqType | Target.
            byte bmRequestType = (byte)(Direction | ReqType | Target);
            int requested = len;

            // libusb needs a buffer at least wLength long.
            byte[] data = buf;
            if (data == null) data = new byte[0];
            if (data.Length < requested)
            {
                data = new byte[requested];
                Array.Copy(buf ?? new byte[0], data, Math.Min(buf?.Length ?? 0, requested));
            }

            int rc = LibUsb.ControlTransfer(Owner.Handle, bmRequestType, ReqCode,
                                            Value, Index, data, (ushort)requested, TimeOut);

            int actual = rc >= 0 ? rc : 0;
            LastError = rc >= 0 ? LibUsb.LIBUSB_SUCCESS : rc;
            UsbTrace.Control(bmRequestType, ReqCode, Value, Index, data, requested, rc, TimeOut, rc);

            if (rc >= 0 && !ReferenceEquals(data, buf) && buf != null)
                Array.Copy(data, buf, Math.Min(buf.Length, actual));

            len = actual;
            return rc >= 0;
        }
    }
}

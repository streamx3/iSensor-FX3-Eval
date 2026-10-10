// Replacement for ADI's FX3USB.dll.
//
// The original (MIT, ADI) wraps every bulk transfer in Win32 overlapped I/O:
//   CreateEvent -> BeginDataXfer -> WaitForSingleObject -> FinishDataXfer -> CloseHandle
// with an Abort() path on WAIT_TIMEOUT (258). That machinery exists purely to get
// a cancellable transfer out of the Windows CyUSB driver.
//
// libusb_bulk_transfer is already synchronous-with-timeout, so the entire dance
// collapses to one call. Replacing this assembly (rather than emulating
// OVERLAPPED inside the CyUSB shim) is what keeps the shim small and portable.
//
// Semantics preserved: returns true only on a fully successful transfer; on
// timeout the endpoint's LastError carries the libusb code, matching how
// FX3Api reports failures.

using CyUSB;

namespace FX3USB
{
    public class USB
    {
        /// <summary>
        /// Synchronous bulk transfer. Direction is implied by the endpoint address
        /// (bit 7 set = device-to-host).
        /// </summary>
        /// <param name="buf">Data buffer; receives read data for IN endpoints.</param>
        /// <param name="len">In: bytes requested. Out: bytes actually transferred.</param>
        /// <param name="endpoint">Target endpoint.</param>
        public static bool XferData(ref byte[] buf, ref int len, ref CyUSBEndPoint endpoint)
        {
            if (endpoint == null) return false;
            return endpoint.BulkXfer(ref buf, ref len);
        }
    }
}

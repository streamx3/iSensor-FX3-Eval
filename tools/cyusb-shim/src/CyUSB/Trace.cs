// Protocol tracer. This is the whole point of the shim: every USB transfer the
// legacy ADI stack performs is recorded here as one JSON line.
//
// Output feeds tests/ as golden-file corpora for the C++ ReplayTransport.

using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace CyUSB
{
    public static class UsbTrace
    {
        private static readonly object Gate = new object();
        private static StreamWriter _writer;
        private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();

        /// <summary>Enabled by setting CYUSB_TRACE=&lt;path to .jsonl&gt;.</summary>
        public static bool Enabled { get; private set; }

        /// <summary>Set CYUSB_TRACE_HEX=0 to omit payload bytes (smaller traces).</summary>
        public static bool IncludePayload { get; private set; } = true;

        static UsbTrace()
        {
            try
            {
                string path = Environment.GetEnvironmentVariable("CYUSB_TRACE");
                if (!string.IsNullOrEmpty(path))
                {
                    string dir = Path.GetDirectoryName(Path.GetFullPath(path));
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    _writer = new StreamWriter(path, append: false) { AutoFlush = false };
                    Enabled = true;

                    // Buffered writes would otherwise be lost when the host process
                    // exits without an explicit Shutdown() — which is the normal case
                    // when driving the legacy stack from a short-lived harness.
                    AppDomain.CurrentDomain.ProcessExit += (s, e) => Shutdown();
                    AppDomain.CurrentDomain.UnhandledException += (s, e) => Flush();
                }
                IncludePayload = Environment.GetEnvironmentVariable("CYUSB_TRACE_HEX") != "0";
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[cyusb-shim] trace init failed: " + ex.Message);
                Enabled = false;
            }
        }

        public static void Control(byte bmRequestType, byte bRequest, ushort wValue, ushort wIndex,
                                   byte[] data, int requested, int actual, uint timeoutMs, int rc)
        {
            if (!Enabled) return;
            var sb = Begin("control");
            Field(sb, "bmRequestType", bmRequestType);
            Field(sb, "bRequest", bRequest);
            Field(sb, "wValue", wValue);
            Field(sb, "wIndex", wIndex);
            Field(sb, "requested", requested);
            Field(sb, "actual", actual);
            Field(sb, "timeout_ms", timeoutMs);
            Field(sb, "rc", rc);
            bool isIn = (bmRequestType & 0x80) != 0;
            sb.Append(",\"dir\":\"").Append(isIn ? "in" : "out").Append('"');
            Payload(sb, data, actual < 0 ? 0 : actual);
            End(sb);
        }

        public static void Bulk(byte endpoint, byte[] data, int requested, int transferred, uint timeoutMs, int rc)
        {
            if (!Enabled) return;
            var sb = Begin("bulk");
            Field(sb, "endpoint", endpoint);
            Field(sb, "requested", requested);
            Field(sb, "transferred", transferred);
            Field(sb, "timeout_ms", timeoutMs);
            Field(sb, "rc", rc);
            bool isIn = (endpoint & 0x80) != 0;
            sb.Append(",\"dir\":\"").Append(isIn ? "in" : "out").Append('"');
            Payload(sb, data, transferred < 0 ? 0 : transferred);
            End(sb);
        }

        public static void Event(string what, string detail)
        {
            if (!Enabled) return;
            var sb = Begin(what);
            sb.Append(",\"detail\":\"").Append(Escape(detail)).Append('"');
            End(sb);
        }

        private static StringBuilder Begin(string kind)
        {
            var sb = new StringBuilder(256);
            sb.Append("{\"t_us\":")
              .Append((Clock.ElapsedTicks * 1000000L / System.Diagnostics.Stopwatch.Frequency)
                      .ToString(CultureInfo.InvariantCulture))
              .Append(",\"kind\":\"").Append(kind).Append('"');
            return sb;
        }

        private static void Field(StringBuilder sb, string name, long v)
        {
            sb.Append(",\"").Append(name).Append("\":").Append(v.ToString(CultureInfo.InvariantCulture));
        }

        private static void Payload(StringBuilder sb, byte[] data, int len)
        {
            if (!IncludePayload || data == null || len <= 0) return;
            if (len > data.Length) len = data.Length;
            var hex = new StringBuilder(len * 2);
            for (int i = 0; i < len; i++) hex.Append(data[i].ToString("X2"));
            sb.Append(",\"data\":\"").Append(hex).Append('"');
        }

        private static void End(StringBuilder sb)
        {
            sb.Append('}');
            lock (Gate)
            {
                if (_writer == null) return;
                _writer.WriteLine(sb.ToString());
            }
        }

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
        }

        public static void Flush()
        {
            lock (Gate) { _writer?.Flush(); }
        }

        public static void Shutdown()
        {
            lock (Gate)
            {
                if (_writer == null) return;
                _writer.Flush();
                _writer.Dispose();
                _writer = null;
                Enabled = false;
            }
        }
    }
}

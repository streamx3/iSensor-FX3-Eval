// Minimal Microsoft.VisualBasic runtime.
//
// Mono's Homebrew bottle ships only a *reference* assembly for
// Microsoft.VisualBasic (no method bodies), and mono-basic is not packaged.
// Rather than build mono-basic from source, we implement the exact surface the
// ADI assemblies use — measured across FX3Api, adisInterface and adisApi:
//
//     Information.IsNothing          56 call sites
//     Operators.CompareString        24
//     ProjectData.SetProjectError    12
//     ProjectData.ClearProjectError   8
//     Conversions.ToUInteger/ToString/ToInteger   1 each
//
// Semantics follow the documented behaviour of the VB runtime (reference
// implementation is MIT-licensed at dotnet/runtime).
//
// Strong-named with Microsoft's PUBLIC key (token b03f5f7f11d50a3a) so that
// FX3Api's binding reference resolves. Public-signed only — see CyUSB.csproj.

using System;
using System.Globalization;

namespace Microsoft.VisualBasic
{
    public sealed class Information
    {
        private Information() { }

        /// <summary>VB's IsNothing: true when the reference is null.</summary>
        public static bool IsNothing(object Expression) => Expression == null;
    }
}

namespace Microsoft.VisualBasic.CompilerServices
{
    /// <summary>VB error-state helpers. The Err object is not used by the ADI code.</summary>
    public sealed class ProjectData
    {
        private ProjectData() { }

        [ThreadStatic] private static Exception _current;

        public static void SetProjectError(Exception ex) => _current = ex;

        public static void SetProjectError(Exception ex, int lErl) => _current = ex;

        public static void ClearProjectError() => _current = null;

        /// <summary>Not part of the used surface; present for completeness.</summary>
        public static Exception CreateProjectError(int hr) => new Exception("VB error " + hr);
    }

    public sealed class Operators
    {
        private Operators() { }

        /// <summary>
        /// VB string comparison. Returns &lt;0, 0 or &gt;0.
        /// Null and "" are equivalent in VB semantics.
        /// </summary>
        public static int CompareString(string Left, string Right, bool TextCompare)
        {
            if ((object)Left == (object)Right) return 0;
            if (Left == null) { if (Right.Length == 0) return 0; return -1; }
            if (Right == null) { if (Left.Length == 0) return 0; return 1; }

            int result = TextCompare
                ? CultureInfo.CurrentCulture.CompareInfo.Compare(Left, Right, CompareOptions.IgnoreCase | CompareOptions.IgnoreWidth | CompareOptions.IgnoreKanaType)
                : string.CompareOrdinal(Left, Right);

            return result < 0 ? -1 : (result > 0 ? 1 : 0);
        }
    }

    public sealed class Conversions
    {
        private Conversions() { }

        // ToString — VB emits calls to the exact overload matching the argument's
        // static type, so the full primitive set must be present even though only
        // a few are reachable at runtime.
        private static readonly CultureInfo Inv = CultureInfo.CurrentCulture;

        public static string ToString(object Value) => Value == null ? null : Convert.ToString(Value, Inv);
        public static string ToString(bool Value) => Value ? bool.TrueString : bool.FalseString;
        public static string ToString(char Value) => Value.ToString();
        public static string ToString(byte Value) => Value.ToString(Inv);
        public static string ToString(sbyte Value) => Value.ToString(Inv);
        public static string ToString(short Value) => Value.ToString(Inv);
        public static string ToString(ushort Value) => Value.ToString(Inv);
        public static string ToString(int Value) => Value.ToString(Inv);
        public static string ToString(uint Value) => Value.ToString(Inv);
        public static string ToString(long Value) => Value.ToString(Inv);
        public static string ToString(ulong Value) => Value.ToString(Inv);
        public static string ToString(float Value) => Value.ToString(Inv);
        public static string ToString(double Value) => Value.ToString(Inv);
        public static string ToString(decimal Value) => Value.ToString(Inv);
        public static string ToString(DateTime Value) => Value.ToString(Inv);
        public static string ToString(float Value, NumberFormatInfo NumberFormat) => Value.ToString(NumberFormat);
        public static string ToString(double Value, NumberFormatInfo NumberFormat) => Value.ToString(NumberFormat);
        public static string ToString(decimal Value, NumberFormatInfo NumberFormat) => Value.ToString(NumberFormat);
        public static string ToString(int Value, int FromBase) => Convert.ToString(Value, FromBase);
        public static string ToString(long Value, int FromBase) => Convert.ToString(Value, FromBase);

        public static int ToInteger(object Value) => Value == null ? 0 : Convert.ToInt32(Value, Inv);
        public static int ToInteger(string Value) => string.IsNullOrEmpty(Value) ? 0 : Convert.ToInt32(Value, Inv);

        public static uint ToUInteger(object Value) => Value == null ? 0u : Convert.ToUInt32(Value, Inv);
        public static uint ToUInteger(string Value) => string.IsNullOrEmpty(Value) ? 0u : Convert.ToUInt32(Value, Inv);

        public static bool ToBoolean(object Value) => Value != null && Convert.ToBoolean(Value, Inv);
        public static bool ToBoolean(string Value) => !string.IsNullOrEmpty(Value) && Convert.ToBoolean(Value, Inv);

        public static byte ToByte(object Value) => Value == null ? (byte)0 : Convert.ToByte(Value, Inv);
        public static sbyte ToSByte(object Value) => Value == null ? (sbyte)0 : Convert.ToSByte(Value, Inv);
        public static short ToShort(object Value) => Value == null ? (short)0 : Convert.ToInt16(Value, Inv);
        public static ushort ToUShort(object Value) => Value == null ? (ushort)0 : Convert.ToUInt16(Value, Inv);
        public static long ToLong(object Value) => Value == null ? 0L : Convert.ToInt64(Value, Inv);
        public static ulong ToULong(object Value) => Value == null ? 0UL : Convert.ToUInt64(Value, Inv);
        public static float ToSingle(object Value) => Value == null ? 0f : Convert.ToSingle(Value, Inv);
        public static double ToDouble(object Value) => Value == null ? 0d : Convert.ToDouble(Value, Inv);
        public static double ToDouble(string Value) => string.IsNullOrEmpty(Value) ? 0d : Convert.ToDouble(Value, Inv);
        public static decimal ToDecimal(object Value) => Value == null ? 0m : Convert.ToDecimal(Value, Inv);
        public static char ToChar(object Value) => Value == null ? '\0' : Convert.ToChar(Value, Inv);
        public static DateTime ToDate(object Value) => Value == null ? DateTime.MinValue : Convert.ToDateTime(Value, Inv);
    }
}

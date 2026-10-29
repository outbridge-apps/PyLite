namespace Outbridge.PyLite.Modules.Support
{
    // Hex digits for every codec, hash and repr that writes or reads them (Syntax keeps its own: it
    // depends on nothing above it).
    internal static class Hex
    {
        private const string LowerDigits = "0123456789abcdef";
        private const string UpperDigits = "0123456789ABCDEF";

        // The value of one hex digit, either case; -1 for anything else.
        internal static int Nibble(int c)
        {
            if (c >= '0' && c <= '9')
                return c - '0';
            if (c >= 'a' && c <= 'f')
                return c - 'a' + 10;
            if (c >= 'A' && c <= 'F')
                return c - 'A' + 10;
            return -1;
        }

        internal static char Digit(int v, bool upper = false)
        {
            return (upper ? UpperDigits : LowerDigits)[v];
        }

        internal static string Encode(byte[] data, bool upper = false)
        {
            string digits = upper ? UpperDigits : LowerDigits;
            var buf = new char[data.Length * 2];
            for (int i = 0; i < data.Length; i++)
            {
                buf[i * 2] = digits[data[i] >> 4];
                buf[i * 2 + 1] = digits[data[i] & 0xF];
            }
            return new string(buf);
        }

        // The same digits as ASCII bytes (binascii.hexlify, base64.b16encode).
        internal static byte[] EncodeAscii(byte[] data, bool upper = false)
        {
            string digits = upper ? UpperDigits : LowerDigits;
            var buf = new byte[data.Length * 2];
            for (int i = 0; i < data.Length; i++)
            {
                buf[i * 2] = (byte)digits[data[i] >> 4];
                buf[i * 2 + 1] = (byte)digits[data[i] & 0xF];
            }
            return buf;
        }
    }
}

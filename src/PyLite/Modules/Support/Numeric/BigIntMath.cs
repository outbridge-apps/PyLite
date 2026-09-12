using System.Numerics;

namespace Outbridge.PyLite.Modules.Support
{
    // BigInteger helpers absent from net472. GetBitLength does not exist here.
    internal static class BigIntMath
    {
        // Bit length of |n| (n==0 -> 0). Used by loghelper / factorial pre-estimation / fraction growth charging.
        public static int BitLength(BigInteger n)
        {
            if (n.IsZero)
                return 0;
            byte[] b = BigInteger.Abs(n).ToByteArray();   // little-endian, may have a trailing 0 sign byte
            int i = b.Length - 1;
            while (i > 0 && b[i] == 0)
                i--;
            int top = b[i];
            int bits = 0;
            while (top != 0)
            {
                bits++;
                top >>= 1;
            }
            return i * 8 + bits;
        }

        public static bool IsPowerOfTwo(BigInteger n)
        {
            if (n.Sign <= 0)
                return false;
            int bits = BitLength(n);
            return n == (BigInteger.One << (bits - 1));
        }
    }
}

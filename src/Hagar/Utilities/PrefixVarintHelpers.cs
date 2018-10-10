using System.Runtime.CompilerServices;

namespace Hagar.Utilities
{
    internal static class PrefixVarIntHelpers
    {
        /// <summary>
        /// Encoding prefixes.
        /// Index is the number of bytes being encoded.
        /// </summary>
        private static readonly byte[] Prefixes = { /* Invalid */ 0, 0b00000000, 0b10000000, 0b11000000, 0b11100000, 0b11110000, 0b11111000, 0b11111100, 0b11111110, 0b11111111, };
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static byte GetPrefix(int bytes)
        {
            return Prefixes[bytes];
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int CountLeadingOnes(byte x)
        {
            // TODO: use intrinsics when available and a better algorithm when not
            return CountLeadingOnes(0xFFFFFF00 | x) - 24;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int CountLeadingOnes(uint x) => CountLeadingZeros(~x);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int CountLeadingOnes(ulong x) => CountLeadingZeros(~x);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int CountSetBits(uint x)
        {
            x -= ((x >> 1) & 0x55555555);
            x = (((x >> 2) & 0x33333333) + (x & 0x33333333));
            x = (((x >> 4) + x) & 0x0f0f0f0f);
            x += (x >> 8);
            x += (x >> 16);
            return (int)x & 0x0000003f;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int CountLeadingZeros(uint x)
        {
            x |= (x >> 1);
            x |= (x >> 2);
            x |= (x >> 4);
            x |= (x >> 8);
            x |= (x >> 16);
            return 32 - CountSetBits(x);
        }

        public static int CountLeadingZeros(ulong i)
        {
            if (i == 0) return 64;
            uint n = 1;
            uint x = (uint)(i >> 32);
            if (x == 0) { n += 32; x = (uint)i; }
            if (x >> 16 == 0) { n += 16; x <<= 16; }
            if (x >> 24 == 0) { n += 8; x <<= 8; }
            if (x >> 28 == 0) { n += 4; x <<= 4; }
            if (x >> 30 == 0) { n += 2; x <<= 2; }
            n -= x >> 31;
            return (int)n;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe int CountRequiredBytes(uint x)
        {
            var a = x > 0b01111111;
            var b = x > 0b00111111_11111111;
            var c = x > 0b00011111_11111111_11111111;
            var d = x > 0b00001111_11111111_11111111_11111111;
            return 1 + *(byte*)&a + *(byte*)&b + *(byte*)&c + *(byte*)&d;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe int CountRequiredBytes(ulong x)
        {
            var a = x > 0b01111111;
            var b = x > 0b00111111_11111111;
            var c = x > 0b00011111_11111111_11111111;
            var d = x > 0b00001111_11111111_11111111_11111111;
            var e = x > 0b00000111_11111111_11111111_11111111_11111111;
            var f = x > 0b00000011_11111111_11111111_11111111_11111111_11111111;
            var g = x > 0b00000001_11111111_11111111_11111111_11111111_11111111_11111111;
            var h = x > 0b00000000_11111111_11111111_11111111_11111111_11111111_11111111_11111111;
            return 1 + *(byte*)&a + *(byte*)&b + *(byte*)&c + *(byte*)&d + *(byte*)&e + *(byte*)&f + *(byte*)&g + *(byte*)&h;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe int WriteShuntForFiveByteValues(uint x)
        {
            var d = x > 0x0FFFFFFF;
            return *(byte*)&d;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe int ReadShuntForFiveByteValues(byte x)
        {
            var d = (x & 0b11110000) == 0b11110000;
            return *((byte*)&d);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe int WriteShuntForFiveByteValues(ulong x)
        {
            var d = x > 0x0FFFFFFF_FFFFFFFF;
            return *(byte*)&d;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe int ReadShuntForNineByteValues(byte x)
        {
            var d = x == 0b11111111;
            return *((byte*)&d);
        }
    }
}

using System.Buffers;
using System.Runtime.CompilerServices;
using Hagar.Buffers;

namespace Hagar.Utilities
{
    public static class VarIntWriterExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteVarInt8<TBufferWriter>(ref this Writer<TBufferWriter> writer, sbyte value) where TBufferWriter : IBufferWriter<byte> => WriteVarUInt8(ref writer, ZigZagEncode(value));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteVarInt16<TBufferWriter>(ref this Writer<TBufferWriter> writer, short value) where TBufferWriter : IBufferWriter<byte> => WriteVarUInt16(ref writer, ZigZagEncode(value));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteVarInt32<TBufferWriter>(ref this Writer<TBufferWriter> writer, int value) where TBufferWriter : IBufferWriter<byte> => writer.WriteVarUInt32(ZigZagEncode(value));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteVarInt64<TBufferWriter>(ref this Writer<TBufferWriter> writer, long value) where TBufferWriter : IBufferWriter<byte> => writer.WriteVarUInt64(ZigZagEncode(value));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteVarUInt8<TBufferWriter>(ref this Writer<TBufferWriter> writer, byte value) where TBufferWriter : IBufferWriter<byte>
        {
            writer.WriteVarUInt32((uint)value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteVarUInt16<TBufferWriter>(ref this Writer<TBufferWriter> writer, ushort value) where TBufferWriter : IBufferWriter<byte>
        {
            writer.WriteVarUInt32((uint)value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte ZigZagEncode(sbyte value)
        {
            return (byte)((value << 1) ^ (value >> 7));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ushort ZigZagEncode(short value)
        {
            return (ushort)((value << 1) ^ (value >> 15));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint ZigZagEncode(int value)
        {
            return (uint)((value << 1) ^ (value >> 31));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong ZigZagEncode(long value)
        {
            return (ulong)((value << 1) ^ (value >> 63));
        }
    }
}
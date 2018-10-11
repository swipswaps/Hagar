using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using Hagar.Session;
using Hagar.Utilities;

namespace Hagar.Buffers
{
    public ref struct Reader
    {
        private ReadOnlySequence<byte> input;
        private ReadOnlySpan<byte> currentSpan;
        private SequencePosition nextSequencePosition;
        private int bufferPos;
        private int bufferSize;
        private long previousBuffersSize;

        public Reader(ReadOnlySequence<byte> input, SerializerSession session)
        {
            this.input = input;
            this.Session = session;
            this.nextSequencePosition = input.Start;
            this.currentSpan = input.First.Span;
            this.bufferPos = 0;
            this.bufferSize = this.currentSpan.Length;
            this.previousBuffersSize = 0;
        }

        public SerializerSession Session { get; }

        public long Position
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => this.previousBuffersSize + this.bufferPos;
        }

        public void Skip(long count)
        {
            var end = this.Position + count;
            while (this.Position < end)
            {
                if (this.Position + this.bufferSize >= end)
                {
                    this.bufferPos = (int) (end - this.previousBuffersSize);
                }
                else
                {
                    MoveNext();
                }
            }
        }

        /// <summary>
        /// Creates a new reader beginning at the specified position.
        /// </summary>
        public Reader ForkFrom(long position) => new Reader(this.input.Slice(position), this.Session);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void MoveNext()
        {
            this.previousBuffersSize += this.bufferSize;

            // If this is the first call to MoveNext then nextSequencePosition is invalid and must be moved to the second position.
            if (this.nextSequencePosition.Equals(this.input.Start)) this.input.TryGet(ref this.nextSequencePosition, out _);

            if (!this.input.TryGet(ref this.nextSequencePosition, out var memory))
            {
                this.currentSpan = memory.Span;
                ThrowInsufficientData();
            }

            this.currentSpan = memory.Span;
            this.bufferPos = 0;
            this.bufferSize = this.currentSpan.Length;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte ReadByte()
        {
            if (this.bufferPos == this.bufferSize) MoveNext();
            return currentSpan[this.bufferPos++];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ReadInt32()
        {
            return (int)ReadUInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint ReadUInt32()
        {
            const int width = 4;
            if (this.bufferPos + width > this.bufferSize) return ReadSlower(ref this);

            var result = BinaryPrimitives.ReadUInt32LittleEndian(currentSpan.Slice(this.bufferPos, width));
            this.bufferPos += width;
            return result;
            
            uint ReadSlower(ref Reader r)
            {
                uint b1 = r.ReadByte();
                uint b2 = r.ReadByte();
                uint b3 = r.ReadByte();
                uint b4 = r.ReadByte();

                return b1 | (b2 << 8) | (b3 << 16) | (b4 << 24);
            }
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long ReadInt64()
        {
            return (long)ReadUInt64();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong ReadUInt64()
        {
            const int width = 8;
            if (this.bufferPos + width > this.bufferSize) return ReadSlower(ref this);

            var result = BinaryPrimitives.ReadUInt64LittleEndian(currentSpan.Slice(this.bufferPos, width));
            this.bufferPos += width;
            return result;

            ulong ReadSlower(ref Reader r)
            {
                ulong b1 = r.ReadByte();
                ulong b2 = r.ReadByte();
                ulong b3 = r.ReadByte();
                ulong b4 = r.ReadByte();
                ulong b5 = r.ReadByte();
                ulong b6 = r.ReadByte();
                ulong b7 = r.ReadByte();
                ulong b8 = r.ReadByte();

                return b1 | (b2 << 8) | (b3 << 16) | (b4 << 24)
                       | (b5 << 32) | (b6 << 40) | (b7 << 48) | (b8 << 56);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowInsufficientData()
        {
            throw new InvalidOperationException("Insufficient data present in buffer.");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#if NETCOREAPP2_1
        public float ReadFloat() => BitConverter.Int32BitsToSingle(ReadInt32());
#else
        public float ReadFloat() => BitConverter.ToSingle(BitConverter.GetBytes(ReadInt32()), 0);
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#if NETCOREAPP2_1
        public double ReadDouble() => BitConverter.Int64BitsToDouble(ReadInt64());
#else
        public double ReadDouble() => BitConverter.ToDouble(BitConverter.GetBytes(ReadInt64()), 0);
#endif

        public decimal ReadDecimal()
        {
            var parts = new[] { ReadInt32(), ReadInt32(), ReadInt32(), ReadInt32() };
            return new decimal(parts);
        }
        
        public byte[] ReadBytes(uint count)
        {
            if (count == 0)
            {
                return Array.Empty<byte>();
            }

            var bytes = new byte[count];
            var destination = new Span<byte>(bytes);
            ReadBytes(in destination);
            return bytes;
        }
        
        public void ReadBytes(in Span<byte> destination)
        {
            if (this.bufferPos + destination.Length <= this.bufferSize)
            {
                this.currentSpan.Slice(this.bufferPos, destination.Length).CopyTo(destination);
                this.bufferPos += destination.Length;
                return;
            }

            CopySlower(in destination, ref this);

            void CopySlower(in Span<byte> d, ref Reader reader)
            {
                var dest = d;
                while (true)
                {
                    var writeSize = Math.Min(dest.Length, reader.currentSpan.Length - reader.bufferPos);
                    reader.currentSpan.Slice(reader.bufferPos, writeSize).CopyTo(dest);
                    reader.bufferPos += writeSize;
                    dest = dest.Slice(writeSize);

                    if (dest.Length == 0) break;

                    reader.MoveNext();
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryReadBytes(int length, out ReadOnlySpan<byte> bytes)
        {
            if (this.bufferPos + length <= this.bufferSize)
            {
                bytes = currentSpan.Slice(this.bufferPos, length);
                this.bufferPos += length;
                return true;
            }

            bytes = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint ReadVarUInt32()
        {
            var firstByte = this.PeekByte();
            var shunt = PrefixVarIntHelpers.ReadShuntForFiveByteValues(firstByte);
            var numBytes = 1 + PrefixVarIntHelpers.CountLeadingOnes(firstByte);

            if (this.bufferPos + shunt + 4 > this.bufferSize)
            {
                return this.ReadPrefixVarUInt32Slow(numBytes, shunt);
            }

            var span = this.currentSpan.Slice(this.bufferPos + shunt);
            var result = (BinaryPrimitives.ReadUInt32BigEndian(span) & ReadMask32[numBytes]) >> ((4 + shunt - numBytes) * 8);
            this.bufferPos += numBytes;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong ReadVarUInt64()
        {
            var firstByte = this.PeekByte();
            var shunt = PrefixVarIntHelpers.ReadShuntForNineByteValues(firstByte);
            var numBytes = 1 + PrefixVarIntHelpers.CountLeadingOnes(firstByte);

            if (this.bufferPos + shunt + 8 > this.bufferSize)
            {
                return this.ReadPrefixVarUInt64Slow(numBytes, shunt);
            }

            var span = this.currentSpan.Slice(this.bufferPos + shunt);
            var result = (BinaryPrimitives.ReadUInt64BigEndian(span) & ReadMask64[numBytes]) >> ((8 + shunt - numBytes) * 8);
            this.bufferPos += numBytes;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte PeekByte()
        {
            if (this.bufferPos == this.bufferSize) MoveNext();
            return currentSpan[this.bufferPos];
        }

        private static readonly uint[] ReadMask32 =
        {
            /* 1-based array */ 0,
            0b01111111_00000000_00000000_00000000,
            0b00111111_11111111_00000000_00000000,
            0b00011111_11111111_11111111_00000000,
            0b00001111_11111111_11111111_11111111,

            // Shunted by one byte
            0b11111111_11111111_11111111_11111111,
            0b11111111_11111111_11111111_11111111,
            0b11111111_11111111_11111111_11111111,
            0b11111111_11111111_11111111_11111111,
            0b11111111_11111111_11111111_11111111,
        };

        private static readonly ulong[] ReadMask64 =
        {
            /* 1-based array */ 0,
            0b01111111_00000000_00000000_00000000_00000000_00000000_00000000_00000000,
            0b00111111_11111111_00000000_00000000_00000000_00000000_00000000_00000000,
            0b00011111_11111111_11111111_00000000_00000000_00000000_00000000_00000000,
            0b00001111_11111111_11111111_11111111_00000000_00000000_00000000_00000000,         
            0b00000111_11111111_11111111_11111111_11111111_00000000_00000000_00000000,
            0b00000011_11111111_11111111_11111111_11111111_11111111_00000000_00000000,
            0b00000001_11111111_11111111_11111111_11111111_11111111_11111111_00000000,
            0b00000000_11111111_11111111_11111111_11111111_11111111_11111111_11111111,

            // Shunted by one byte
            0b11111111_11111111_11111111_11111111_11111111_11111111_11111111_11111111,
        };

        public uint ReadPrefixVarUInt32Slow(int numBytes, int shunt)
        {
            Span<byte> span = stackalloc byte[4];
            var readSpan = span.Slice(0, numBytes - shunt);

            var dest = readSpan;
            this.bufferPos += shunt;
            while (true)
            {
                var writeSize = Math.Min(dest.Length, this.bufferSize - this.bufferPos);
                this.currentSpan.Slice(this.bufferPos, writeSize).CopyTo(dest);
                this.bufferPos += writeSize;
                dest = dest.Slice(writeSize);

                if (dest.Length == 0) break;

                this.MoveNext();
            }

            var result = (BinaryPrimitives.ReadUInt32BigEndian(span) & ReadMask32[numBytes]) >> ((4 + shunt - numBytes) * 8);
            return result;
        }

        public ulong ReadPrefixVarUInt64Slow(int numBytes, int shunt)
        {
            Span<byte> span = stackalloc byte[8];
            var readSpan = span.Slice(0, numBytes - shunt);

            var dest = readSpan;
            this.bufferPos += shunt;
            while (true)
            {
                var writeSize = Math.Min(dest.Length, this.bufferSize - this.bufferPos);
                this.currentSpan.Slice(this.bufferPos, writeSize).CopyTo(dest);
                this.bufferPos += writeSize;
                dest = dest.Slice(writeSize);

                if (dest.Length == 0) break;

                this.MoveNext();
            }

            var result = (BinaryPrimitives.ReadUInt64BigEndian(span) & ReadMask64[numBytes]) >> ((8 + shunt - numBytes) * 8);
            return result;
        }
    }
}
using System.Collections.Generic;
using System.Linq;
using Hagar.Buffers;
using Hagar.Session;
using Hagar.TestKit;
using Hagar.Utilities;
using Xunit;

namespace Hagar.UnitTests
{
    [Trait("Category", "BVT")]
    public class VarIntTests
    {
        [Fact]
        public void CountRequiredBytesForWrite()
        {
            var steps = new[] { 0, 7, 14, 21, 28, 32 };
            for (var expected = 1; expected < steps.Length; expected++)
            {
                // Test with just a single bit set, i.e, the minimum representable
                // value for the given number of bytes
                for (var shift = steps[expected - 1]; shift < steps[expected]; shift++)
                {
                    var requiredBytes = PrefixVarIntHelpers.CountRequiredBytes32((uint)1 << shift);
                    Assert.Equal(expected, requiredBytes);
                }

                // Test again with multiple bits set, i.e, the maximum representable
                // value for the given number of bytes
                uint value = 0;
                for (var shift = steps[expected - 1]; shift < steps[expected]; shift++)
                {
                    value |= (uint)1 << shift;
                    var requiredBytes = PrefixVarIntHelpers.CountRequiredBytes32(value);
                    Assert.Equal(expected, requiredBytes);
                }
            }
        }

        [Fact]
        public void GetPrefix()
        {
            for (var bytes = 1; bytes < 9; bytes++)
            {
                var prefix = PrefixVarIntHelpers.GetPrefix(bytes);
                var ones = PrefixVarIntHelpers.CountLeadingOnes(prefix);
                Assert.Equal(bytes, ones + 1);
            }
        }

        [Fact]
        public void CountLeadingOnes()
        {
            byte[] testBits =
            {
                0b00000000,
                0b10000000,
                0b11000000,
                0b11100000,
                0b11110000,
                0b11111000,
                0b11111100,
                0b11111110,
                0b11111111,
            };

            for (var expected = 0; expected < testBits.Length; expected++)
            {
                var ones = PrefixVarIntHelpers.CountLeadingOnes(testBits[expected]);
                Assert.Equal(expected, ones);
            }
        }

        [Fact]
        void CanRoundTripVarInt32()
        {
            void Test(uint num, bool fastRead)
            {
                var buffer = new TestSingleSegmentBufferWriter(new byte[1000]);
                var session = new SerializerSession(null, null, null);
                var writer = new Writer<TestSingleSegmentBufferWriter>(buffer, session);
                writer.WriteVarUInt32(num);
                Assert.Equal(PrefixVarIntHelpers.CountRequiredBytes32(num), writer.Position);
                if (fastRead) writer.Write(0); // Extend the written amount so that there is always enough data to perform a fast read
                writer.Commit();

                session = new SerializerSession(null, null, null);
                var reader = new Reader(buffer.GetReadOnlySequence(120), session);
                var read = reader.ReadVarUInt32();

                Assert.Equal(num, read);
                Assert.Equal(PrefixVarIntHelpers.CountRequiredBytes32(num), (int)reader.Position);
            }

            foreach (var fastRead in new[] { false, true })
            {
                foreach (var num in GetTestValues())
                {
                    Test(num, fastRead);
                }
            }

            {
                var buffer = new TestSingleSegmentBufferWriter(new byte[1000]);
                var session = new SerializerSession(null, null, null);
                var writer = new Writer<TestSingleSegmentBufferWriter>(buffer, session);
                foreach (var num in GetTestValues())
                {
                    writer.WriteVarUInt32(num);
                }

                writer.Commit();
                session = new SerializerSession(null, null, null);
                var reader = new Reader(buffer.GetReadOnlySequence(120), session);
                foreach (var num in GetTestValues())
                {
                    var read = reader.ReadVarUInt32();

                    Assert.Equal(num, read);
                }

                Assert.Equal(writer.Position, reader.Position);
            }

            IEnumerable<uint> GetTestValues()
            {
                yield return 0;
                uint num = 0;
                foreach (var singleBit in new[] { false, true })
                {
                    for (var i = 0; i < 32; i++)
                    {
                        if (singleBit) num = 0;
                        num |= (uint)1 << i;
                        yield return num;
                    }
                }
            }
        }

        [Fact]
        void CanRoundTripVarInt64()
        {
            void Test(ulong num, bool fastRead)
            {
                var buffer = new TestSingleSegmentBufferWriter(new byte[1000]);
                var session = new SerializerSession(null, null, null);
                var writer = new Writer<TestSingleSegmentBufferWriter>(buffer, session);
                writer.WriteVarUInt64(num);
                Assert.Equal(PrefixVarIntHelpers.CountRequiredBytes64(num), writer.Position);
                if (fastRead) writer.Write((long)0); // Extend the written amount so that there is always enough data to perform a fast read
                writer.Commit();

                session = new SerializerSession(null, null, null);
                var reader = new Reader(buffer.GetReadOnlySequence(120), session);
                var read = reader.ReadVarUInt64();

                Assert.Equal(num, read);
                Assert.Equal(PrefixVarIntHelpers.CountRequiredBytes64(num), (int)reader.Position);
            }
            
            foreach (var fastRead in new[] { false, true })
            {
                foreach (var num in GetTestValues())
                {
                    Test(num, fastRead);
                }
            }

            {
                var buffer = new TestSingleSegmentBufferWriter(new byte[1000]);
                var session = new SerializerSession(null, null, null);
                var writer = new Writer<TestSingleSegmentBufferWriter>(buffer, session);
                foreach (var num in GetTestValues())
                {
                    writer.WriteVarUInt64(num);
                }

                writer.Commit();
                session = new SerializerSession(null, null, null);
                var reader = new Reader(buffer.GetReadOnlySequence(120), session);
                foreach (var num in GetTestValues())
                {
                    var read = reader.ReadVarUInt64();

                    Assert.Equal(num, read);
                }

                Assert.Equal(writer.Position, reader.Position);
            }

            IEnumerable<ulong> GetTestValues()
            {
                yield return 0;
                ulong num = 0;
                foreach (var singleBit in new[] { false, true })
                {
                    for (var i = 0; i < 64; i++)
                    {
                        if (singleBit) num = 0;
                        num |= (ulong)1 << i;
                        yield return num;
                    }
                }
            }
        }
    }
}

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
                    var requiredBytes = PrefixVarIntHelpers.CountRequiredBytes((uint)1 << shift);
                    Assert.Equal(expected, requiredBytes);
                }

                // Test again with multiple bits set, i.e, the maximum representable
                // value for the given number of bytes
                uint value = 0;
                for (var shift = steps[expected - 1]; shift < steps[expected]; shift++)
                {
                    value |= (uint)1 << shift;
                    var requiredBytes = PrefixVarIntHelpers.CountRequiredBytes(value);
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
                writer.WriteVarInt(num);
                Assert.Equal(PrefixVarIntHelpers.CountRequiredBytes(num), writer.Position);
                if (fastRead) writer.Write(0); // Extend the written amount so that there is always enough data to perform a fast read
                writer.Commit();

                session = new SerializerSession(null, null, null);
                var reader = new Reader(buffer.GetReadOnlySequence(120), session);
                var read = reader.ReadVarUInt32();

                Assert.Equal(num, read);
                Assert.Equal(PrefixVarIntHelpers.CountRequiredBytes(num), (int) reader.Position);
            }

            var nums = new uint[] { 0, 1, 1 << 7, 1 << 8, 1 << 15, 1 << 16, 1 << 20, 1 << 21, 1 << 24, 1 << 31 + 1234, 1234, 0xefefefef, uint.MaxValue };
            foreach (var fastRead in new[] {false, true})
            {
                foreach (var num in nums.Concat(Enumerable.Range(1, 512).Select(n => (uint)n)))
                {
                    Test(num, fastRead);
                }
            }

            {
                var buffer = new TestSingleSegmentBufferWriter(new byte[1000]);
                var session = new SerializerSession(null, null, null);
                var writer = new Writer<TestSingleSegmentBufferWriter>(buffer, session);
                foreach (var num in nums)
                {
                    writer.WriteVarInt(num);
                }

                writer.Commit();
                session = new SerializerSession(null, null, null);
                var reader = new Reader(buffer.GetReadOnlySequence(120), session);
                foreach (var num in nums)
                {
                    var read = reader.ReadVarUInt32();

                    Assert.Equal(num, read);
                }

                Assert.Equal(writer.Position, reader.Position);
            }
        }
    }
}

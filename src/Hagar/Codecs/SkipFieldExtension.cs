using System;
using System.Buffers;
using Hagar.Buffers;
using Hagar.WireProtocol;

namespace Hagar.Codecs
{
    public class SkipFieldCodec : IFieldCodec<object>
    {
        public void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta, Type expectedType, object value) where TBufferWriter : IBufferWriter<byte>
        {
            ReferenceCodec.MarkValueField(writer.Session);
            throw new NotImplementedException();
        }

        public object ReadValue(ref Reader reader, in Field field)
        {
            ReferenceCodec.MarkValueField(reader.Session);
            reader.SkipField(field);
            return null;
        }
    }

    public static class SkipFieldExtension
    {
        public static void SkipField(this ref Reader reader, in Field field)
        {
            switch (field.WireType)
            {
                case WireType.Reference:
                case WireType.VarInt:
                    reader.ReadVarUInt64();
                    break;
                case WireType.TagDelimited:
                    SkipTagDelimitedField(ref reader);
                    break;
                case WireType.LengthPrefixed:
                    SkipLengthPrefixedField(ref reader);
                    break;
                case WireType.Fixed32:
                    reader.Skip(4);
                    break;
                case WireType.Fixed64:
                    reader.Skip(8);
                    break;
                case WireType.Fixed128:
                    reader.Skip(16);
                    break;
                case WireType.Extended:
                    if (!field.IsEndBaseOrEndObject)
                        ThrowUnexpectedExtendedWireType(field);
                    break;
                default:
                    ThrowUnexpectedWireType(field);
                    break;
            }
        }

        internal static void ThrowUnexpectedExtendedWireType(in Field field)
        {
            throw new ArgumentOutOfRangeException(
                $"Unexpected {nameof(ExtendedWireType)} value [{field.ExtendedWireType}] in field {field} while skipping field.");
        }

        internal static void ThrowUnexpectedWireType(in Field field)
        {
            throw new ArgumentOutOfRangeException(
                $"Unexpected {nameof(WireType)} value [{field.WireType}] in field {field} while skipping field.");
        }

        internal static void SkipLengthPrefixedField(ref Reader reader)
        {
            var length = reader.ReadVarUInt32();
            reader.Skip(length);
        }

        private static void SkipTagDelimitedField(ref Reader reader)
        {
            Field header = default;
            while (true)
            {
                reader.ReadFieldHeader(ref header);
                if (header.IsEndObject) break;
                reader.SkipField(header);
            }
        }
    }
}
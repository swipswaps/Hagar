using Hagar.GeneratedCodeHelpers;
using Hagar.Serializers;
using Hagar.WireProtocol;
using System;
using System.Buffers;
using System.Net;

namespace Hagar.Codecs.SystemNet
{
    [RegisterSerializer]
    public sealed class IPAddressCodec : IFieldCodec<IPAddress>, IDerivedTypeCodec 
    {
        public static readonly Type CodecFieldType = typeof(IPAddress);

        IPAddress IFieldCodec<IPAddress>.ReadValue<TInput>(ref Buffers.Reader<TInput> reader, Field field) => ReadValue(ref reader, field);

        public static IPAddress ReadValue<TInput>(ref Buffers.Reader<TInput> reader, Field field)
        {
            if (field.WireType == WireType.Reference)
            {
                return (IPAddress)ReferenceCodec.ReadReference(ref reader, field, CodecFieldType);
            }

            var length = reader.ReadVarUInt32();
            IPAddress result;
#if NET5_0
            if (reader.TryReadBytes((int)length, out var bytes))
            {
                result = new IPAddress(bytes);
            }
            else
            {
#endif
                var addressBytes = reader.ReadBytes(length);
                result = new IPAddress(addressBytes);
#if NET5_0
            }
#endif

            ReferenceCodec.RecordObject(reader.Session, result);
            return result;
        }

        void IFieldCodec<IPAddress>.WriteField<TBufferWriter>(ref Buffers.Writer<TBufferWriter> writer, uint fieldIdDelta, Type expectedType, IPAddress value)
        {
            WriteField(ref writer, fieldIdDelta, expectedType, value);
        }

        public static void WriteField<TBufferWriter>(ref Buffers.Writer<TBufferWriter> writer, uint fieldIdDelta, Type expectedType, IPAddress value) where TBufferWriter : IBufferWriter<byte>
        {
            if (ReferenceCodec.TryWriteReferenceField(ref writer, fieldIdDelta, expectedType, value))
            {
                return;
            }

            writer.WriteFieldHeader(fieldIdDelta, expectedType, CodecFieldType, WireType.LengthPrefixed);
#if NET5_0
            Span<byte> buffer = stackalloc byte[64];
            if (value.TryWriteBytes(buffer, out var length))
            {
                var writable = writer.WritableSpan;
                if (writable.Length > length)
                {
                    writer.WriteVarInt((uint)length);
                    buffer.Slice(0, length).CopyTo(writable.Slice(1));
                    writer.AdvanceSpan(length);
                    return;
                }
            }
#endif
            var bytes = value.GetAddressBytes();
            writer.WriteVarInt((uint)bytes.Length);
            writer.Write(bytes);
        }
    }

    [RegisterSerializer]
    public sealed class IPEndPointCodec : IFieldCodec<IPEndPoint>
    {
        public static readonly Type CodecFieldType = typeof(IPEndPoint);

        IPEndPoint IFieldCodec<IPEndPoint>.ReadValue<TInput>(ref Buffers.Reader<TInput> reader, Field field) => ReadValue(ref reader, field);

        void IFieldCodec<IPEndPoint>.WriteField<TBufferWriter>(ref Buffers.Writer<TBufferWriter> writer, uint fieldIdDelta, Type expectedType, IPEndPoint value) => WriteField(ref writer, fieldIdDelta, expectedType, value);

        public static IPEndPoint ReadValue<TInput>(ref Buffers.Reader<TInput> reader, Field field)
        {
            if (field.WireType == WireType.Reference)
            {
                return (IPEndPoint)ReferenceCodec.ReadReference(ref reader, field, CodecFieldType);
            }

            var referencePlaceholder = ReferenceCodec.CreateRecordPlaceholder(reader.Session);
            IPAddress address = default;
            ushort port = 0;
            int id = 0;
            Field header = default;
            while (true)
            {
                id = HagarGeneratedCodeHelper.ReadHeader(ref reader, ref header, id);
                if (id == 1)
                {
                    address = IPAddressCodec.ReadValue(ref reader, header);
                    id = HagarGeneratedCodeHelper.ReadHeader(ref reader, ref header, id);
                }
                if (id == 2)
                {
                    port = UInt16Codec.ReadValue(ref reader, header);
                    id = HagarGeneratedCodeHelper.ReadHeaderExpectingEndBaseOrEndObject(ref reader, ref header, id);
                }
                if (id != -1)
                {
                    reader.ConsumeUnknownField(header);
                }
                else
                {
                    break;
                }
            }

            var result = new IPEndPoint(address, port);
            ReferenceCodec.RecordObject(reader.Session, result, referencePlaceholder);
            return result;
        }

        public static void WriteField<TBufferWriter>(ref Buffers.Writer<TBufferWriter> writer, uint fieldIdDelta, Type expectedType, IPEndPoint value) where TBufferWriter : IBufferWriter<byte>
        {
            if (ReferenceCodec.TryWriteReferenceField(ref writer, fieldIdDelta, expectedType, value))
            {
                return;
            }

            writer.WriteFieldHeader(fieldIdDelta, expectedType, CodecFieldType, WireType.TagDelimited);
            IPAddressCodec.WriteField(ref writer, 1, IPAddressCodec.CodecFieldType, value.Address);
            UInt16Codec.WriteField(ref writer, 1, UInt16Codec.CodecFieldType, (ushort)value.Port);
            writer.WriteEndObject();
        }
    }
}

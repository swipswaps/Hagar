using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using Hagar.Buffers;
using Hagar.Session;
using Hagar.WireProtocol;

namespace Hagar.Codecs
{
    /// <summary>
    /// Codec for operating with the wire format.
    /// </summary>
    public static class FieldHeaderCodec
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteFieldHeader<TBufferWriter>(
            ref this Writer<TBufferWriter> writer,
            uint fieldId,
            Type expectedType,
            Type actualType,
            WireType wireType) where TBufferWriter : IBufferWriter<byte>
        {
            var hasExtendedFieldId = fieldId > Tag.MaxEmbeddedFieldIdDelta;
            var embeddedFieldId = hasExtendedFieldId ? Tag.FieldIdCompleteMask : (byte) fieldId;
            var tag = (byte) ((byte) wireType | embeddedFieldId);

            if (actualType == expectedType)
            {
                writer.Write((byte) (tag | (byte) SchemaType.Expected));
                if (hasExtendedFieldId) writer.WriteVarUInt32(fieldId);
            }
            else if (writer.Session.WellKnownTypes.TryGetWellKnownTypeId(actualType, out var typeOrReferenceId))
            {
                writer.Write((byte) (tag | (byte) SchemaType.WellKnown));
                if (hasExtendedFieldId) writer.WriteVarUInt32(fieldId);
                writer.WriteVarUInt32(typeOrReferenceId);
            }
            else if (writer.Session.ReferencedTypes.TryGetTypeReference(actualType, out typeOrReferenceId))
            {
                writer.Write((byte) (tag | (byte) SchemaType.Referenced));
                if (hasExtendedFieldId) writer.WriteVarUInt32(fieldId);
                writer.WriteVarUInt32(typeOrReferenceId);
            }
            else
            {
                writer.Write((byte) (tag | (byte) SchemaType.Encoded));
                if (hasExtendedFieldId) writer.WriteVarUInt32(fieldId);
                writer.Session.TypeCodec.Write(ref writer, actualType);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteFieldHeaderExpected<TBufferWriter>(this ref Writer<TBufferWriter> writer, uint fieldId, WireType wireType)
            where TBufferWriter : IBufferWriter<byte>
        {
            if (fieldId < Tag.MaxEmbeddedFieldIdDelta) WriteFieldHeaderExpectedEmbedded(ref writer, fieldId, wireType);
            else WriteFieldHeaderExpectedExtended(ref writer, fieldId, wireType);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteFieldHeaderExpectedEmbedded<TBufferWriter>(this ref Writer<TBufferWriter> writer, uint fieldId, WireType wireType)
            where TBufferWriter : IBufferWriter<byte>
        {
            writer.Write((byte) ((byte) wireType | (byte) fieldId));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteFieldHeaderExpectedExtended<TBufferWriter>(this ref Writer<TBufferWriter> writer, uint fieldId, WireType wireType)
            where TBufferWriter : IBufferWriter<byte>
        {
            writer.Write((byte) ((byte) wireType | Tag.FieldIdCompleteMask));
            writer.WriteVarUInt32(fieldId);
        }

        public static void ReadFieldHeader(ref this Reader reader, ref Field field)
        {
            var tag = reader.ReadByte();
            field.Tag = tag;

            if ((tag & (byte)WireType.Extended) != (byte)WireType.Extended)
            {
                // If all of the field id delta bits are set and the field isn't an extended wire type field, read the extended field id delta
                var embeddedFieldId = tag & Tag.FieldIdCompleteMask;
                if (embeddedFieldId == Tag.FieldIdCompleteMask)
                {
                    field.FieldIdDelta = reader.ReadVarUInt32();
                }
                else
                {
                    field.FieldIdDelta = (uint)embeddedFieldId;
                }

                // If schema type is valid, read the type.
                field.FieldType = reader.ReadType(reader.Session, (SchemaType)(tag & Tag.SchemaTypeMask));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Type ReadType(this ref Reader reader, SerializerSession session, SchemaType schemaType)
        {
            switch (schemaType)
            {
                case SchemaType.Expected:
                    return null;
                case SchemaType.WellKnown:
                    var typeId = reader.ReadVarUInt32();
                    return session.WellKnownTypes.GetWellKnownType(typeId);
                case SchemaType.Encoded:
                    session.TypeCodec.TryRead(ref reader, out var encoded);
                    return encoded;
                case SchemaType.Referenced:
                    var reference = reader.ReadVarUInt32();
                    return session.ReferencedTypes.GetReferencedType(reference);
                default:
                    return ExceptionHelper.ThrowArgumentOutOfRange<Type>(nameof(SchemaType));
            }
        }
    }
}
using Hagar.Buffers;
using Hagar.Codecs;
using Hagar.WireProtocol;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Hagar.Utilities
{
    public static class BitstreamAnalyzer
    {
        public static string Analyze<TInput>(ref Reader<TInput> reader)
        {
            var res = new StringBuilder();
            reader.AnalyzeField(res);
            return res.ToString();
        }

        private static void AnalyzeField<TInput>(this ref Reader<TInput> reader, StringBuilder res)
        {
            var field = reader.ReadFieldHeader();
            AnalyzeField(ref reader, field, res);
        }

        public static void AnalyzeField<TInput>(this ref Reader<TInput> reader, Field field, StringBuilder res)
        {
            // References cannot themselves be referenced.
            if (field.WireType == WireType.Reference)
            {
                ReferenceCodec.MarkValueField(reader.Session);
                _ = reader.ReadVarUInt32();
                return;
            }

            // Record a placeholder so that this field can later be correctly deserialized if it is referenced.
            ReferenceCodec.RecordObject(reader.Session, new UnknownFieldMarker(field, reader.Position));

            switch (field.WireType)
            {
                case WireType.VarInt:
                    _ = reader.ReadVarUInt64();
                    break;
                case WireType.TagDelimited:
                    // Since tag delimited fields can be comprised of other fields, recursively consume those, too.
                    reader.AnalyzeTagDelimitedField(res);
                    break;
                case WireType.LengthPrefixed:
                    SkipFieldExtension.SkipLengthPrefixedField(ref reader);
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
                    SkipFieldExtension.ThrowUnexpectedExtendedWireType(field);
                    break;
                default:
                    SkipFieldExtension.ThrowUnexpectedWireType(field);
                    break;
            }
        }

        /// <summary>
        /// Consumes a tag-delimited field.
        /// </summary>
        private static void AnalyzeTagDelimitedField<TInput>(this ref Reader<TInput> reader, StringBuilder res)
        {
            while (true)
            {
                var field = reader.ReadFieldHeader();
                if (field.IsEndObject)
                {
                    break;
                }

                if (field.IsEndBaseFields)
                {
                    continue;
                }

                reader.AnalyzeField(field, res);
            }
        }
    }
}

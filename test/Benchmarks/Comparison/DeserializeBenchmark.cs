using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BenchmarkDotNet.Attributes;
using Benchmarks.Models;
using Benchmarks.Utilities;
using Hagar;
using Hagar.Buffers;
using Hagar.Codecs;
using Hagar.Session;
using Hyperion;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Serialization;
using Xunit;
using ZeroFormatter;
using SerializerSession = Hagar.Session.SerializerSession;

namespace Benchmarks.Comparison
{
    internal sealed class Unrolled_IntClass_Serializer : global::Hagar.Serializers.IPartialSerializer<global::Benchmarks.Models.IntClass>
    {
        private static readonly Type int32Type = typeof(int);

        public Unrolled_IntClass_Serializer()
        {
        }

        public void Serialize<TBufferWriter>(ref global::Hagar.Buffers.Writer<TBufferWriter> writer, global::Benchmarks.Models.IntClass instance)
            where TBufferWriter : IBufferWriter<byte>
        {
            global::Hagar.Codecs.Int32Codec.WriteField(ref writer, 0, int32Type, instance.MyProperty1);
            global::Hagar.Codecs.Int32Codec.WriteField(ref writer, 1, int32Type, instance.MyProperty2);
            global::Hagar.Codecs.Int32Codec.WriteField(ref writer, 1, int32Type, instance.MyProperty3);
            global::Hagar.Codecs.Int32Codec.WriteField(ref writer, 1, int32Type, instance.MyProperty4);
            global::Hagar.Codecs.Int32Codec.WriteField(ref writer, 1, int32Type, instance.MyProperty5);
            global::Hagar.Codecs.Int32Codec.WriteField(ref writer, 1, int32Type, instance.MyProperty6);
            global::Hagar.Codecs.Int32Codec.WriteField(ref writer, 1, int32Type, instance.MyProperty7);
            global::Hagar.Codecs.Int32Codec.WriteField(ref writer, 1, int32Type, instance.MyProperty8);
            global::Hagar.Codecs.Int32Codec.WriteField(ref writer, 1, int32Type, instance.MyProperty9);
        }

        public void Deserialize(ref global::Hagar.Buffers.Reader reader, global::Benchmarks.Models.IntClass instance)
        {
            int fieldId = 0;
            global::Hagar.WireProtocol.Field header;

            header = reader.ReadFieldHeader();
            fieldId += header.FieldIdDelta;
            if (fieldId != 0) goto slow;
            instance.MyProperty1 = (int)global::Hagar.Codecs.Int32Codec.ReadValue(ref reader, header);

            header = reader.ReadFieldHeader();
            fieldId += header.FieldIdDelta;
            if (fieldId != 1) goto slow;
            instance.MyProperty2 = (int)global::Hagar.Codecs.Int32Codec.ReadValue(ref reader, header);

            header = reader.ReadFieldHeader();
            fieldId += header.FieldIdDelta;
            if (fieldId != 2) goto slow;
            instance.MyProperty3 = (int)global::Hagar.Codecs.Int32Codec.ReadValue(ref reader, header);

            header = reader.ReadFieldHeader();
            fieldId += header.FieldIdDelta;
            if (fieldId != 3) goto slow;
            instance.MyProperty4 = (int)global::Hagar.Codecs.Int32Codec.ReadValue(ref reader, header);

            header = reader.ReadFieldHeader();
            fieldId += header.FieldIdDelta;
            if (fieldId != 4) goto slow;
            instance.MyProperty5 = (int)global::Hagar.Codecs.Int32Codec.ReadValue(ref reader, header);

            header = reader.ReadFieldHeader();
            fieldId += header.FieldIdDelta;
            if (fieldId != 5) goto slow;
            instance.MyProperty6 = (int)global::Hagar.Codecs.Int32Codec.ReadValue(ref reader, header);

            header = reader.ReadFieldHeader();
            fieldId += header.FieldIdDelta;
            if (fieldId != 6) goto slow;
            instance.MyProperty7 = (int)global::Hagar.Codecs.Int32Codec.ReadValue(ref reader, header);

            header = reader.ReadFieldHeader();
            fieldId += header.FieldIdDelta;
            if (fieldId != 7) goto slow;
            instance.MyProperty8 = (int)global::Hagar.Codecs.Int32Codec.ReadValue(ref reader, header);

            header = reader.ReadFieldHeader();
            fieldId += header.FieldIdDelta;
            if (fieldId != 8) goto slow;
            instance.MyProperty9 = (int)global::Hagar.Codecs.Int32Codec.ReadValue(ref reader, header);

            return;
slow:
            // Fix up field id if we encountered a negative value (expected for end of object cases)
            if (fieldId < 0) fieldId -= header.FieldIdDelta;
            DeserializeSlow(ref reader, instance, fieldId, header);
        }

        public void DeserializeSlow(ref global::Hagar.Buffers.Reader reader, global::Benchmarks.Models.IntClass instance, int fieldId, global::Hagar.WireProtocol.Field header)
        {
            while (!header.IsEndBaseOrEndObject)
            {
                switch ((fieldId))
                {
                    case 0:
                        instance.MyProperty1 = (int)global::Hagar.Codecs.Int32Codec.ReadValue(ref reader, header);
                        break;
                    case 1:
                        instance.MyProperty2 = (int)global::Hagar.Codecs.Int32Codec.ReadValue(ref reader, header);
                        break;
                    case 2:
                        instance.MyProperty3 = (int)global::Hagar.Codecs.Int32Codec.ReadValue(ref reader, header);
                        break;
                    case 3:
                        instance.MyProperty4 = (int)global::Hagar.Codecs.Int32Codec.ReadValue(ref reader, header);
                        break;
                    case 4:
                        instance.MyProperty5 = (int)global::Hagar.Codecs.Int32Codec.ReadValue(ref reader, header);
                        break;
                    case 5:
                        instance.MyProperty6 = (int)global::Hagar.Codecs.Int32Codec.ReadValue(ref reader, header);
                        break;
                    case 6:
                        instance.MyProperty7 = (int)global::Hagar.Codecs.Int32Codec.ReadValue(ref reader, header);
                        break;
                    case 7:
                        instance.MyProperty8 = (int)global::Hagar.Codecs.Int32Codec.ReadValue(ref reader, header);
                        break;
                    case 8:
                        instance.MyProperty9 = (int)global::Hagar.Codecs.Int32Codec.ReadValue(ref reader, header);
                        break;
                    default:
                        reader.ConsumeUnknownField(header);
                        break;
                }

                header = reader.ReadFieldHeader();
                fieldId += header.FieldIdDelta;
            }
        }
    }

    [Trait("Category", "Benchmark")]
    [Config(typeof(BenchmarkConfig))]
    public class DeserializeBenchmark
    {
        private static readonly MemoryStream ProtoInput;

        private static readonly byte[] MsgPackInput = MessagePack.MessagePackSerializer.Serialize(IntClass.Create());

        private static readonly string NewtonsoftJsonInput = JsonConvert.SerializeObject(IntClass.Create());

        private static readonly byte[] SpanJsonInput = SpanJson.JsonSerializer.Generic.Utf8.Serialize(IntClass.Create());

        private static readonly Hyperion.Serializer HyperionSerializer = new Hyperion.Serializer(new SerializerOptions(knownTypes: new[] {typeof(IntClass) }));
        private static readonly MemoryStream HyperionInput;

        private static readonly Serializer<IntClass> HagarSerializer;
        private static readonly Serializer<IntClass> HagarSerializer_Canary;
        private static readonly byte[] HagarInput;
        private static readonly SerializerSession Session;

        private static readonly SerializationManager OrleansSerializer;
        private static readonly List<ArraySegment<byte>> OrleansInput;
        private static readonly BinaryTokenStreamReader OrleansBuffer;

        static DeserializeBenchmark()
        {
            ProtoInput = new MemoryStream();
            ProtoBuf.Serializer.Serialize(ProtoInput, IntClass.Create());

            HyperionInput = new MemoryStream();
            HyperionSerializer.Serialize(IntClass.Create(), HyperionInput);

            // Hagar
            {
                var hagarServices = new ServiceCollection()
                    .AddHagar(hagar => hagar.AddAssembly(typeof(Program).Assembly))
                    .BuildServiceProvider();
                HagarSerializer = hagarServices.GetRequiredService<Serializer<IntClass>>();
                var bytes = new byte[1000];
                Session = hagarServices.GetRequiredService<SessionPool>().GetSession();
                var writer = new SingleSegmentBuffer(bytes).CreateWriter(Session);
                HagarSerializer.Serialize(ref writer, IntClass.Create());
                HagarInput = bytes;
            }

            // Hagar canary
            {
                var hagarServices = new ServiceCollection()
                    .AddHagar(hagar => hagar.Configure(config => config.Serializers.Add(typeof(Unrolled_IntClass_Serializer))))
                    .BuildServiceProvider();
                HagarSerializer_Canary = hagarServices.GetRequiredService<Serializer<IntClass>>();
            }

            // Orleans
            OrleansSerializer = new ClientBuilder()
                .ConfigureDefaults()
                .UseLocalhostClustering()
                .ConfigureServices(s => s.ToList().ForEach(r =>
                {
                    if (r.ServiceType == typeof(IConfigurationValidator)) s.Remove(r);
                }))
                .Configure<ClusterOptions>(o => o.ClusterId = o.ServiceId = "test")
                .ConfigureApplicationParts(parts => parts.AddApplicationPart(typeof(SimpleClass).Assembly).WithCodeGeneration())
                .Configure<SerializationProviderOptions>(options => options.FallbackSerializationProvider = typeof(SupportsNothingSerializer).GetTypeInfo())
                .Build().ServiceProvider.GetRequiredService<SerializationManager>();

            var writer2 = new BinaryTokenStreamWriter();
            OrleansSerializer.Serialize(IntClass.Create(), writer2);
            OrleansInput = writer2.ToBytes();
            OrleansBuffer = new BinaryTokenStreamReader(OrleansInput);
        }

        private static int SumResult(IntClass result)
        {
            return result.MyProperty1 +
                   result.MyProperty2 +
                   result.MyProperty3 +
                   result.MyProperty4 +
                   result.MyProperty5 +
                   result.MyProperty6 +
                   result.MyProperty7 +
                   result.MyProperty8 +
                   result.MyProperty9;
        }

        private static int SumResult(VirtualIntsClass result)
        {
            return result.MyProperty1 +
                   result.MyProperty2 +
                   result.MyProperty3 +
                   result.MyProperty4 +
                   result.MyProperty5 +
                   result.MyProperty6 +
                   result.MyProperty7 +
                   result.MyProperty8 +
                   result.MyProperty9;
        }

        [Fact]
        [Benchmark(Baseline = true)]
        public int Hagar()
        {
            Session.FullReset();
            var reader = new Reader(new ReadOnlySequence<byte>(HagarInput), Session);
            return SumResult(HagarSerializer.Deserialize(ref reader));
        }

        [Benchmark()]
        public int HagarCanary()
        {
            Session.FullReset();
            var reader = new Reader(new ReadOnlySequence<byte>(HagarInput), Session);
            return SumResult(HagarSerializer_Canary.Deserialize(ref reader));
        }

        //[Benchmark]
        public int Orleans()
        {
            OrleansBuffer.Reset(OrleansInput);
            return SumResult(OrleansSerializer.Deserialize<IntClass>(OrleansBuffer));
        }

        [Benchmark]
        public int MessagePackCSharp()
        {
            return SumResult(MessagePack.MessagePackSerializer.Deserialize<IntClass>(MsgPackInput));
        }

        [Benchmark]
        public int ProtobufNet()
        {
            ProtoInput.Position = 0;
            return SumResult(ProtoBuf.Serializer.Deserialize<IntClass>(ProtoInput));
        }

        //[Benchmark]
        public int Hyperion()
        {
            HyperionInput.Position = 0;
            return SumResult(HyperionSerializer.Deserialize<IntClass>(HyperionInput));
        }

        [Benchmark]
        public int NewtonsoftJson()
        {
            return SumResult(JsonConvert.DeserializeObject<IntClass>(NewtonsoftJsonInput));
        }

        [Benchmark(Description = "SpanJson")]
        public int SpanJsonUtf8()
        {
            return SumResult(SpanJson.JsonSerializer.Generic.Utf8.Deserialize<IntClass>(SpanJsonInput));
        }
    }
}
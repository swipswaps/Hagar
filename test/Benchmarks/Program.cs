using System;
using System.Threading;
using BenchmarkDotNet.Running;
using Benchmarks.Comparison;

namespace Benchmarks
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "loop")
            {
                var benchmarks = new SerializeBenchmark();
                var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                while (!cancellation.IsCancellationRequested)
                {
                    benchmarks.Hagar();
                }

                return;
            }

            if (args.Length > 0 && args[0] == "structloop")
            {
                var benchmarks = new StructSerializeBenchmark();
                var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                while (!cancellation.IsCancellationRequested)
                {
                    benchmarks.Hagar();
                }

                return;
            }

            if (args.Length > 0 && args[0] == "destructloop")
            {
                var benchmarks = new StructDeserializeBenchmark();
                var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                while (!cancellation.IsCancellationRequested)
                {
                    benchmarks.Hagar();
                }

                return;
            }

            var switcher = new BenchmarkSwitcher(new[]
            {
                typeof(DeserializeBenchmark),
                typeof(SerializeBenchmark),
                typeof(StructSerializeBenchmark),
                typeof(StructDeserializeBenchmark),
                typeof(ComplexTypeBenchmarks),
                typeof(FieldHeaderBenchmarks)
            });

            switcher.Run(args);
        }
    }
}

using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Hagar;
using Hagar.Buffers;
using Hagar.Configuration;
using Hagar.Session;
using Microsoft.Extensions.DependencyInjection;
using TestRpc.IO;
using TestRpc.Runtime;

namespace TestRpc
{
    internal sealed class ConnectionHandler
    {
        private readonly ChannelWriter<Message> outgoingWriter;
        private readonly ChannelReader<Message> outgoingReader;
        private readonly ConnectionContext connection;
        private readonly ChannelWriter<Message> incoming;
        private readonly SessionPool serializerSessionPool;
        private readonly Serializer<Message> messageSerializer;

        public ConnectionHandler(ConnectionContext connection, ChannelWriter<Message> received, SessionPool sessionPool, Serializer<Message> messageSerializer)
        {
            this.connection = connection;
            var outgoing = Channel.CreateUnbounded<Message>(
                new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false,
                });
            this.outgoingWriter = outgoing.Writer;
            this.outgoingReader = outgoing.Reader;
            this.incoming = received;
            this.serializerSessionPool = sessionPool;
            this.messageSerializer = messageSerializer;
        }

        public Task Run(CancellationToken cancellation) => Task.WhenAll(this.SendPump(cancellation), this.ReceivePump(cancellation));

        public ValueTask SendMessage(Message message, CancellationToken cancellation) => this.outgoingWriter.TryWrite(message)
            ? default
            : this.outgoingWriter.WriteAsync(message, cancellation);

        private async Task ReceivePump(CancellationToken cancellation)
        {
            using (var session = this.serializerSessionPool.GetSession())
            {
                var input = this.connection.Input;
                while (!cancellation.IsCancellationRequested)
                {
                    ReadResult result;
                    while (input.TryRead(out result))
                    {
                        var message = ReadMessage(result.Buffer, session, out var consumedTo);
                        input.AdvanceTo(consumedTo);
                        if (!this.incoming.TryWrite(message)) await this.incoming.WriteAsync(message, cancellation);
                    }

                    result = await input.ReadAsync(cancellation);

                    if (result.IsCanceled) break;
                    if (result.Buffer.IsEmpty && result.IsCompleted) break;
                }
            }

            Message ReadMessage(ReadOnlySequence<byte> payload, SerializerSession session, out SequencePosition consumedTo)
            {
                var reader = new Reader(payload, session);
                var result = this.messageSerializer.Deserialize(ref reader);
                consumedTo = payload.GetPosition(reader.Position);
                return result;
            }
        }

        private async Task SendPump(CancellationToken cancellation)
        {
            using (var session = this.serializerSessionPool.GetSession())
            {
                while (!cancellation.IsCancellationRequested && await this.outgoingReader.WaitToReadAsync(cancellation))
                {
                    while (this.outgoingReader.TryRead(out var item))
                    {
                        WriteMessage(item, session);
                        session.PartialReset();
                        
                        var flushTask = this.connection.Output.FlushAsync(cancellation);
                        if (flushTask.IsCompletedSuccessfully)
                        {
                            var flushResult = flushTask.GetAwaiter().GetResult();
                            if (flushResult.IsCanceled || flushResult.IsCompleted) return;
                        }
                        else
                        {
                            var flushResult = await flushTask;
                            if (flushResult.IsCanceled || flushResult.IsCompleted) return;
                        }
                    }
                }
            }

            void WriteMessage(Message message, SerializerSession session)
            {
                var writer = new Writer<PipeWriter>(this.connection.Output, session);
                this.messageSerializer.Serialize(ref writer, message);
            }
        }
    }

    internal class RuntimeClient : IRuntimeClient
    {
        // only one connection for now and no concurrency control.
        private readonly ConnectionContext connection;
        private readonly Serializer<Message> messageSerializer;

        private int messageId = 0;

        public RuntimeClient(ConnectionContext connection, Serializer<Message> messageSerializer)
        {
            this.connection = connection;
            this.messageSerializer = messageSerializer;
        }

        public Task<object> SendRequest(TargetId targetId, object request)
        {
            var message = new Message
            {
                Direction = Direction.Request,
                MessageId = this.messageId++,
                TargetId = targetId,
                Body = request
            };
            

        }
    }

    class Program
    {
        static async Task Main(string[] args)
        {
            var clientToServer = new Pipe(PipeOptions.Default);
            var serverToClient = new Pipe(PipeOptions.Default);
            var clientConnection = new ConnectionContext(serverToClient.Reader, clientToServer.Writer);
            var serverConnection = new ConnectionContext(clientToServer.Reader, serverToClient.Writer);
            await Task.WhenAll(RunServer(serverConnection), RunClient(clientConnection));
        }

        private static async Task RunServer<TConnection>(TConnection connection) where TConnection : IDuplexPipe
        {

        }

        private static async Task RunClient<TConnection>(TConnection connection) where TConnection : IDuplexPipe
        {

        }

        public static async Task TestRpc()
        {
            Console.WriteLine("Hello World!");
            var serviceProvider = new ServiceCollection()
                .AddHagar()
                .AddSerializers(typeof(Program).Assembly)
                .BuildServiceProvider();

            var config = serviceProvider.GetRequiredService<IConfiguration<SerializerConfiguration>>();
            var allProxies = config.Value.InterfaceProxies;

            /*var proxy = GetProxy<IMyInvokable>();
            await proxy.Multiply(4, 5, "hello");
            var proxyBase = proxy as MyProxyBaseClass;
            var invocation = proxyBase.Invocations.First();
            invocation.SetTarget(new TargetHolder(new MyImplementation()));
            await invocation.Invoke();
            invocation.Reset();

            var generic = GetProxy<IMyInvokable<int>>();
            //((MyProxyBaseClass)generic).Invocations.Find()
            await generic.DoStuff<string>();*/

            TInterface GetProxy<TInterface>()
            {
                if (typeof(TInterface).IsGenericType)
                {
                    var unbound = typeof(TInterface).GetGenericTypeDefinition();
                    var parameters = typeof(TInterface).GetGenericArguments();
                    foreach (var proxyType in allProxies)
                    {
                        if (!proxyType.IsGenericType) continue;
                        var matching = proxyType.FindInterfaces(
                                (type, criteria) =>
                                    type.IsGenericType && type.GetGenericTypeDefinition() == (Type)criteria,
                                unbound)
                            .FirstOrDefault();
                        if (matching != null)
                        {
                            return (TInterface)Activator.CreateInstance(
                                proxyType.GetGenericTypeDefinition().MakeGenericType(parameters));
                        }
                    }
                }

                return (TInterface)Activator.CreateInstance(
                    allProxies.First(p => typeof(TInterface).IsAssignableFrom(p)));
            }
        }
    }
}

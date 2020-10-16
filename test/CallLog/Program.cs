using FASTER.core;
using Hagar;
using Hagar.Configuration;
using Hagar.Invocation;
using HagarGeneratedCode.CallLog;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace CallLog
{
    class Program
    {
        static async Task Main(string[] args) => await Host
            .CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddHagar(hagar =>
                {
                    hagar
                        .AddAssembly(typeof(Program).Assembly)
                        .AddISerializableSupport();
                });
                services.AddSingleton<ApplicationContext>();
                services.AddSingleton<Catalog>();
                services.AddSingleton<MessageRouter>();
                services.AddSingleton<LogManager>();
                services.AddSingleton<ProxyFactory>();
                services.AddSingleton<IHostedService, MyApp>();
                services.AddSingleton<IHostedService, LogEnumerator>();
                services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<LogManager>());
            })
            .RunConsoleAsync();
    }

    internal class MyApp : BackgroundService
    {
        private readonly ILogger<MyApp> _log;
        private readonly Catalog _catalog;
        private readonly ProxyFactory _proxyFactory;
        private readonly IServiceProvider _serviceProvider;
        private readonly ApplicationContext _context;

        public MyApp(ILogger<MyApp> log, Catalog catalog, ProxyFactory proxyFactory, IServiceProvider serviceProvider, ApplicationContext context)
        {
            _log = log;
            _catalog = catalog;
            _proxyFactory = proxyFactory;
            _serviceProvider = serviceProvider;
            _context = context;
            _catalog.RegisterGrain(context.Id, context);
        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            var id = IdSpan.Create("counter1");
            var grain = ActivatorUtilities.CreateInstance<CounterGrain>(_serviceProvider, id);
            await grain.ActivateAsync();
            _catalog.RegisterGrain(id, grain);

            id = IdSpan.Create("counter2");
            grain = ActivatorUtilities.CreateInstance<CounterGrain>(_serviceProvider, id);
            await grain.ActivateAsync();
            _catalog.RegisterGrain(id, grain);
            await base.StartAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var id = IdSpan.Create("counter1");
            var proxy = _proxyFactory.GetProxy<ICounterGrain, RpcProxyBase>(id);
            RuntimeContext.Current = _context;
            while (!stoppingToken.IsCancellationRequested)
            {
                var result = await proxy.PingPongFriend(IdSpan.Create("counter2"), 1);
                _log.LogInformation("Got result: {DateTime}", result);
                await Task.Delay(10_000);
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            await base.StopAsync(cancellationToken);
        }
    }

    public static class RuntimeContext
    {
        private static readonly AsyncLocal<IGrainContext> _runtimeContext = new AsyncLocal<IGrainContext>();

        public static IGrainContext Current { get => _runtimeContext.Value; set => _runtimeContext.Value = value; }
    }

    [GenerateSerializer]
    public class Increment
    {
    }

    [GenerateMethodSerializers(typeof(RpcProxyBase))]
    public interface IGrain
    {
    }

    public interface ICounterGrain : IGrain
    {
        ValueTask Increment();
        ValueTask<DateTime> PingPongFriend(IdSpan friend, int cycles);
    }

    internal struct RequestState
    {
        public Response Response { get; set; }
        public IResponseCompletionSource Completion { get; set; }
    }


    [GenerateSerializer]
    internal class ActivationMarker
    {
        /// <summary>
        /// The time of this activation.
        /// </summary>
        [Id(1)]
        public DateTime Time { get; set; }

        /// <summary>
        /// The unique identifier for this activation.
        /// </summary>
        [Id(2)]
        public Guid InvocationId { get; set; }

        /// <summary>
        /// The version of the grain at the time of this activation.
        /// </summary>
        [Id(3)]
        public int Version { get; set; }
    }

    internal class CounterGrain : IGrainContext, ICounterGrain, ITargetHolder
    {
        private readonly object _lock = new object();
        private readonly Dictionary<long, RequestState> _callbacks = new Dictionary<long, RequestState>();
        private long _nextMessageId;
        private readonly Guid _invocationId;
        private bool _isRecovered;
        private readonly Channel<object> _messages;
        private readonly Channel<object> _logStage;
        private readonly ILogger<CounterGrain> _log;
        private readonly ProxyFactory _proxyFactory;
        private readonly LogManager _logManager;
        private readonly MessageRouter _router;
        private readonly IdSpan _id;
        private Task _runTask;
        private int _counter;

        public CounterGrain(IdSpan id, ILogger<CounterGrain> log, ProxyFactory proxyFactory, LogManager logManager, MessageRouter router)
        {
            _invocationId = Guid.NewGuid();
            _logStage = Channel.CreateUnbounded<object>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });
            _messages = Channel.CreateUnbounded<object>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });
            _id = id;
            _log = log;
            _proxyFactory = proxyFactory;
            _logManager = logManager;
            _router = router;
        }

        public IdSpan Id => _id;

        public int MaxSupportedVersion { get; set; } = 2;

        public int CurrentVersion { get; set; }

        public async ValueTask ActivateAsync()
        {
            RuntimeContext.Current = this;

            _logStage.Writer.TryWrite(new ActivationMarker
            {
                InvocationId = _invocationId,
                Time = DateTime.UtcNow,
                Version = this.MaxSupportedVersion,
            });

            _runTask = Task.WhenAll(Task.Run(RunLogWriter), Task.Run(RunMessagePump));

            await Task.CompletedTask;
        }

        public async ValueTask DeactivateAsync()
        {
            RuntimeContext.Current = this;
            _messages.Writer.TryComplete();
            if (_runTask is Task task)
            {
                await task.ConfigureAwait(false);
            }
        }

        public ValueTask Increment()
        {
            _log.LogInformation("Incrementing counter: {Counter}", _counter);
            _counter++;
            return default;
        }

        public void OnReplayMessage(object message)
        {
            var previousContext = RuntimeContext.Current;
            RuntimeContext.Current = this;
            try
            {
                if (message is Message msg && msg.Body is Response response)
                {
                    bool removed = false;
                    RequestState state;
                    lock (_lock)
                    {
                        removed = _callbacks.Remove(msg.SequenceNumber, out state);
                        if (!removed)
                        {
                            _callbacks[msg.SequenceNumber] = new RequestState { Response = response };
                        }
                    }

                    if (removed)
                    {
                        if (state.Completion is object)
                        {
                            state.Completion.Complete(response);
                        }
                        else
                        {
                            _log.LogWarning("Received unexpected response {Id}", msg.SequenceNumber);
                        }
                    }
                }
                else if (!_isRecovered)
                {
                    _messages.Writer.TryWrite(message);
                }
                else
                {
                    // TODO: Unsubscribe? Depends on log impl...
                    //_log.LogWarning("Received message {Message} after recovery has completed", message);
                }
            }
            finally
            {
                RuntimeContext.Current = previousContext;
            }
        }

        public void OnMessage(object message)
        {
            /*
            if (message is Message msg && msg.Body is Response response)
            {
                bool removed = false;
                RequestState state;
                lock (_lock)
                {
                    removed = _callbacks.Remove(msg.SequenceNumber, out state);
                    if (!removed)
                    {
                        _callbacks[msg.SequenceNumber] = new RequestState { Response = response };
                    }
                }

                if (removed)
                {
                    // Ensure responses are committed before sending them to the 
                    Task.Run(async () =>
                    {
                        // If the instance has not recovered, then the value must already have been persisted.
                        if (_isRecovered)
                        {
                            await _logManager.EnqueueLogEntryAndWaitForCommitAsync(
                                msg.SenderId,
                                new Message
                                {
                                    SenderId = _id,
                                    SequenceNumber = msg.SequenceNumber,
                                    Body = response
                                });
                        }

                        // Complete the request with the response.
                        state.Completion.Complete(response);
                    });
                }
            }
            else*/ if (!_logStage.Writer.TryWrite(message))
            {
                _log.LogWarning("Received message {Message} after deactivation has begun", message);
            }
        }

        public bool PrepareRequest(IResponseCompletionSource completion, out long sequenceNumber)
        {
            var previousContext = RuntimeContext.Current;
            RuntimeContext.Current = this;
            try
            {
                bool added;
                RequestState state;
                lock (_lock)
                {
                    // Get a unique id for this request.
                    var messageId = ++_nextMessageId;
                    sequenceNumber = messageId;
                    if (_callbacks.TryAdd(messageId, new RequestState { Completion = completion }))
                    {
                        added = true;
                        state = default;
                    }
                    else
                    {
                        // This request has already been seen.
                        added = false;
                        var removed = _callbacks.Remove(messageId, out state);
                        Debug.Assert(removed);
                    }
                }

                if (!added)
                {
                    // Complete the already-seen request with the existing response.
                    completion.Complete(state.Response);
                }

                return added;
            }
            finally
            {
                RuntimeContext.Current = previousContext;
            }
        }

        private async Task RunMessagePump()
        {
            RuntimeContext.Current = this;
            var reader = _messages.Reader;
            while (await reader.WaitToReadAsync())
            {
                while (reader.TryRead(out var item))
                {
                    if (item is ActivationMarker marker)
                    {
                        this.CurrentVersion = marker.Version;
                        if (marker.InvocationId == _invocationId)
                        {
                            _isRecovered = true;
                        }
                    }
                    else if (item is Message message)
                    {
                        await HandleCommittedMessage(message);
                    }
                    else
                    {
                        _log.LogWarning("{Id} received unknown message {Message}", _id.ToString(), item);
                        continue;
                    }
                }
            }
        }


        private ValueTask HandleCommittedMessage(Message message)
        {
            if (message.Body is Response response)
            {
                bool removed = false;
                RequestState state;
                lock (_lock)
                {
                    removed = _callbacks.Remove(message.SequenceNumber, out state);
                    if (!removed)
                    {
                        _callbacks[message.SequenceNumber] = new RequestState { Response = response };
                    }
                }

                if (removed)
                {
                    state.Completion.Complete(response);
                }
                else
                {
                    _log.LogWarning("Received unexpected response {Id}", message.SequenceNumber);
                }

                return default;
            }
            else if (message.Body is IInvokable request)
            {
                request.SetTarget(this);

                ValueTask<Response> responseTask;
                try
                {
                     responseTask = request.Invoke();
                }
                catch (Exception exception)
                {
                    responseTask = new ValueTask<Response>(Response.FromException<object>(exception));
                }

                if (responseTask.IsCompleted)
                {
                    _router.SendMessage(message.SenderId, new Message { SenderId = _id, SequenceNumber = message.SequenceNumber, Body = responseTask.Result });
                    return default;
                }
                else
                {
                    return HandleRequestAsync(message, responseTask);
                    async ValueTask HandleRequestAsync(Message message, ValueTask<Response> task)
                    {
                        try
                        {
                            response = await task;
                        }
                        catch (Exception exception)
                        {
                            response = Response.FromException<object>(exception);
                        }

                        _router.SendMessage(message.SenderId, new Message { SenderId = _id, SequenceNumber = message.SequenceNumber, Body = response });
                    }
                }
            }
            else
            {
                _log.LogWarning("{Id} received unknown message {Message}", _id.ToString(), message);
                return default;
            }
        }

        private async Task RunLogWriter()
        {
            RuntimeContext.Current = this;
            var writeQueue = new List<(long logAddress, object)>();
            var reader = _logStage.Reader;
            var writer = _messages.Writer;
            while (await reader.WaitToReadAsync())
            {
                while (reader.TryRead(out var item))
                {
                    var address = _logManager.EnqueueLogEntry(_id, item);
                    writeQueue.Add((address, item));
                }

                foreach (var (address, message) in writeQueue)
                {
                    await _logManager.WaitForCommitAsync(address, CancellationToken.None);

                    if (message is Message msg && msg.Body is Response response)
                    {
                        bool removed = false;
                        RequestState state;
                        lock (_lock)
                        {
                            removed = _callbacks.Remove(msg.SequenceNumber, out state);
                            if (!removed)
                            {
                                _callbacks[msg.SequenceNumber] = new RequestState { Response = response };
                            }
                        }

                        if (removed)
                        {
                            // Complete the request with the response.
                            if (state.Completion is object)
                            {
                                state.Completion.Complete(response);
                            }
                            else
                            {
                                _log.LogWarning("No request for response {Response} for message {Id}", response, msg.SequenceNumber);
                            }
                        }
                    }
                    else
                    {
                        writer.TryWrite(message);
                    }
                }

                writeQueue.Clear();
            }
        }

        public TTarget GetTarget<TTarget>() => (TTarget)(object)this;

        public TComponent GetComponent<TComponent>() => throw new NotImplementedException();

        public async ValueTask<DateTime> PingPongFriend(IdSpan friend, int cycles)
        {
            if (cycles <= 0)
            {
                var time = await WorkflowEnvironment.GetUtcNow();
                _log.LogInformation("{Id} says PINGPONG FRIEND at {DateTime}!", _id.ToString(), time);
                return time;
            }
            else
            {
                var friendProxy = _proxyFactory.GetProxy<ICounterGrain, RpcProxyBase>(friend);
                var time = await friendProxy.PingPongFriend(this.Id, cycles - 1);
                _log.LogInformation("{Id} received PINGPONG FRIEND at {DateTime}!", _id.ToString(), time);
                return time;
            }
        }
    }

    public class WorkflowEnvironment
    {
        public static ValueTask<DateTime> GetUtcNow() => Promise.Record(() => DateTime.UtcNow);
    }

    public class Promise
    {
        public static ValueTask<T> Record<T>(Func<T> func)
        {
            var completion = ResponseCompletionSourcePool.Get<T>();
            var current = RuntimeContext.Current;
            if (current.PrepareRequest(completion, out var sequenceNumber))
            {
                current.OnMessage(new Message { SenderId = current.Id, SequenceNumber = sequenceNumber, Body = Response.FromResult<T>(func()), });
            }    

            return completion.AsValueTask();
        }
    }

    [GenerateSerializer]
    public class Message
    {
        [Id(1)]
        public IdSpan SenderId { get; set; }

        [Id(2)]
        public long SequenceNumber { get; set; }

        [Id(3)]
        public object Body { get; set; }
    }

    [GenerateSerializer]
    public class LogEntry
    {
        [Id(1)]
        public IdSpan GrainId { get; set; }

        [Id(2)]
        public object Payload { get; set; }
    }

    public interface IGrainContext
    {
        IdSpan Id { get; }

        ValueTask ActivateAsync();

        void OnMessage(object message);
        void OnReplayMessage(object message);

        bool PrepareRequest(IResponseCompletionSource completion, out long sequenceNumber);

        ValueTask DeactivateAsync();
    }

    internal class Catalog 
    {
        private readonly ConcurrentDictionary<IdSpan, IGrainContext> _grains = new ConcurrentDictionary<IdSpan, IGrainContext>(IdSpan.Comparer.Instance);

        public void RegisterGrain(IdSpan grainId, IGrainContext grain)
        {
            _grains[grainId] = grain;
        }

        public IGrainContext GetGrain(IdSpan grainId)
        {
            if (_grains.TryGetValue(grainId, out var grain))
            {
                return grain;
            }

            throw new InvalidOperationException();
        }
    }

    internal class MessageRouter
    {
        private readonly Catalog _catalog;

        public MessageRouter(Catalog catalog) => _catalog = catalog;

        public void SendMessage(IdSpan grainId, Message message)
        {
            _catalog.GetGrain(grainId).OnMessage(message);
        }
    }

    internal class LogManager : BackgroundService, IDisposable 
    {
        private readonly FasterLog _dbLog;
        private readonly Serializer<LogEntry> _logEntrySerializer;

        public LogManager(Serializer<LogEntry> logEntrySerializer)
        {
            var path = Path.GetTempPath() + "Orleansia\\";
            IDevice device = Devices.CreateLogDevice(path + "main.log");

            // FasterLog will recover and resume if there is a previous commit found
            _dbLog = new FasterLog(new FasterLogSettings { LogDevice = device });
            _logEntrySerializer = logEntrySerializer;
        }

        public long EnqueueLogEntry(IdSpan grainId, object payload)
        {
            var bytes = _logEntrySerializer.SerializeToArray(new LogEntry
            {
                GrainId = grainId,
                Payload = payload,
            }, sizeHint: 20);

            return _dbLog.Enqueue(bytes);
        }

        public async ValueTask EnqueueLogEntryAndWaitForCommitAsync(IdSpan grainId, object payload)
        {
            var bytes = _logEntrySerializer.SerializeToArray(new LogEntry
            {
                GrainId = grainId,
                Payload = payload,
            }, sizeHint: 20);

            await _dbLog.EnqueueAndWaitForCommitAsync(bytes);
        }

        public ValueTask WaitForCommitAsync(long untilAddress, CancellationToken cancellationToken) => _dbLog.WaitForCommitAsync(untilAddress, cancellationToken);


        public FasterLog Log => _dbLog;

        public override void Dispose()
        {
            base.Dispose();
            _dbLog.Dispose();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(1_000, stoppingToken);
                    await _dbLog.CommitAsync(stoppingToken);
                }
                catch
                {

                }
            }
        }
    }

    internal class LogEnumerator : IHostedService
    {
        private readonly ILogger<LogEnumerator> _log;
        private readonly Channel<LogEntryHolder> _entryChannel;
        private readonly ChannelReader<LogEntryHolder> _entryChannelReader;
        private readonly ChannelWriter<LogEntryHolder> _entryChannelWriter;
        private readonly Serializer<LogEntry> _entrySerializer;
        private readonly FasterLog _dbLog;
        private readonly Catalog _catalog;
        private readonly CancellationTokenSource _shutdownCancellation = new CancellationTokenSource();
        private Task _runTask;

        public LogEnumerator(LogManager logManager, Catalog catalog, Serializer<LogEntry> entrySerializer, ILogger<LogEnumerator> log)
        {
            _entryChannel = Channel.CreateUnbounded<LogEntryHolder>(new UnboundedChannelOptions
            {
                SingleWriter = true,
                SingleReader = true,
            });
            _entryChannelReader = _entryChannel.Reader;
            _entryChannelWriter = _entryChannel.Writer;
            _dbLog = logManager.Log;
            _catalog = catalog;
            _entrySerializer = entrySerializer;
            _log = log;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _runTask = Task.WhenAll(Task.Run(RunLogReader), Task.Run(RunReader));
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _shutdownCancellation.Cancel();
            await _runTask;
        }

        public async Task RunLogReader()
        {
            try
            {
                using var iterator = _dbLog.Scan(beginAddress: _dbLog.BeginAddress, endAddress: long.MaxValue, name: "main", recover: true);
                await foreach (var (entry, entryLength, currentAddress, nextAddress) in iterator.GetAsyncEnumerable(_shutdownCancellation.Token))
                {
                    if (!_entryChannelWriter.TryWrite(new LogEntryHolder(entry, entryLength, currentAddress, nextAddress)))
                    {
                        break;
                    }

                    //iterator.CompleteUntil(currentAddress);
                }
            }
            catch (Exception exception)
            {
                _log.LogError(exception, "Error reading log");
            }
        } 

        public async Task RunReader()
        {
            try
            {
                while (await _entryChannelReader.WaitToReadAsync(_shutdownCancellation.Token))
                {
                    while (_entryChannelReader.TryRead(out var holder))
                    {
                        var entry = _entrySerializer.Deserialize(holder.Payload);
                        var grain = _catalog.GetGrain(entry.GrainId);
                        grain.OnReplayMessage(entry.Payload);
                    }
                }
            }
            catch (Exception exception)
            {
                _log.LogError(exception, "Error reading log");
            }
        }

        private readonly struct LogEntryHolder
        {
            private readonly byte[] _payload;
            private readonly int _length;

            public LogEntryHolder(byte[] payload, int length, long address, long nextAddress)
            {
                _payload = payload;
                _length = length;
                Address = address;
                NextAddress = nextAddress;
            }

            public Span<byte> Payload => _payload.AsSpan(0, _length);

            public long Address { get; }
            public long NextAddress { get; }
        }
    }

    // request => log => enumerator => target
    // response => log => enumerator => caller

    internal class ApplicationContext : IGrainContext
    {
        private long _nextRequestId = 0;
        private ILogger<ApplicationContext> _log;
        private readonly ConcurrentDictionary<long, IResponseCompletionSource> _pendingRequests = new ConcurrentDictionary<long, IResponseCompletionSource>();

        public ApplicationContext(ILogger<ApplicationContext> logger)
        {
            _log = logger;
        }

        public IdSpan Id { get; } = IdSpan.Create("app");
        public ValueTask ActivateAsync() => default;
        public ValueTask DeactivateAsync() => default;
        public void OnReplayMessage(object message)
        {
            _log.LogInformation("Replaying {Message}", message);
        }

        public void OnMessage(object message)
        {
            if (message is Message msg && msg.Body is Response response)
            {
                if (_pendingRequests.TryRemove(msg.SequenceNumber, out var completion))
                {
                    completion.Complete(response);
                }
                else
                {
                    _log.LogWarning("No pending request matching response for sequence {SequenceNumber}", msg.SequenceNumber);
                }
            }
            else
            {
                _log.LogWarning("Unsupported message of type {Type}: {Message}", message?.GetType(), message);
            }
        }

        public bool PrepareRequest(IResponseCompletionSource completion, out long sequenceNumber)
        {
            var requestId = Interlocked.Increment(ref _nextRequestId);
            sequenceNumber = requestId;
            _pendingRequests[requestId] = completion;

            return true;
        }
    }

    internal abstract class RpcProxyBase
    {
        private readonly IdSpan _id;
        private readonly MessageRouter _router;

        protected RpcProxyBase(IdSpan id, MessageRouter router)
        {
            _id = id;
            _router = router;
        }

        protected void SendRequest(IResponseCompletionSource callback, IInvokable body)
        {
            var caller = RuntimeContext.Current;
            var callerId = caller.Id;
            var message = new Message
            {
                SenderId = callerId,
                Body = body
            };

            if (caller.PrepareRequest(callback, out var sequenceNumber))
            {
                message.SequenceNumber = sequenceNumber;

                // Send the message if the sender signals that it should be sent.
                _router.SendMessage(_id, message);
            }
        }
    }
    
    public sealed class ProxyFactory
    {
        private readonly IServiceProvider _services;
        private readonly HashSet<Type> _knownProxies;
        private readonly ConcurrentDictionary<(Type, Type), Type> _proxyMap = new ConcurrentDictionary<(Type, Type), Type>();

        public ProxyFactory(IConfiguration<SerializerConfiguration> configuration, IServiceProvider services)
        {
            _services = services;
            _knownProxies = new HashSet<Type>(configuration.Value.InterfaceProxies);
        }

        private Type GetProxyType(Type interfaceType, Type baseType)
        {
            if (interfaceType.IsGenericType)
            {
                var unbound = interfaceType.GetGenericTypeDefinition();
                var parameters = interfaceType.GetGenericArguments();
                foreach (var proxyType in _knownProxies)
                {
                    if (!proxyType.IsGenericType)
                    {
                        continue;
                    }

                    if (!HasBaseType(proxyType.BaseType, baseType))
                    {
                        continue;
                    }

                    var matching = proxyType.FindInterfaces(
                            (type, criteria) =>
                                type.IsGenericType && type.GetGenericTypeDefinition() == (Type)criteria,
                            unbound)
                        .FirstOrDefault();
                    if (matching != null)
                    {
                        return proxyType.GetGenericTypeDefinition().MakeGenericType(parameters);
                    }
                }
            }

            return _knownProxies.First(interfaceType.IsAssignableFrom);

            static bool HasBaseType(Type type, Type baseType) => type switch
            {
                null => false,
                Type when type == baseType => true,
                _ => HasBaseType(type.BaseType, baseType)
            };
        }

        public TInterface GetProxy<TInterface, TBase>(IdSpan id)
        {
            if (!_proxyMap.TryGetValue((typeof(TInterface), typeof(TBase)), out var proxyType))
            {
                proxyType = _proxyMap[(typeof(TInterface), typeof(TBase))] = GetProxyType(typeof(TInterface), typeof(TBase));
            }

            return (TInterface)ActivatorUtilities.CreateInstance(_services, proxyType, id);
        }
    }

}

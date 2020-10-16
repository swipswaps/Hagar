using CallLog.Scheduling;
using Hagar;
using Hagar.Invocation;
using Hagar.Session;
using Microsoft.Extensions.Logging;
using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace CallLog
{
    // TODO: store the state elsewhere - it could be error prone to have services (ILogger) + data on the same class.
    [GenerateSerializer]
    public class CounterWorkflow : ICounterWorkflow
    {
        [NonSerialized]
        private readonly IdSpan _id;

        [NonSerialized]
        private readonly ILogger<CounterWorkflow> _log;

        [NonSerialized]
        private readonly ProxyFactory _proxyFactory;

        [Id(1)]
        internal int _counter;

        public CounterWorkflow(IdSpan id, ILogger<CounterWorkflow> log, ProxyFactory proxyFactory)
        {
            _id = id;
            _log = log;
            _proxyFactory = proxyFactory;
        }

        public ValueTask Increment()
        {
            _log.LogInformation("Incrementing counter: {Counter}", _counter);
            _counter++;
            return default;
        }

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
                var friendProxy = _proxyFactory.GetProxy<ICounterWorkflow, WorkflowProxyBase>(friend);
                var time = await friendProxy.PingPongFriend(_id, cycles - 1);
                _log.LogInformation("{Id} received PINGPONG FRIEND at {DateTime}!", _id.ToString(), time);
                return time;
            }
        }
    }

    internal class WorkflowContext : IWorkflowContext, ITargetHolder
    {
        private readonly object _lock = new object();
        private readonly Dictionary<long, RequestState> _callbacks = new Dictionary<long, RequestState>();
        private readonly Guid _invocationId;
        private readonly Channel<object> _logStage;
        private readonly ILogger<WorkflowContext> _log;
        private readonly LogManager _logManager;
        private readonly LogEnumerator _logEnumerator;
        private readonly MessageRouter _router;
        private readonly ActivationTaskScheduler _taskScheduler;
        private readonly IdSpan _id;
        private readonly Queue<Message> _messageQueue = new Queue<Message>();
        private long _nextMessageId;
        private bool _isRecovered;
        private Message _currentMessage;
        private object _instance;
        private Task _runTask;

        public WorkflowContext(
            IdSpan id,
            ILogger<WorkflowContext> log,
            ILogger<ActivationTaskScheduler> taskSchedulerLogger,
            LogManager logManager,
            LogEnumerator logEnumerator,
            MessageRouter router)
        {
            _invocationId = Guid.NewGuid();
            _logStage = Channel.CreateUnbounded<object>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });
            _id = id;
            _log = log;
            _logManager = logManager;
            _logEnumerator = logEnumerator;
            _router = router;
            _taskScheduler = new ActivationTaskScheduler(this, CancellationToken.None, taskSchedulerLogger);
        }

        public IdSpan Id => _id;

        public object Instance { get => _instance; set => _instance = value; }

        public int MaxSupportedVersion { get; set; } = 2;

        public int CurrentVersion { get; set; }

        public async ValueTask ActivateAsync()
        {
            RuntimeContext.Current = this;

            // Write a marker to signify the creation of a new activation.
            // This is used for:
            //  * Workflow versioning, so that workflow code can evolve over time while remaining deterministic.
            //  * Recovery, so that the workflow knows when it has recovered and can begin processing incoming requests.
            //  * Diagnostics, so that the log can be inspected to see where each run began.
            _logManager.EnqueueLogEntry(_id, new Message
            {
                SenderId = _id,
                SequenceNumber = -1, // weird
                Body =
                new ActivationMarker
                {
                    InvocationId = _invocationId,
                    Time = DateTime.UtcNow,
                    Version = MaxSupportedVersion,
                }
            });

            _ = Task.Factory.StartNew(state => ((WorkflowContext)state).RecoverAsync(), this, CancellationToken.None, TaskCreationOptions.None, _taskScheduler);
            await Task.CompletedTask;
        }

        public async ValueTask DeactivateAsync()
        {
            var previousContext = RuntimeContext.Current;
            try
            {
                RuntimeContext.Current = this;
                _logStage.Writer.TryComplete();
                if (_runTask is Task task)
                {
                    await task.ConfigureAwait(false);
                }
            }
            finally
            {
                RuntimeContext.Current = previousContext;
            }
        }

        public async Task RecoverAsync()
        {
            try
            {
                // Enumerate all log messages into buffer.
                var entries = _logEnumerator.GetCommittedLogEntries(_id);
                var requests = new List<Message>();
                await foreach (var entry in entries.Reader.ReadAllAsync())
                {
                    switch (entry)
                    {
                        case Message msg when msg.Body is Response:
                            // Handle responses immediately.
                            OnCommittedResponse(msg);
                            break;
                        case Message msg when msg.Body is IInvokable:
                            requests.Add(msg);
                            break;
                        default:
                            _log.LogWarning("Unknown message during recovery: {Message}", entry);
                            break;
                    }
                }

                foreach (var request in requests)
                {
                    OnCommittedRequest(request);
                }

                // Start pumping newly added messages.
                // TODO: Ideally, we would be pumping them already (for performance), but just not sending them to the committed message handlers until now.
                _runTask = RunLogWriter();
            }
            catch (Exception exception)
            {
                _log.LogError(exception, "Error during recovery");
                throw;
            }
        }

        internal void OnActivationMarker(ActivationMarker marker)
        {
            CurrentVersion = marker.Version;
            if (marker.InvocationId == _invocationId)
            {
                _isRecovered = true;
                _log.LogInformation("{Id} recovered with InvocationId {InvocationId}", _id.ToString(), _invocationId);
            }
            else
            {
                _log.LogInformation("{Id} with InvocationId {InvocationId} encountered previous invocation with id {PreviousInvocationId}", _id.ToString(), _invocationId, marker.InvocationId);
            }
        }

        public void OnCommittedMessage(object message)
        {
            switch (message)
            {
                case Message msg when msg.Body is Response:
                    OnCommittedResponse(msg);
                    break;
                case Message msg when msg.Body is IInvokable:
                    OnCommittedRequest(msg);
                    break;
                default:
                    _log.LogWarning("{Id} received unknown message {Message}", _id.ToString(), message);
                    break;
            }
        }

        public void OnCommittedResponse(Message message)
        {
            var sequenceNumber = message.SequenceNumber;
            var response = (Response)message.Body;

            bool removed = false;
            RequestState state;
            lock (_lock)
            {
                removed = _callbacks.Remove(sequenceNumber, out state);
                if (!removed)
                {
                    // Register the response for later execution, when the request is made.
                    _callbacks[sequenceNumber] = new RequestState { Response = response };
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
                    _log.LogWarning("Received duplicate response {Id}", sequenceNumber);
                }
            }
            else
            {
                _log.LogWarning("Received unexpected response {Id}", sequenceNumber);
            }
        }

        public void OnCommittedRequest(Message message)
        {
            lock (_lock)
            {
                if (_currentMessage is null)
                {
                    // Synchronously start invocation under the lock.
                    Invoke(message);
                }
                else
                {
                    // Enqueue the message. The current invocation will complete by acquiring the lock and checking the queue.
                    _messageQueue.Enqueue(message);
                }
            }
            
            void RunMessagePump()
            {
                while (true)
                {
                    lock (_lock)
                    {
                        if (!_messageQueue.TryPeek(out var message))
                        {
                            return;
                        }

                        if (_currentMessage is null)
                        {
                            // Invoke the message.
                            // No need to observe the dequeued message, since we are under the same lock it was peeked on.
                            _ = _messageQueue.Dequeue();
                            Invoke(message);
                        }
                    }
                }
            }

            // Note: this method must not be 'async' itself. It must be called under the lock, and _currentMessage
            // must be accessed under the lock.
            void Invoke(Message message)
            {
                var request = (IInvokable)message.Body;
                request.SetTarget(this);

                ValueTask<Response> responseTask;
                try
                {
                    _currentMessage = message;
                    responseTask = request.Invoke();
                }
                catch (Exception exception)
                {
                    responseTask = new ValueTask<Response>(Response.FromException<object>(exception));
                }

                if (responseTask.IsCompleted)
                {
                    _router.SendMessage(message.SenderId, new Message { SenderId = _id, SequenceNumber = message.SequenceNumber, Body = responseTask.Result });

                    _currentMessage = null;
                    if (_messageQueue.Count > 0)
                    {
                        RunMessagePump();
                    }
                }
                else
                {
                    _ = HandleRequestAsync(message, responseTask);

                    async ValueTask HandleRequestAsync(Message message, ValueTask<Response> task)
                    {
                        Response response = default;
                        try
                        {
                            response = await task;
                        }
                        catch (Exception exception)
                        {
                            response = Response.FromException<object>(exception);
                        }
                        finally
                        {
                            _router.SendMessage(message.SenderId, new Message { SenderId = _id, SequenceNumber = message.SequenceNumber, Body = response });

                            lock (_lock)
                            {
                                _currentMessage = null;
                                if (_messageQueue.Count > 0)
                                {
                                    RunMessagePump();
                                }
                            }
                        }
                    }
                }
            }
        }

        public void OnMessage(object message)
        {
            // If message has not been committed, commit it.
            //   How do we know?
            //      If it is a request, it must not have been committed.
            //      If it is a response && we are waiting for it, it must not have been committed
            //         - NOTE: this implies we pre-populate the _callback dictionary with all committed responses 
            // If message is response, execute it.
            // else if message is request && no current request is executing, schedule it.
            // else if message is request && a current request is executing, enqueue it.

            if (!_logStage.Writer.TryWrite(message))
            {
                _log.LogWarning("Received message {Message} after deactivation has begun", message);
            }
        }

        /// <summary>
        /// Returns false if a request has already been seen by this workflow.
        /// </summary>
        /// <param name="completion"></param>
        /// <param name="sequenceNumber"></param>
        /// <returns></returns>
        public bool PrepareRequest(IResponseCompletionSource completion, out long sequenceNumber)
        {
            // Always allocate a new sequence number.
            // If recovery is still being performed, 
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

        private async Task RunLogWriter()
        {
            RuntimeContext.Current = this;
            var writeQueue = new List<(long logAddress, object)>();
            var reader = _logStage.Reader;
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
                    OnCommittedMessage(message);
                }

                writeQueue.Clear();
            }
        }

        public TTarget GetTarget<TTarget>() => (TTarget)_instance;

        public TComponent GetComponent<TComponent>() => throw new NotImplementedException();
    }

}

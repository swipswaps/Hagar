using Hagar;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace CallLog
{
    public interface ICounterWorkflow : IWorkflow
    {
        [Id(1)]
        ValueTask<int> Increment();

        [Id(2)]
        ValueTask<DateTime> PingPongFriend(IdSpan friend, int cycles);
        
        [Id(3)]
        ValueTask<int> GetCounter();
    }

    public class CounterWorkflow : ICounterWorkflow
    {
        private int _msgId;

        [NonSerialized]
        private readonly IdSpan _id;

        [NonSerialized]
        private readonly ILogger<CounterWorkflow> _log;

        [NonSerialized]
        private readonly ProxyFactory _proxyFactory;

        internal int _counter;

        public CounterWorkflow(IdSpan id, ILogger<CounterWorkflow> log, ProxyFactory proxyFactory)
        {
            _msgId = 0;
            _id = id;
            _log = log;
            _proxyFactory = proxyFactory;
        }

        public ValueTask<int> GetCounter() => new ValueTask<int>(_counter);

        public ValueTask<int> Increment()
        {
            _log.LogInformation("{Id}.{MsgId} Incrementing counter: {Counter}", _id.ToString(), _msgId++.ToString(), _counter);
            _counter++;
            return new ValueTask<int>(_counter);
        }

        public async ValueTask<DateTime> PingPongFriend(IdSpan friend, int cycles)
        {
            if (cycles <= 0)
            {
                var time = await WorkflowEnvironment.GetUtcNow();
                _log.LogInformation("{Id}.{MsgId} says PINGPONG FRIEND at {DateTime}!", _id.ToString(), _msgId++.ToString(), time);
                return time;
            }
            else
            {
                var friendProxy = _proxyFactory.GetProxy<ICounterWorkflow, WorkflowProxyBase>(friend);
                var time = await friendProxy.PingPongFriend(_id, cycles - 1);
                _log.LogInformation("{Id}.{MsgId} received PINGPONG FRIEND at {DateTime}!", _id.ToString(), _msgId++.ToString(), time);

                return time;
            }
        }
    }

}

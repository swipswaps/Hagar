using System.Threading.Tasks;

namespace TestRpc.App
{
    public sealed class PingPongGrain : IPingPongGrain
    {
        public ValueTask Ping() => default;
    }
}
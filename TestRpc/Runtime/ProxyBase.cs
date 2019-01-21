using System;
using System.Threading.Tasks;
using Hagar.Invocation;

namespace TestRpc.Runtime
{
    public abstract class ProxyBase
    {
        [NonSerialized]
        private readonly IRuntimeClient runtimeClient;

        protected ProxyBase(TargetId id, IRuntimeClient runtimeClient)
        {
            this.runtimeClient = runtimeClient;
            this.TargetId = id;
        }

        public TargetId TargetId { get; }

        protected async ValueTask Invoke<T>(T request) where T : IInvokable
        {
            var result = await this.runtimeClient.SendRequest(this.TargetId, request);
            request.Result = result;
        }
    }
}
using System.Threading.Tasks;

namespace TestRpc.Runtime
{
    public interface IRuntimeClient
    {
        Task<object> SendRequest(TargetId targetId, object request);
    }
}
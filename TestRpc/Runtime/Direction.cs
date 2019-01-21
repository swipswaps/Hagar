using Hagar;

namespace TestRpc.Runtime
{
    [GenerateSerializer]
    public enum Direction
    {
        Request,
        Response
    }
}
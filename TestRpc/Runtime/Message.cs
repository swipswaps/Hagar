using Hagar;

namespace TestRpc.Runtime
{
    [GenerateSerializer]
    public sealed class Message
    {
        [Id(0)]
        public int MessageId { get; set; }
        [Id(1)]
        public TargetId TargetId { get; set; }
        [Id(2)]
        public Direction Direction { get; set; }
        [Id(3)]
        public object Body { get; set; }
    }
}
using Hagar;

namespace TestRpc.Runtime
{
    [GenerateSerializer]
    public struct TargetId
    {
        public TargetId(int id) => this.Id = id;

        [Id(0)]
        public int Id { get; set; }
    }
}
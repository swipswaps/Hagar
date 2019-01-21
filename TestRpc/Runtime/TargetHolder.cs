using System;
using Hagar.Invocation;

namespace TestRpc.Runtime
{
    internal struct TargetHolder<T> : ITargetHolder
    {
        private readonly T target;

        public TargetHolder(T target)
        {
            this.target = target;
        }

        public TTarget GetTarget<TTarget>() => (TTarget)(object)this.target;

        public TExtension GetExtension<TExtension>() => throw new NotImplementedException();
    }
}
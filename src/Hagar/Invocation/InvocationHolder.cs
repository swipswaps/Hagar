using System;
using System.Buffers;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Hagar.Buffers;
using Hagar.Codecs;
using Microsoft.Extensions.ObjectPool;

namespace Hagar.Invocation
{
    /// <summary>
    /// Represents an object which holds an invocation target as well as target extensions.
    /// </summary>
    public interface ITargetHolder
    {
        /// <summary>
        /// Gets the target.
        /// </summary>
        /// <typeparam name="TTarget">The target type.</typeparam>
        /// <returns>The target.</returns>
        TTarget GetTarget<TTarget>();

        /// <summary>
        /// Gets the extension object with the specified type.
        /// </summary>
        /// <typeparam name="TExtension">The extension type.</typeparam>
        /// <returns>The extension object with the specified type.</returns>
        TExtension GetExtension<TExtension>();
    }

    /// <summary>
    /// Represents an object which can be invoked asynchronously.
    /// </summary>
    public interface IInvokable
    {
        /// <summary>
        /// Gets the invocation target.
        /// </summary>
        /// <typeparam name="TTarget">The target type.</typeparam>
        /// <returns>The invocation target.</returns>
        TTarget GetTarget<TTarget>();

        /// <summary>
        /// Sets the invocation target from an instance of <see cref="ITargetHolder"/>.
        /// </summary>
        /// <typeparam name="TTargetHolder">The target holder type.</typeparam>
        /// <param name="holder">The invocation target.</param>
        void SetTarget<TTargetHolder>(TTargetHolder holder) where TTargetHolder : ITargetHolder;

        /// <summary>
        /// Invoke the object.
        /// </summary>
        /// <returns>A <see cref="ValueTask"/> which will complete when the invocation is complete.</returns>
        ValueTask Invoke();

        /// <summary>
        /// Gets or sets the result of invocation.
        /// </summary>
        /// <remarks>This property is internally set by <see cref="Invoke"/>.</remarks>
        object Result { get; set; }

        /// <summary>
        /// Gets the result.
        /// </summary>
        /// <typeparam name="TResult">The result type.</typeparam>
        /// <returns>The result.</returns>
        TResult GetResult<TResult>();

        /// <summary>
        /// Sets the result.
        /// </summary>
        /// <typeparam name="TResult">The result type.</typeparam>
        /// <param name="value">The result value.</param>
        void SetResult<TResult>(ref TResult value);

        /// <summary>
        /// Serializes the result to the provided <paramref name="writer"/>.
        /// </summary>
        /// <typeparam name="TBufferWriter">The underlying buffer writer type.</typeparam>
        /// <param name="writer">The buffer writer.</param>
        void SerializeResult<TBufferWriter>(ref Writer<TBufferWriter> writer) where TBufferWriter : IBufferWriter<byte>;

        /// <summary>
        /// Gets the number of arguments.
        /// </summary>
        int ArgumentCount { get; }

        /// <summary>
        /// Gets the argument at the specified index.
        /// </summary>
        /// <typeparam name="TArgument">The argument type.</typeparam>
        /// <param name="index">The argument index.</param>
        /// <returns>The argument at the specified index.</returns>
        TArgument GetArgument<TArgument>(int index);

        /// <summary>
        /// Sets the argument at the specified index.
        /// </summary>
        /// <typeparam name="TArgument">The argument type.</typeparam>
        /// <param name="index">The argument index.</param>
        /// <param name="value">The argument value</param>
        void SetArgument<TArgument>(int index, ref TArgument value);

        /// <summary>
        /// Resets this instance.
        /// </summary>
        void Reset();
    }

    /// <inheritdoc />
public abstract class Invokable // : IInvokable
    {
    /*
        /// <inheritdoc />
        public abstract TTarget GetTarget<TTarget>();

        /// <inheritdoc />
        public abstract void SetTarget<TTargetHolder>(TTargetHolder holder) where TTargetHolder : ITargetHolder;

        /// <inheritdoc />
        public abstract ValueTask Invoke();

        /// <inheritdoc />
        public object Result
        {
            get => this.GetResult<object>();
            set => this.SetResult(ref value);
        }

        /// <inheritdoc />
        public abstract TResult GetResult<TResult>();

        /// <inheritdoc />
        public abstract void SetResult<TResult>(ref TResult value);

        /// <inheritdoc />
        public abstract void SerializeResult<TBufferWriter>(ref Writer<TBufferWriter> writer)
            where TBufferWriter : IBufferWriter<byte>;

        /// <inheritdoc />
        public abstract int ArgumentCount { get; }

        /// <inheritdoc />
        public abstract TArgument GetArgument<TArgument>(int index);

        /// <inheritdoc />
        public abstract void SetArgument<TArgument>(int index, ref TArgument value);

        /// <inheritdoc />
        public abstract void Reset();
    */
    }

// for experimentation
    public interface IMyInterface
    {
        ValueTask<int> Multiply(int a, int b);
    }

    public class MyImplementation : IMyInterface
    {
        public ValueTask<int> Multiply(int a, int b) => new ValueTask<int>(a * b);
    }
    /*
// Note that we would want to optimize type name serialization for these generated classes.
// We can generate special type ids for each (note: must support generic methods, which will be expressed as generic IInvokable impls)
// We can even use an custom serializer (within the Hagar wire protocol) which has its own type encoding and lookup table (eg populated using feature providers)
// - that would give us a lot of freedom for how we encode types.
    [Serializable]
    internal sealed class MyImplementation_Multiply_Int_Int_Closure : Invokable
    {
        public MyImplementation_Multiply_Int_Int_Closure(IFieldCodec<int> intCodec)
        {
            this.resultCodec = intCodec;
        }

        [NonSerialized]
        internal IMyInterface target;

        [NonSerialized]
        internal int result;

        [Id(0)]
        internal int arg0;

        [Id(1)]
        internal int arg1;

        // Note that in Hagar-generated code this field would have been removed because int is a well-known type and the code generator treats its codec as an intrinsic.
        // So it would be a static class access, Int32Codec. This demonstrates the general case.
        private readonly IFieldCodec<int> resultCodec;

        public override void SetTarget<TTargetHolder>(TTargetHolder holder) =>
            this.target = holder.GetTarget<IMyInterface>();

        public override ValueTask Invoke()
        {
            var resultTask = this.target.Multiply(this.arg0, this.arg1);

            // Avoid allocations and async machinery on the fast path
            if (resultTask.IsCompleted) // Even if it failed.
            {
                this.result = resultTask.GetAwaiter().GetResult();
                return default; // default(ValueTask) is a successfully completed ValueTask.
            }

            // Allocate only on the slow path (when the call is actually async, not just returning Task.FromResult(x))
            // We can likely improve perf here, too by using IValueTaskSource and pooling,
            // but it's an optimization which can come later.
            return InvokeAsync(resultTask);

            async ValueTask InvokeAsync(ValueTask<int> asyncValue)
            {
                this.result = await asyncValue.ConfigureAwait(false);
            }
        }

        public override void SerializeResult<TBufferWriter>(ref Writer<TBufferWriter> writer) =>
            this.resultCodec.WriteField(ref writer, 0, typeof(int), this.result);

        public override TResult GetResult<TResult>() => throw new System.NotImplementedException();

        public override void SetResult<TResult>(ref TResult value) => this.result = (int)(object)value;

// The remaining members are not strictly necessary but allow supporting features such as call filters, pooling.

        public override TTarget GetTarget<TTarget>() => (TTarget)this.target;

        public override int ArgumentCount => 2;

        public override TArgument GetArgument<TArgument>(int index)
        {
            switch (index)
            {
                case 0: return (TArgument)(object)this.arg0;
                case 1: return (TArgument)(object)this.arg1;
                default:
                    ThrowIndexOutOfRange(index);
                    return default;
            }

            void ThrowIndexOutOfRange(int i) => throw new ArgumentOutOfRangeException(
                nameof(index),
                $"Argument index out of range, must be between 0 and {Math.Max(0, this.ArgumentCount - 1)}. {nameof(index)}: {i}.")
            ;
        }

        public override void SetArgument<TArgument>(int index, ref TArgument value)
        {
            switch (index)
            {
                case 0:
                    this.arg0 = (int)(object)value;
                    return;
                case 1:
                    this.arg1 = (int)(object)value;
                    return;
                default:
                    ThrowIndexOutOfRange(index, value);
                    return;
            }

            void ThrowIndexOutOfRange(int i, object v) => throw new ArgumentOutOfRangeException(
                nameof(index),
                $"Argument index out of range, must be between 0 and {Math.Max(0, this.ArgumentCount - 1)}. {nameof(index)}: {i}, value: {v}")
            ;
        }

        public override void Reset()
        {
            this.arg0 = default;
            this.arg1 = default;
            this.result = default;
        }
    }

    public sealed partial class Generated_MyInterface_Proxy : IMyInterface
    {
        private ObjectPool<MyImplementation_Multiply_Int_Int_Closure> pool;

        public async ValueTask<int> Multiply(int a, int b)
        {
            var request = this.pool.Get();
            try
            {
                request.arg0 = a;
                request.arg1 = b;

                // issues request, returns a generic Response object which contains buffer containing result value.
                // If request failed (eg, remote grain threw an exception) then the exception is thrown from this call.

                //await base.InvokeMethodAsync(request, this.Version);
                var result = request.result;
                return result;
            }
            finally
            {
                request.Reset(); // Reset, return request to pool.
                this.pool.Return(request);
            }
        }
    }

    public abstract class MyProxyBaseClass
    {
        protected ValueTask Invoke<TInvokable>(TInvokable invokable) where TInvokable : IInvokable
        {
            return default;
        }
    }
}

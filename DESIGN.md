
## Method call serialization

Implementing RPC requires serializing information about which method is being called and (in our case) information about which object the target of that method is.

There are many systems (eg, RPC, decoupled RPC, event sourcing, transaction processing, database journaling) which can be implemented using some form of method call serialization.

The goal is to transform some interface, `IMyInterface`, into at least two separate parts: a *proxy* which is responsible for capturing the method call (target, method, arguments) and an *invoker* which is responsible for executing a captured method call against a target object.

The proxy is a class which implements the interface.

``` csharp
// All methods generate a close which implements IInvokable.
// Methods with arguments also implement IInvokableWithArguments
// Methods which return a meaningful value (eg, Task<T>, ValueTask<T>) also implement IInvokableWithResult
// Code generation uses these three interfaces

public interface IInvokable
{
    // Invoke the call on the target and set the result.
    public ValueTask Invoke()
}

public interface IInvokableWithArguments
{
// Not required but demonstrates how we could have accessors which do not require type information.
    // This is expensive both for getting and setting since it likely requires boxing.
    // This likely boxes - indented only for call filters and other middleware.
    ReadOnlySpan<object> Arguments { get; set; }

// Not required but demonstrates how we could have more efficient accessors for args/result
    TArgument GetArgument<TArgument>(int index);
    void SetArgument<TArgument>(int index, ref TArgument value);
}

public interface IInvokableWithResult
{
    // This is only valid after awaiting Invoke()
    // This likely boxes - indented only for call filters and other middleware.
    object Result { get; set; }

    // Get is required and the implementation should be inlined. Can be called after awaiting Invoke().
    TResult GetResult<TResult>();
    void SetResult<TResult>(ref TResult value);

    // This is only valid after awaiting Invoke(). Called on target side.
    void SerializeResult(ref Writer<TBuffer> writer) where TBuffer : IOutputStream;

    // Called on receiver side after call returns from (remote) target.
    void DeserializeResult(ref Reader reader);
}

[Serializable]
public struct MyInterface_MyMethod_Closure<TTarget, TMethodArg1, TMethodParam2> : IInvokable
  where TTarget : IMyInterface
  where TMethodArg1 : <method generic parameter constraints>
  // etc
{
    private MyInterface_MyMethod_Closure_Codec<TTarget, TMethodArg1, TMethodParam2> codec;
    private TTarget target; // Generated deserializer is responsible for calling into (eg) catalog to get target implementation (eg, grain)
    private TArg1 arg1;
    private TArg2 arg2;

    public TResult result;

    public object Result { get => this.result; set => this.result = (TResult)value; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SerializeResult(ref Writer<TBuffer> writer) where TBuffer : IOutputStream;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DeserializeResult(ref Reader reader);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask Invoke()
    {
        var resultTask = target.MyMethod<TMethodArg1, TMethodArg2>(arg1, arg2);

        // Avoid allocations and async machinery on the fast path
        if (resultTask.IsCompleted) // Even if it failed.
        {
            this.result = resultTask.GetAwaiter().GetResult();
            return ValueTask.CompletedTask;
        }

        // Allocate only on the slow path (when the call is actually async, not just returning Task.FromResult(x))
        // We can likely improve perf here, too by using IValueTaskSource and pooling,
        // but it's an optimization which can come later.
        return InvokeAsync(resultTask);

        async ValueTask InvokeAsync(TResultTask asyncValue)
        {
            this.result = await asyncValue; // consider if ConfigureAwait(false) is beneficial here
        }
    }

    // For pooling, reset the fields on this instance. Is it faster to set the entire instance to `default` at the holder level?
    void Reset();
}

// Implementation holds the IInvokable struct
public abstract class InvocationHolder : IInvokable // All invocation methods are forwarded to the IInvokable struct held by the subclass.
{
}

// Specialized at runtime, based upon deserialized type.
// This type can be pooled (and reset between uses by `holder.Payload = default`)
public class InvocationHolder<TInvokable> : InvocationHolder where TInvokable : IInvokable
{
    // Generated code sets this value
    TInvokable Payload { get; set; }
}

```
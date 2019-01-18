
## Method call serialization

Implementing RPC requires serializing information about which method is being called and (in our case) information about which object the target of that method is.

There are many systems (eg, RPC, decoupled RPC, event sourcing, transaction processing, database journaling) which can be implemented using some form of method call serialization.

The goal is to transform some interface, `IMyInterface`, into at least two separate parts: a *proxy* which is responsible for capturing the method call (target, method, arguments) and an *invoker* which is responsible for executing a captured method call against a target object.

The proxy is a class which implements the interface.

NOTE: for this design to work *well* (minimal interface dispatch) may require:

* Serializer object pooling (done, we have that ability today)
* Type substitution?! I.e, ideally we would want to say "deserialize this class which specifies the grain *interface* type, but specify the concrete grain type instead.
    * I'm not certain if this is a good idea yet:
        * For one, an interface can be implemented by multiple classes.
        * How would this target grain extensions?
        * Ideally we serialize the minimum information required to get the target (grain).
        * In the event that the target is not yet activated, we also need anough information to activate it.
            * NewPlacement calls could include additional information which is not usually required.
        * GrainReference serialization today is flawed in terms of how generic arguments are serialized (as strings) and there are edge cases for generics (eg when iface generic params don't match class generic parameters). Currently the mapping between interface and implementation (type id) is done on the caller. The caller has a copy of the type map and constructs a GrainId/GrainReference using that type map.
        * How should versioning work? Similar to iface -> class mapping, that can be done on the caller (during addressing/placement) and a version stamp added to the message header.

Message:
  * Target -> Some app-specific definition of the target
  * Invokable -> Encapsulates target Interface, Method, Arguments
    * Reason to include interface: by including interface as a `Type`, we can support grain extensions
		* Alternatively, because grain extensions must implement a special marker interface (IGrainExtension), we always know that the target will not directly implement the required interface and the invoker can _ask the target for its extension_. This last bit is hand-wavy and needs design. In Orleans we would want to pass the ActivationData since that has both the Grain (main target) as well as grain extensions. In order to keep things generic, we can take a new interface type and wrap the ActivationData in a struct which implements that interface, eg:

``` csharp
interface IHasTargetAndExtensions
{
	object GetTarget();
	object GetExtension<T>();
}

struct ActivationDataTargetWithExtensions : IHasTargetAndExtensions
{
	private ActivationData inner;
	
	// We expect this to be called significantly more frequently than GetExtension. Theoretically
	// these two methods could be merged into one, but splitting them out for the common case
	// and less common case allows for improved performance.
	T GetTarget<T>() => inner.Grain;

	// Get the extension, potentially installing it (for auto-installed extensions)
	T GetExtension<T>() => inner.GetExtension<T>(); 
}

// then the method on the IInvokable can look like this:
void SetTarget<T>(T holder) where T : IHasTargetAndExtensions
{
	// If this is a regular grain method:
	this.target = (TTarget)holder.GetTarget();
	
	// else if this is a grain extension:
	this.target = inner.GetExtension<T>(); 
}
```

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
    [NonSerialized]
    public MyInterface_MyMethod_Closure_Codec<TTarget, TMethodArg1, TMethodParam2> codec;
    
    [Id(0)]
    public TTarget target; // Generated deserializer is responsible for calling into (eg) catalog to get target implementation (eg, grain)
    
    [Id[1]]
    public TArg1 arg1;

    [Id(2)]
    public TArg2 arg2; // etc

    [NonSerialized]
    public TResult result;

		// Allows us to support grain extensions (if !TargetType.IsAssignableFrom(target), get extension which matches TargetType)
		[NonSerialized]
		public Type TargetType => typeof(TTarget);

		[NonSerialized]
		public MethodInfo TargetMethod => typeof(TTarget).GetMethod(...);

    public object Result { get => this.result; set => this.result = (TResult)value; }

    // The result is serialized directly and it will be deserialized by
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

// Implementation holds the IInvokable struct.
// Why do it this way? We could make 
public abstract class InvocationHolder : IInvokable
{
}

// Specialized at runtime, based upon deserialized type.
// This type can be pooled (and reset between uses by `holder.Payload = default`)
// All invocation methods are forwarded to the IInvokable struct held by the subclass.
public class InvocationHolder<TInvokable> : InvocationHolder where TInvokable : IInvokable
{
    // Generated code sets this value
    TInvokable Payload { get; set; }
}

```
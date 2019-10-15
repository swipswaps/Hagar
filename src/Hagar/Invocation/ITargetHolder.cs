namespace Hagar.Invocation
{
    /// <summary>
    /// Represents an object which holds an invocation target as well as target extensions.
    /// </summary>
    public interface ITargetHolder
    {
        /// <summary>
        /// Gets the component with the specified type.
        /// </summary>
        /// <typeparam name="T">The target type.</typeparam>
        /// <returns>The target.</returns>
        T GetComponent<T>();

        /// <summary>
        /// Gets the component with the specified type.
        /// </summary>
        /// <typeparam name="T">The component type.</typeparam>
        /// <returns>The component with the specified type.</returns>
        T GetComponent<TKey, T>(TKey key);
    }
}
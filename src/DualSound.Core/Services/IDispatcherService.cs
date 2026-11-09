using System;
using System.Threading.Tasks;

namespace DualSound.Core.Services;

/// <summary>
/// Abstraction for marshaling execution to the UI thread safely.
/// </summary>
public interface IDispatcherService
{
    /// <summary>
    /// Synchronously executes an action on the UI thread.
    /// </summary>
    void Invoke(Action action);

    /// <summary>
    /// Asynchronously executes an action on the UI thread.
    /// </summary>
    Task InvokeAsync(Action action);

    /// <summary>
    /// Asynchronously executes a function returning a result on the UI thread.
    /// </summary>
    Task<T> InvokeAsync<T>(Func<T> func);
}


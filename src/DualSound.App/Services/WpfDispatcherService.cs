using System;
using System.Threading.Tasks;
using System.Windows;
using DualSound.Core.Services;

namespace DualSound.App.Services;

/// <summary>
/// Dispatches invocations to the WPF UI thread.
/// </summary>
public sealed class WpfDispatcherService : IDispatcherService
{
    public void Invoke(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (Application.Current?.Dispatcher == null || Application.Current.Dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            Application.Current.Dispatcher.Invoke(action);
        }
    }

    public async Task InvokeAsync(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (Application.Current?.Dispatcher == null || Application.Current.Dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            await Application.Current.Dispatcher.InvokeAsync(action);
        }
    }

    public async Task<T> InvokeAsync<T>(Func<T> func)
    {
        ArgumentNullException.ThrowIfNull(func);

        if (Application.Current?.Dispatcher == null || Application.Current.Dispatcher.CheckAccess())
        {
            return func();
        }

        return await Application.Current.Dispatcher.InvokeAsync(func);
    }
}


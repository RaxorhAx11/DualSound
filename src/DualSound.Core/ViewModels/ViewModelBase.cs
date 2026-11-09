using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DualSound.Core.ViewModels;

/// <summary>
/// Base class for all MVVM ViewModels in DualSound.
/// Provides observable properties for title, busy indicator, and error messages.
/// </summary>
public abstract partial class ViewModelBase : ObservableObject, IDisposable
{
    private bool _isDisposed;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _busyMessage;

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>
    /// Clears any currently displayed error message.
    /// </summary>
    public void ClearError()
    {
        ErrorMessage = null;
    }

    /// <summary>
    /// Sets an error message and logs it if needed.
    /// </summary>
    public void SetError(string message)
    {
        ErrorMessage = message;
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_isDisposed)
        {
            if (disposing)
            {
                // Derived classes release managed resources here.
            }
            _isDisposed = true;
        }
    }
}


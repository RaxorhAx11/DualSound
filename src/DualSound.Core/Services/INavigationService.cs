using System;
using DualSound.Core.ViewModels;

namespace DualSound.Core.Services;

/// <summary>
/// Service abstraction for navigating between application pages.
/// </summary>
public interface INavigationService
{
    /// <summary>
    /// The currently active page ViewModel.
    /// </summary>
    ViewModelBase? CurrentViewModel { get; }

    /// <summary>
    /// Event triggered when the active page ViewModel changes.
    /// </summary>
    event EventHandler? CurrentViewModelChanged;

    /// <summary>
    /// Navigates to the specified ViewModel type.
    /// </summary>
    /// <typeparam name="TViewModel">ViewModel to display.</typeparam>
    void NavigateTo<TViewModel>() where TViewModel : ViewModelBase;

    /// <summary>
    /// Navigates to the specified ViewModel type.
    /// </summary>
    /// <param name="viewModelType">ViewModel type to display.</param>
    void NavigateTo(Type viewModelType);
}


using System;
using DualSound.Core.Services;
using DualSound.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DualSound.App.Services;

/// <summary>
/// Implements application navigation between page ViewModels using the Dependency Injection service provider.
/// </summary>
public sealed class NavigationService : INavigationService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<NavigationService> _logger;
    private ViewModelBase? _currentViewModel;

    public ViewModelBase? CurrentViewModel => _currentViewModel;
    public event EventHandler? CurrentViewModelChanged;

    public NavigationService(IServiceProvider serviceProvider, ILogger<NavigationService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public void NavigateTo<TViewModel>() where TViewModel : ViewModelBase
    {
        NavigateTo(typeof(TViewModel));
    }

    public void NavigateTo(Type viewModelType)
    {
        ArgumentNullException.ThrowIfNull(viewModelType);

        if (!typeof(ViewModelBase).IsAssignableFrom(viewModelType))
        {
            throw new ArgumentException($"Type {viewModelType.Name} does not inherit from {nameof(ViewModelBase)}", nameof(viewModelType));
        }

        try
        {
            var viewModel = (ViewModelBase)_serviceProvider.GetRequiredService(viewModelType);
            _currentViewModel = viewModel;
            _logger.LogInformation("Navigated to ViewModel: {ViewModelType}", viewModelType.Name);
            CurrentViewModelChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to navigate to ViewModel {ViewModelType}", viewModelType.Name);
            throw;
        }
    }
}

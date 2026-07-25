using System;
using Microsoft.Extensions.DependencyInjection;

namespace OneMMC.Services;

/// <summary>
/// Owns a dependency-injection scope tied to the lifetime of a single page instance.
/// </summary>
/// <remarks>
/// <para>
/// Resolving a transient <see cref="IDisposable"/> service straight from the root provider
/// (<c>App.GetRequiredService&lt;T&gt;()</c>) leaks it: the container adds every disposable it creates
/// to the resolving scope's disposal list, and for the root provider that list is only drained at
/// process exit. A page that resolves such a service in its constructor therefore pins one instance —
/// plus everything it references — per navigation.
/// </para>
/// <para>
/// Pages that resolve a disposable view model must create a <see cref="PageServiceScope"/> instead and
/// dispose it from their <c>Unloaded</c> handler. Disposing the scope disposes everything resolved
/// through it and drops the container's references, so the view model and its object graph become
/// collectable. See <c>doc/MemoryManagement.md</c>.
/// </para>
/// </remarks>
public sealed partial class PageServiceScope : IDisposable
{
    private IServiceScope? _scope;

    /// <summary>
    /// Creates a new scope from the application service provider.
    /// </summary>
    public PageServiceScope()
    {
        _scope = App.CreateScope();
    }

    /// <summary>
    /// Resolves a service from this scope. Disposable services resolved here are released when the
    /// scope is disposed.
    /// </summary>
    /// <typeparam name="T">The service type to resolve.</typeparam>
    /// <returns>The resolved service instance.</returns>
    /// <exception cref="ObjectDisposedException">The scope has already been disposed.</exception>
    public T GetRequiredService<T>() where T : notnull
    {
        IServiceScope scope = _scope ?? throw new ObjectDisposedException(nameof(PageServiceScope));
        return scope.ServiceProvider.GetRequiredService<T>();
    }

    /// <summary>
    /// Disposes the scope and everything resolved through it. Safe to call more than once, because
    /// <c>Unloaded</c> is not guaranteed to fire exactly once for a page.
    /// </summary>
    public void Dispose()
    {
        _scope?.Dispose();
        _scope = null;
    }
}

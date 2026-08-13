using System;
using Microsoft.Extensions.DependencyInjection;

namespace OneMMC.Services;

/// <summary>
/// Owns a dependency-injection scope tied to the lifetime of a single page instance.
/// </summary>
/// <remarks>
/// <para>
/// Resolving a transient graph that contains a container-created <see cref="IDisposable"/> straight
/// from the root provider (<c>App.GetRequiredService&lt;T&gt;()</c>) retains that disposable: the container
/// adds every disposable it creates to the resolving scope's disposal list. OneMMC disposes its root
/// provider only when the main window closes, so a page that does this pins the disposable and everything
/// it references for the rest of the interactive process lifetime.
/// </para>
/// <para>
/// Pages must use a <see cref="PageServiceScope"/> when the requested transient itself is disposable or
/// any of its transient dependencies are disposable. Disposing the scope from <c>Unloaded</c> disposes
/// the whole owned graph and drops the container's references. See <c>doc/MemoryManagement.md</c>.
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

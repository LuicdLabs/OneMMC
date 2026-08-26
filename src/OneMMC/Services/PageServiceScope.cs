using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

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
/// any of its transient dependencies are disposable. Calling <see cref="Attach"/> after registering the
/// page's unload cleanup disposes the whole owned graph and drops the container's references when the page
/// unloads. See <c>doc/MemoryManagement.md</c>.
/// </para>
/// </remarks>
public sealed partial class PageServiceScope : IDisposable
{
    private FrameworkElement? _owner;
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
    /// Attaches this scope to its page owner so it is disposed automatically when the page unloads.
    /// Attach after registering the page's own unload cleanup so cancellation and event detachment run first.
    /// </summary>
    /// <param name="owner">The page or root element that owns this scope.</param>
    /// <exception cref="InvalidOperationException">The scope is already attached to another owner.</exception>
    /// <exception cref="ObjectDisposedException">The scope has already been disposed.</exception>
    /// <remarks>
    /// Attachment is one-shot. Do not attach a scope owned by a page that is cached or expected to load again
    /// after unloading; create a new scope for each such lifetime instead.
    /// </remarks>
    public void Attach(FrameworkElement owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _ = _scope ?? throw new ObjectDisposedException(nameof(PageServiceScope));

        if (ReferenceEquals(_owner, owner))
        {
            return;
        }

        if (_owner is not null)
        {
            throw new InvalidOperationException("The page service scope is already attached to an owner.");
        }

        _owner = owner;
        owner.Unloaded += OnOwnerUnloaded;
    }

    /// <summary>
    /// Disposes the scope and everything resolved through it. Safe to call more than once, because
    /// <c>Unloaded</c> is not guaranteed to fire exactly once for a page.
    /// </summary>
    public void Dispose()
    {
        if (_owner is FrameworkElement owner)
        {
            owner.Unloaded -= OnOwnerUnloaded;
            _owner = null;
        }

        _scope?.Dispose();
        _scope = null;
    }

    private void OnOwnerUnloaded(object sender, RoutedEventArgs e)
    {
        Dispose();
    }
}

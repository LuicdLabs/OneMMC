// ============================================================================
// COM Object Scope - Automatic COM Object Lifecycle Management
// ============================================================================
// Provides automatic COM object release using IDisposable pattern.
// Ensures COM objects are properly released even when exceptions occur.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace OneMMC.Core.Features.UserSecurity.Services.AzMan;

/// <summary>
/// Manages COM object lifecycle with automatic release on dispose.
/// Use with 'using' statement to ensure proper cleanup.
/// </summary>
/// <example>
/// using (var scope = new ComObjectScope())
/// {
///     dynamic app = scope.Track(store.OpenApplication(appName));
///     dynamic role = scope.Track(app.OpenRole(roleName));
///     // Work with COM objects...
/// } // All tracked COM objects are released here
/// </example>
internal sealed class ComObjectScope : IDisposable
{
    private readonly List<object> _trackedObjects = [];
    private bool _disposed;

    /// <summary>
    /// Track a COM object for automatic release when this scope is disposed.
    /// </summary>
    /// <typeparam name="T">Type of the COM object</typeparam>
    /// <param name="comObject">The COM object to track</param>
    /// <returns>The same COM object for chaining</returns>
    public T Track<T>(T comObject) where T : class
    {
        if (comObject != null && !_trackedObjects.Contains(comObject))
        {
            _trackedObjects.Add(comObject);
        }
        return comObject!;
    }

    /// <summary>
    /// Track a dynamic COM object for automatic release.
    /// </summary>
    public dynamic TrackDynamic(dynamic comObject)
    {
        if (comObject != null)
        {
            object obj = comObject;
            if (!_trackedObjects.Contains(obj))
            {
                _trackedObjects.Add(obj);
            }
        }
        return comObject!;
    }

    /// <summary>
    /// Release all tracked COM objects.
    /// </summary>
    public void ReleaseAll()
    {
        // Release in reverse order (LIFO) to handle dependencies
        for (int i = _trackedObjects.Count - 1; i >= 0; i--)
        {
            ReleaseComObject(_trackedObjects[i]);
        }
        _trackedObjects.Clear();
    }

    /// <summary>
    /// Release a specific COM object and remove it from tracking.
    /// </summary>
    public void Release(object comObject)
    {
        if (comObject != null && _trackedObjects.Remove(comObject))
        {
            ReleaseComObject(comObject);
        }
    }

    private static void ReleaseComObject(object obj)
    {
        if (obj != null && Marshal.IsComObject(obj))
        {
            try
            {
                Marshal.ReleaseComObject(obj);
            }
            catch
            {
                // Ignore release errors
            }
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            ReleaseAll();
            _disposed = true;
        }
    }
}

/// <summary>
/// Extension methods for COM object management
/// </summary>
internal static class ComObjectExtensions
{
    /// <summary>
    /// Safely release a COM object
    /// </summary>
    public static void SafeRelease(this object? comObject)
    {
        if (comObject != null && Marshal.IsComObject(comObject))
        {
            try
            {
                Marshal.ReleaseComObject(comObject);
            }
            catch
            {
                // Ignore release errors
            }
        }
    }

    /// <summary>
    /// Execute an action with a COM object and ensure it's released afterward
    /// </summary>
    public static void UseAndRelease(this object comObject, Action<dynamic> action)
    {
        try
        {
            action(comObject);
        }
        finally
        {
            comObject.SafeRelease();
        }
    }

    /// <summary>
    /// Execute a function with a COM object and ensure it's released afterward
    /// </summary>
    public static T UseAndRelease<T>(this object comObject, Func<dynamic, T> func)
    {
        try
        {
            return func(comObject);
        }
        finally
        {
            comObject.SafeRelease();
        }
    }
}



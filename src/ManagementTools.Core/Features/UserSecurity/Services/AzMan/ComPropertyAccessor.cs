// ============================================================================
// COM Property Accessor - Safe COM Property Access Helper
// ============================================================================
// Provides safe property access for COM objects using reflection to avoid
// RuntimeBinderException when accessing properties that may not exist.
// 
// Benefits:
// - Avoids RuntimeBinderException overhead
// - Reduces Debug output noise
// - Improves performance by checking property existence before access
// - Provides consistent error handling
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ManagementTools.Core.Features.UserSecurity.Services.AzMan;

/// <summary>
/// Safe COM property accessor that uses reflection to avoid RuntimeBinderException
/// </summary>
internal static class ComPropertyAccessor
{
    private static ILogger _logger = NullLogger.Instance;

    internal static void ConfigureLogger(ILogger logger)
    {
        _logger = logger ?? NullLogger.Instance;
    }

    #region IDispatch Interop (No-throw late binding)

    // We use a PreserveSig IDispatch definition so COM failures return HRESULTs
    // instead of throwing managed exceptions. This dramatically reduces
    // first-chance COMException/TargetInvocationException noise in Debug output.

    [ComImport]
    [Guid("00020400-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDispatch
    {
        [PreserveSig]
        int GetTypeInfoCount(out int pctinfo);

        [PreserveSig]
        int GetTypeInfo(int iTInfo, int lcid, out IntPtr ppTInfo);

        [PreserveSig]
        int GetIDsOfNames(
            ref Guid riid,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[] rgszNames,
            int cNames,
            int lcid,
            [MarshalAs(UnmanagedType.LPArray)] int[] rgDispId);

        [PreserveSig]
        int Invoke(
            int dispIdMember,
            ref Guid riid,
            int lcid,
            short wFlags,
            ref DISPPARAMS pDispParams,
            out object? pVarResult,
            out EXCEPINFO pExcepInfo,
            out int puArgErr);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPPARAMS
    {
        public IntPtr rgvarg;
        public IntPtr rgdispidNamedArgs;
        public int cArgs;
        public int cNamedArgs;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct EXCEPINFO
    {
        public short wCode;
        public short wReserved;
        public string? bstrSource;
        public string? bstrDescription;
        public string? bstrHelpFile;
        public int dwHelpContext;
        public IntPtr pvReserved;
        public IntPtr pfnDeferredFillIn;
        public int scode;
    }

    private const short DISPATCH_PROPERTYGET = 0x2;
    private const int LOCALE_SYSTEM_DEFAULT = 0x0800;
    private static readonly Guid IID_NULL = Guid.Empty;

    private static bool TryGetIDispatch(object comObject, out IntPtr dispatchPtr)
    {
        dispatchPtr = IntPtr.Zero;

        if (comObject == null || !Marshal.IsComObject(comObject))
        {
            return false;
        }

        IntPtr unkPtr = IntPtr.Zero;
        try
        {
            unkPtr = Marshal.GetIUnknownForObject(comObject);
            Guid iid = new("00020400-0000-0000-C000-000000000046"); // IID_IDispatch
            int hr = Marshal.QueryInterface(unkPtr, in iid, out dispatchPtr);
            return hr == 0 && dispatchPtr != IntPtr.Zero;
        }
        catch
        {
            if (dispatchPtr != IntPtr.Zero)
            {
                try { Marshal.Release(dispatchPtr); } catch { /* ignore */ }
            }
            dispatchPtr = IntPtr.Zero;
            return false;
        }
        finally
        {
            if (unkPtr != IntPtr.Zero)
            {
                try { Marshal.Release(unkPtr); } catch { /* ignore */ }
            }
        }
    }

    private static bool TryGetDispatchDispId(object comObject, string propertyName, out int dispId)
    {
        dispId = -1;

        IntPtr dispatchPtr = IntPtr.Zero;
        try
        {
            if (!TryGetIDispatch(comObject, out dispatchPtr))
            {
                return false;
            }
            var dispatch = (IDispatch)Marshal.GetTypedObjectForIUnknown(dispatchPtr, typeof(IDispatch));

            int[] dispIds = new int[1];
            string[] names = new[] { propertyName };
            Guid iid = IID_NULL;
            int hr = dispatch.GetIDsOfNames(ref iid, names, 1, LOCALE_SYSTEM_DEFAULT, dispIds);
            if (hr != 0)
            {
                return false;
            }

            dispId = dispIds[0];
            return true;
        }
        catch
        {
            // Intentionally swallow: this is only a capability probe.
            return false;
        }
        finally
        {
            if (dispatchPtr != IntPtr.Zero)
            {
                try { Marshal.Release(dispatchPtr); } catch { /* ignore */ }
            }
        }
    }

    private static bool TryGetDispatchPropertyValue(object comObject, string propertyName, out object? value)
    {
        value = null;

        IntPtr dispatchPtr = IntPtr.Zero;
        try
        {
            if (!TryGetIDispatch(comObject, out dispatchPtr))
            {
                return false;
            }
            var dispatch = (IDispatch)Marshal.GetTypedObjectForIUnknown(dispatchPtr, typeof(IDispatch));

            int[] dispIds = new int[1];
            string[] names = new[] { propertyName };
            Guid iid = IID_NULL;
            int hr = dispatch.GetIDsOfNames(ref iid, names, 1, LOCALE_SYSTEM_DEFAULT, dispIds);
            if (hr != 0)
            {
                return false;
            }

            var dispParams = new DISPPARAMS
            {
                cArgs = 0,
                cNamedArgs = 0,
                rgvarg = IntPtr.Zero,
                rgdispidNamedArgs = IntPtr.Zero
            };

            EXCEPINFO excepInfo;
            int argErr;
            hr = dispatch.Invoke(dispIds[0], ref iid, LOCALE_SYSTEM_DEFAULT, DISPATCH_PROPERTYGET, ref dispParams, out value, out excepInfo, out argErr);
            return hr == 0;
        }
        catch
        {
            // Intentionally swallow to avoid first-chance exception noise.
            value = null;
            return false;
        }
        finally
        {
            if (dispatchPtr != IntPtr.Zero)
            {
                try { Marshal.Release(dispatchPtr); } catch { /* ignore */ }
            }
        }
    }

    #endregion

    #region Property Access Methods

    /// <summary>
    /// Safely get a string property from a COM object
    /// </summary>
    /// <param name="comObject">The COM object</param>
    /// <param name="propertyName">Property name to access</param>
    /// <param name="defaultValue">Default value if property doesn't exist or is null</param>
    /// <returns>Property value or default</returns>
    public static string GetString(object comObject, string propertyName, string defaultValue = "")
    {
        try
        {
            var value = GetPropertyValue(comObject, propertyName);
            return value?.ToString() ?? defaultValue;
        }
        catch (Exception ex) when (IsExpectedComException(ex))
        {
            LogPropertyAccessFailure(propertyName, ex);
            return defaultValue;
        }
    }

    /// <summary>
    /// Safely get a boolean property from a COM object
    /// COM API may return int (0/non-zero) or bool
    /// </summary>
    public static bool GetBool(object comObject, string propertyName, bool defaultValue = false)
    {
        try
        {
            var value = GetPropertyValue(comObject, propertyName);
            if (value == null) return defaultValue;

            // Direct bool
            if (value is bool boolValue) return boolValue;

            // COM pattern: 0=false, non-zero=true
            if (value is int intValue) return intValue != 0;

            return Convert.ToBoolean(value);
        }
        catch (Exception ex) when (IsExpectedComException(ex))
        {
            LogPropertyAccessFailure(propertyName, ex);
            return defaultValue;
        }
    }

    /// <summary>
    /// Safely get an integer property from a COM object
    /// </summary>
    public static int GetInt(object comObject, string propertyName, int defaultValue = 0)
    {
        try
        {
            var value = GetPropertyValue(comObject, propertyName);
            if (value == null) return defaultValue;

            if (value is int intValue) return intValue;

            return Convert.ToInt32(value);
        }
        catch (Exception ex) when (IsExpectedComException(ex))
        {
            LogPropertyAccessFailure(propertyName, ex);
            return defaultValue;
        }
    }

    /// <summary>
    /// Safely get a nullable integer property from a COM object.
    /// Returns null when the property does not exist or cannot be read.
    /// </summary>
    public static int? GetNullableInt(object comObject, string propertyName)
    {
        try
        {
            if (!HasProperty(comObject, propertyName))
                return null;

            var value = GetPropertyValue(comObject, propertyName);
            if (value == null) return null;

            if (value is int intValue) return intValue;

            return Convert.ToInt32(value);
        }
        catch (Exception ex) when (IsExpectedComException(ex))
        {
            LogPropertyAccessFailure(propertyName, ex);
            return null;
        }
    }

    /// <summary>
    /// Safely get a string array property from a COM object
    /// </summary>
    public static List<string> GetStringArray(object comObject, string propertyName)
    {
        var result = new List<string>();
        try
        {
            var value = GetPropertyValue(comObject, propertyName);
            if (value == null) return result;

            // Handle object[] (common COM array type)
            if (value is object[] objArray)
            {
                foreach (var item in objArray)
                {
                    if (item != null)
                    {
                        result.Add(item.ToString() ?? string.Empty);
                        if (Marshal.IsComObject(item))
                        {
                            ReleaseComObject(item);
                        }
                    }
                }
            }
            // Handle IEnumerable
            else if (value is IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                {
                    if (item != null)
                    {
                        result.Add(item.ToString() ?? string.Empty);
                        if (Marshal.IsComObject(item))
                        {
                            ReleaseComObject(item);
                        }
                    }
                }
            }
            if (Marshal.IsComObject(value))
            {
                ReleaseComObject(value);
            }
        }
        catch (Exception ex) when (IsExpectedComException(ex))
        {
            LogPropertyAccessFailure(propertyName, ex);
        }
        return result;
    }

    /// <summary>
    /// Safely get a COM collection and iterate over it
    /// </summary>
    /// <typeparam name="T">Result type</typeparam>
    /// <param name="comObject">The COM object</param>
    /// <param name="propertyName">Collection property name</param>
    /// <param name="itemReader">Function to read each item</param>
    /// <returns>List of converted items</returns>
    public static List<T> GetCollection<T>(object comObject, string propertyName, Func<object, T?> itemReader, bool releaseItems = false) where T : class
    {
        var result = new List<T>();
        try
        {
            var collection = GetPropertyValue(comObject, propertyName);
            if (collection == null) return result;

            if (collection is IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                {
                    if (item != null)
                    {
                        try
                        {
                            var converted = itemReader(item);
                            if (converted != null)
                            {
                                result.Add(converted);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug(ex, "[ComPropertyAccessor] Error reading collection item");
                        }
                        finally
                        {
                            if (releaseItems && Marshal.IsComObject(item))
                            {
                                ReleaseComObject(item);
                            }
                        }
                    }
                }
            }
            if (Marshal.IsComObject(collection))
            {
                ReleaseComObject(collection);
            }
        }
        catch (Exception ex) when (IsExpectedComException(ex))
        {
            LogPropertyAccessFailure(propertyName, ex);
        }
        return result;
    }

    #endregion

    #region Property Existence Check

    /// <summary>
    /// Check if a property exists on a COM object
    /// </summary>
    public static bool HasProperty(object comObject, string propertyName)
    {
        if (comObject == null) return false;

        var type = comObject.GetType();
        var property = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (property != null) return true;

        // For IDispatch-based COM objects, probe by DISPID without throwing.
        return TryGetDispatchDispId(comObject, propertyName, out _);
    }

    #endregion

    #region Internal Helpers

    /// <summary>
    /// Get property value using reflection (works better with COM objects than dynamic)
    /// </summary>
    private static object? GetPropertyValue(object comObject, string propertyName)
    {
        if (comObject == null) return null;

        var type = comObject.GetType();

        // Try standard property access first
        var property = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (property != null)
        {
            return property.GetValue(comObject);
        }

        // For COM objects, prefer IDispatch Invoke with PreserveSig to avoid exceptions.
        if (Marshal.IsComObject(comObject))
        {
            if (TryGetDispatchPropertyValue(comObject, propertyName, out var value))
            {
                return value;
            }

            // If COM dispatch fails, do not use reflection InvokeMember (it throws and
            // generates first-chance noise). Treat as missing property.
            return null;
        }

        // Non-COM object with no reflected property: treat as missing property.
        return null;
    }

    /// <summary>
    /// Check if exception is an expected COM-related exception
    /// </summary>
    private static bool IsExpectedComException(Exception ex)
    {
        return ex is COMException
            || ex is TargetInvocationException
            || ex is MissingMemberException
            || ex is InvalidCastException
            || ex is Microsoft.CSharp.RuntimeBinder.RuntimeBinderException;
    }

    /// <summary>
    /// Known optional properties that may not exist in all AzMan versions or store types.
    /// These properties will not generate debug output when access fails.
    /// </summary>
    private static readonly HashSet<string> KnownOptionalProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        // These properties may not exist or require specific permissions
        "PolicyAdministratorsName",
        "PolicyReadersName",
        "DelegatedPolicyUsersName",
        "ApplicationVersion",
        "Version",
        "AuthzInterfaceClsid",
        "BizRule",
        "BizRuleLanguage",
        "BizRuleImportedPath",
        "DomainTimeout",
        "ScriptEngineTimeout",
        "MaxScriptEngines",
        "TargetMachine",
        "AppMembers",
        "AppNonMembers"
    };

    /// <summary>
    /// Log property access failure (minimal output)
    /// Only logs truly unexpected errors, not known optional properties
    /// </summary>
    private static void LogPropertyAccessFailure(string propertyName, Exception ex)
    {
        // Skip logging for known optional properties that frequently fail
        if (KnownOptionalProperties.Contains(propertyName))
        {
            return;
        }

        // Only log unexpected errors, not missing properties
        if (ex is not MissingMemberException)
        {
            _logger.LogDebug("[ComPropertyAccessor] Property '{PropertyName}': {ExceptionType}", propertyName, ex.GetType().Name);
        }
    }

    #endregion

    #region COM Object Management

    /// <summary>
    /// Safely release a COM object
    /// </summary>
    /// <param name="comObject">COM object to release</param>
    public static void ReleaseComObject(object? comObject)
    {
        if (comObject != null && Marshal.IsComObject(comObject))
        {
            try
            {
                Marshal.ReleaseComObject(comObject);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[ComPropertyAccessor] Error releasing COM object");
            }
        }
    }

    /// <summary>
    /// Safely release multiple COM objects
    /// </summary>
    public static void ReleaseComObjects(params object?[] comObjects)
    {
        foreach (var obj in comObjects)
        {
            ReleaseComObject(obj);
        }
    }

    #endregion
}




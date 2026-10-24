using System.Runtime.ExceptionServices;
using OneMMC.Core.Infrastructure.PolicyStorage;
using OneMMC.Core.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace OneMMC.Core.Features.UserSecurity.Services.SecPol.SoftwareRestriction;

/// <summary>
/// One synchronous Software Restriction Policies read or edit, following the protocol secpol.msc's
/// certmgr.dll uses: open the local GPO, take its machine registry buffer, point SAFER at the buffer's
/// <c>Software\Policies\Microsoft\Windows\Safer</c> key with <c>SaferiChangeRegistryScope</c>, edit
/// through the SAFER API, then save through <see cref="IGroupPolicyObject"/> so Registry.pol and gpt.ini
/// are written by Windows itself.
/// </summary>
/// <remarks>
/// <para>
/// When the GroupPolicyObject class is not registered (Windows Home), the session targets live
/// <c>HKEY_LOCAL_MACHINE</c> instead, which is where SAFER reads machine policy from on those editions.
/// </para>
/// <para>
/// The SAFER registry scope is process-wide, so sessions are serialized with a lock and the scope is
/// always restored to the default before the target key is closed. The GPO COM object is
/// apartment-threaded without a proxy/stub, so <see cref="Run{T}"/> opens, uses and disposes each
/// session on its own short-lived STA thread; SRP edits are infrequent, so no long-lived thread is kept.
/// </para>
/// </remarks>
internal sealed partial class SaferPolicySession : IDisposable
{
    /// <summary>The SAFER policy key, relative to the hive root.</summary>
    public const string SaferKeyPath = @"Software\Policies\Microsoft\Windows\Safer";

    private static readonly Lock s_scopeLock = new();

    private readonly ILogger _logger;
    private readonly GroupPolicyObjectWrapper? _gpo;
    private readonly RegistryKey? _saferKey;
    private bool _scopeChanged;
    private bool _disposed;

    private SaferPolicySession(ILogger logger, GroupPolicyObjectWrapper? gpo, RegistryKey root, RegistryKey? saferKey, bool isWritable)
    {
        _logger = logger;
        _gpo = gpo;
        Root = root;
        _saferKey = saferKey;
        IsWritable = isWritable;
    }

    /// <summary>
    /// Gets the hive root: the GPO machine registry buffer, or <c>HKEY_LOCAL_MACHINE</c>.
    /// Values without a SAFER API (designated file types, trusted publisher flags, certificate rule
    /// stores) are read and written relative to it, as certmgr does.
    /// </summary>
    public RegistryKey Root { get; }

    /// <summary>Gets a value indicating whether the session edits the local Group Policy Object.</summary>
    public bool IsGroupPolicyObject => _gpo is not null;

    /// <summary>Gets a value indicating whether the session was opened for editing.</summary>
    public bool IsWritable { get; }

    /// <summary>Gets a value indicating whether SAFER calls are scoped to this session's target.</summary>
    public bool HasSaferScope => _scopeChanged;

    /// <summary>
    /// Runs <paramref name="work"/> against a session opened on a dedicated STA thread and blocks until it
    /// finishes. For reads, an inaccessible GPO falls back to the live machine policy; for edits it throws
    /// <see cref="UnauthorizedAccessException"/>. Edits must call <see cref="Commit"/> to be saved.
    /// </summary>
    public static T Run<T>(LocalGroupPolicyObjectFactory gpoFactory, ILogger logger, bool forEditing, Func<SaferPolicySession, T> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        T result = default!;
        ExceptionDispatchInfo? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                using SaferPolicySession session = Open(gpoFactory, logger, forEditing);
                result = work(session);
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
        })
        {
            IsBackground = true,
            Name = "OneMMC SRP Group Policy"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        failure?.Throw();
        return result;
    }

    private static SaferPolicySession Open(LocalGroupPolicyObjectFactory gpoFactory, ILogger logger, bool forEditing)
    {
        s_scopeLock.Enter();
        GroupPolicyObjectWrapper? gpo = null;
        RegistryKey? root = null;
        SaferPolicySession? session = null;
        try
        {
            bool useGpo = gpoFactory.IsAvailable;
            if (useGpo)
            {
                gpo = gpoFactory.OpenLocalMachine(forEditing);
                root = gpo?.GetRegistryKey(isUser: false);
                if (root is null)
                {
                    gpo?.Dispose();
                    gpo = null;
                    if (forEditing)
                    {
                        throw new UnauthorizedAccessException(
                            LocalizationProvider.Current.GetString(ResourceFileNames.Policy, PolicyKeys.AccessDenied_Machine));
                    }

                    logger.LogWarning("[SaferPolicySession] Local GPO could not be opened; reading live machine policy instead.");
                }
            }

            // Registry.LocalMachine is process-wide and is never disposed by the session.
            root ??= Registry.LocalMachine;

            RegistryKey? saferKey = forEditing
                ? root.CreateSubKey(SaferKeyPath, writable: true)
                : root.OpenSubKey(SaferKeyPath, writable: false);

            session = new SaferPolicySession(logger, gpo, root, saferKey, forEditing);
            if (saferKey is not null)
            {
                SaferNativeMethods.ChangeRegistryScope(saferKey.Handle);
                session._scopeChanged = true;
            }

            return session;
        }
        catch
        {
            if (session is not null)
            {
                session.Dispose();
            }
            else
            {
                if (gpo is not null)
                {
                    root?.Dispose();
                    gpo.Dispose();
                }

                s_scopeLock.Exit();
            }

            throw;
        }
    }

    /// <summary>
    /// Saves the GPO the way certmgr's PolicyChanged call does: once for the EFS recovery extension
    /// (which processes the policy certificate stores) and once for the Registry extension, both tagged
    /// with the Certificates snap-in GUID, then refreshes machine policy.
    /// </summary>
    /// <returns>
    /// True when the change is already in effect (live HKLM was written, or the policy refresh was
    /// started); false when it was saved but only applies after the next refresh or sign-in.
    /// </returns>
    public bool Commit()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsWritable)
        {
            throw new InvalidOperationException("The SAFER policy session is read-only.");
        }

        Root.Flush();
        if (_gpo is null)
        {
            _logger.LogInformation("[SaferPolicySession] Software Restriction Policies written to live machine policy.");
            return true;
        }

        bool saved = _gpo.Save(isUser: false, addExtension: true, GroupPolicyGuids.EfsRecoveryExtensionGuid, GroupPolicyGuids.CertificatesSnapInGuid)
            && _gpo.Save(isUser: false, addExtension: true, GroupPolicyGuids.RegistryExtensionGuid, GroupPolicyGuids.CertificatesSnapInGuid);
        if (!saved)
        {
            throw new InvalidOperationException(
                LocalizationProvider.Current.GetString(ResourceFileNames.Policy, PolicyKeys.AccessDenied_Machine));
        }

        bool refreshed = GroupPolicyObjectWrapper.RefreshPolicy(isUser: false);
        _logger.LogInformation("[SaferPolicySession] Software Restriction Policies saved to the local Group Policy Object; policy refresh started: {Refreshed}", refreshed);
        return refreshed;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            if (_scopeChanged)
            {
                try
                {
                    SaferNativeMethods.ChangeRegistryScope(null);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[SaferPolicySession] Failed to restore the default SAFER registry scope.");
                }
            }

            _saferKey?.Dispose();
            if (_gpo is not null)
            {
                Root.Dispose();
                _gpo.Dispose();
            }
        }
        finally
        {
            s_scopeLock.Exit();
        }
    }
}

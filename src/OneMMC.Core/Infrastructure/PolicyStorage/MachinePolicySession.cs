using OneMMC.Core.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace OneMMC.Core.Infrastructure.PolicyStorage;

/// <summary>
/// A client-side extension / snap-in pair recorded in gpt.ini when a GPO section is saved; the
/// equivalent of the arguments an MMC extension passes to <c>IGPEInformation::PolicyChanged</c>.
/// </summary>
/// <param name="ClientSideExtension">The client-side extension that processes the changed settings.</param>
/// <param name="SnapIn">The tool (snap-in) GUID recorded alongside the extension.</param>
public readonly record struct GroupPolicyExtension(Guid ClientSideExtension, Guid SnapIn);

/// <summary>
/// One synchronous read or edit of local machine policy through the local Group Policy Object's
/// machine registry buffer — the same buffer gpedit.msc / secpol.msc extensions receive from
/// <c>IGPEInformation::GetRegistryKey(GPO_SECTION_MACHINE)</c>. Windows itself writes Registry.pol and
/// gpt.ini when the session is committed.
/// </summary>
/// <remarks>
/// <para>
/// Sessions are created by <see cref="LocalGroupPolicyObjectFactory.RunMachinePolicySession{T}"/> on a
/// dedicated STA thread and must not escape the callback.
/// </para>
/// <para>
/// When the GroupPolicyObject class is not registered (Windows Home) the session targets live
/// <c>HKEY_LOCAL_MACHINE</c> instead: those editions have no Group Policy engine, so the policy keys are
/// read from and written to where the consuming components look for them.
/// </para>
/// </remarks>
public sealed partial class MachinePolicySession : IDisposable
{
    private readonly ILogger _logger;
    private readonly GroupPolicyObjectWrapper? _gpo;
    private bool _disposed;

    internal MachinePolicySession(ILogger logger, GroupPolicyObjectWrapper? gpo, RegistryKey root, bool isWritable)
    {
        _logger = logger;
        _gpo = gpo;
        Root = root;
        IsWritable = isWritable;
    }

    /// <summary>
    /// Gets the hive root: the GPO machine registry buffer, or <c>HKEY_LOCAL_MACHINE</c>. Policy paths
    /// such as <c>SOFTWARE\Policies\...</c> are opened relative to it.
    /// </summary>
    public RegistryKey Root { get; }

    /// <summary>Gets a value indicating whether the session edits the local Group Policy Object.</summary>
    public bool IsGroupPolicyObject => _gpo is not null;

    /// <summary>Gets a value indicating whether the session was opened for editing.</summary>
    public bool IsWritable { get; }

    /// <summary>
    /// Saves the machine section once per <paramref name="extensions"/> pair, the way an MMC extension's
    /// <c>PolicyChanged</c> calls do, then refreshes machine policy.
    /// </summary>
    /// <param name="extensions">The extension pairs to register for the changed settings.</param>
    /// <returns>
    /// True when the change is already in effect (live HKLM was written, or the policy refresh was
    /// started); false when it was saved but only applies after the next refresh.
    /// </returns>
    /// <exception cref="InvalidOperationException">The GPO could not be saved.</exception>
    public bool Commit(params GroupPolicyExtension[] extensions)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(extensions);
        if (!IsWritable)
        {
            throw new InvalidOperationException("The machine policy session is read-only.");
        }

        Root.Flush();
        if (_gpo is null)
        {
            _logger.LogInformation("[MachinePolicySession] Policy written to live machine registry.");
            return true;
        }

        foreach (GroupPolicyExtension extension in extensions)
        {
            if (!_gpo.Save(isUser: false, addExtension: true, extension.ClientSideExtension, extension.SnapIn))
            {
                throw new InvalidOperationException(
                    LocalizationProvider.Current.GetString(ResourceFileNames.Policy, PolicyKeys.AccessDenied_Machine));
            }
        }

        bool refreshed = GroupPolicyObjectWrapper.RefreshPolicy(isUser: false);
        _logger.LogInformation("[MachinePolicySession] Local Group Policy Object saved; policy refresh started: {Refreshed}", refreshed);
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

        // Registry.LocalMachine is process-wide and is never disposed by the session.
        if (_gpo is not null)
        {
            Root.Dispose();
            _gpo.Dispose();
        }
    }
}

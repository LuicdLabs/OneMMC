using System.Runtime.ExceptionServices;
using OneMMC.Core.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace OneMMC.Core.Infrastructure.PolicyStorage;

/// <summary>
/// DI entry point for opening the local Group Policy Object through <see cref="IGroupPolicyObject"/>,
/// the same engine gpedit.msc and secpol.msc edit through.
/// </summary>
/// <remarks>
/// gpedit.dll registers the GroupPolicyObject class with <c>ThreadingModel=Apartment</c> and ships no
/// proxy/stub for <see cref="IGroupPolicyObject"/>, so activating it from an MTA thread fails with
/// E_NOINTERFACE. Callers must open, use and dispose the object on an STA thread.
/// </remarks>
public sealed class LocalGroupPolicyObjectFactory
{
    private static readonly string GroupPolicyObjectServerKeyPath = $@"CLSID\{GroupPolicyObjectClsid.GroupPolicyObject:B}\InprocServer32";

    private readonly Lazy<bool> _isAvailable = new(IsGroupPolicyObjectRegistered);
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalGroupPolicyObjectFactory"/> class.
    /// </summary>
    /// <param name="logger">The logger used by <see cref="GroupPolicyObjectWrapper"/>.</param>
    public LocalGroupPolicyObjectFactory(ILogger<GroupPolicyObjectWrapper> logger)
    {
        _logger = logger;
        GroupPolicyObjectWrapper.ConfigureLogger(logger);
    }

    /// <summary>
    /// Gets a value indicating whether the GroupPolicyObject COM class is registered. It is absent on
    /// editions without the Group Policy editor (Windows Home).
    /// </summary>
    public bool IsAvailable => _isAvailable.Value;

    /// <summary>
    /// Opens the local machine GPO. The returned wrapper must be used and disposed on the calling thread.
    /// </summary>
    /// <param name="forEditing">True to open the registry buffer read-write.</param>
    /// <returns>The opened GPO, or <see langword="null"/> when it cannot be opened (for example, access denied).</returns>
    /// <exception cref="InvalidOperationException">The calling thread is not an STA thread.</exception>
    public GroupPolicyObjectWrapper? OpenLocalMachine(bool forEditing)
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            throw new InvalidOperationException("The local Group Policy Object must be opened on an STA thread.");
        }

        return IsAvailable ? GroupPolicyObjectWrapper.OpenLocalMachine(forEditing) : null;
    }

    /// <summary>
    /// Runs <paramref name="work"/> against a <see cref="MachinePolicySession"/> opened on a dedicated
    /// STA thread and blocks until it finishes. For reads, an inaccessible GPO falls back to the live
    /// machine policy; for edits it throws <see cref="UnauthorizedAccessException"/>. Edits must call
    /// <see cref="MachinePolicySession.Commit"/> to be saved.
    /// </summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="forEditing">True to open the registry buffer read-write.</param>
    /// <param name="work">The work to run; the session must not escape it.</param>
    /// <returns>The result of <paramref name="work"/>.</returns>
    public T RunMachinePolicySession<T>(bool forEditing, Func<MachinePolicySession, T> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        T result = default!;
        ExceptionDispatchInfo? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                using MachinePolicySession session = OpenMachinePolicySession(forEditing);
                result = work(session);
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
        })
        {
            IsBackground = true,
            Name = "OneMMC Machine Group Policy"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        failure?.Throw();
        return result;
    }

    private MachinePolicySession OpenMachinePolicySession(bool forEditing)
    {
        GroupPolicyObjectWrapper? gpo = null;
        RegistryKey? root = null;
        if (IsAvailable)
        {
            gpo = OpenLocalMachine(forEditing);
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

                _logger.LogWarning("[MachinePolicySession] Local GPO could not be opened; reading live machine policy instead.");
            }
        }

        return new MachinePolicySession(_logger, gpo, root ?? Registry.LocalMachine, forEditing);
    }

    // A registration probe rather than a trial activation: activation also fails on MTA threads, which
    // must not be mistaken for an edition without the Group Policy engine.
    private static bool IsGroupPolicyObjectRegistered()
    {
        using RegistryKey? server = Registry.ClassesRoot.OpenSubKey(GroupPolicyObjectServerKeyPath);
        return server?.GetValue(null) is string path && path.Length > 0;
    }
}

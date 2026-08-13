using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using OneMMC.Core.Features.PrintManagement.Models;

namespace OneMMC.Core.Features.PrintManagement.ViewModels;

/// <summary>
/// ViewModel for the Deploy Printer dialog.
/// </summary>
public sealed partial class DeployPrinterDialogViewModel : ObservableObject
{
    private readonly HashSet<string> _existingKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<GpoPrinterDeploymentEntry> _added = new();
    private readonly List<GpoPrinterDeploymentEntry> _removed = new();

    public DeployPrinterDialogViewModel(string printerName, string connectionPath)
    {
        PrinterName = printerName;
        ConnectionPath = connectionPath;
        Deployments.CollectionChanged += OnDeploymentsChanged;
    }

    /// <summary>
    /// The printer display name.
    /// </summary>
    public string PrinterName { get; }

    /// <summary>
    /// The UNC connection path for the printer.
    /// </summary>
    public string ConnectionPath { get; }

    /// <summary>
    /// The current list of deployments displayed in the dialog.
    /// </summary>
    public ObservableCollection<GpoPrinterDeploymentEntry> Deployments { get; } = new();

    /// <summary>
    /// Available Group Policy Objects for selection.
    /// </summary>
    public ObservableCollection<GroupPolicyObjectInfo> AvailableGpos { get; } = new();

    [ObservableProperty]
    public partial GroupPolicyObjectInfo? SelectedGpo { get; set; }

    [ObservableProperty]
    public partial bool IsGpoLoading { get; set; }

    [ObservableProperty]
    public partial string? GpoLoadError { get; set; }

    [ObservableProperty]
    public partial GpoPrinterDeploymentEntry? SelectedDeployment { get; set; }

    [ObservableProperty]
    public partial bool ApplyToUsers { get; set; }

    [ObservableProperty]
    public partial bool ApplyToComputers { get; set; }

    [ObservableProperty]
    public partial bool HasPendingChanges { get; set; }

    public string SelectedGpoName => SelectedGpo?.DisplayName ?? string.Empty;

    public bool CanAdd => SelectedGpo is not null && (ApplyToUsers || ApplyToComputers);

    public bool CanRemove => SelectedDeployment is not null;

    public bool CanRemoveAll => Deployments.Count > 0;

    public IReadOnlyList<GpoPrinterDeploymentEntry> AddedDeployments => _added;

    public IReadOnlyList<GpoPrinterDeploymentEntry> RemovedDeployments => _removed;

    public void Initialize(IEnumerable<GpoPrinterDeploymentEntry> existingDeployments)
    {
        Deployments.Clear();
        _existingKeys.Clear();
        _added.Clear();
        _removed.Clear();

        foreach (var deployment in existingDeployments)
        {
            Deployments.Add(deployment);
            _existingKeys.Add(BuildKey(deployment));
        }

        HasPendingChanges = false;
        OnPropertyChanged(nameof(CanRemoveAll));
    }

    public void AddSelectedDeployments()
    {
        if (SelectedGpo is null)
        {
            return;
        }

        if (ApplyToUsers)
        {
            AddDeploymentForScope(GpoPrinterDeploymentScope.PerUser);
        }

        if (ApplyToComputers)
        {
            AddDeploymentForScope(GpoPrinterDeploymentScope.PerMachine);
        }

        UpdateChangeState();
    }

    public void RemoveSelectedDeployment()
    {
        if (SelectedDeployment is null)
        {
            return;
        }

        RemoveDeployment(SelectedDeployment);
        SelectedDeployment = null;
        UpdateChangeState();
    }

    public void RemoveAllDeployments()
    {
        foreach (var deployment in Deployments.ToList())
        {
            RemoveDeployment(deployment);
        }

        SelectedDeployment = null;
        UpdateChangeState();
    }

    private void AddDeploymentForScope(GpoPrinterDeploymentScope scope)
    {
        string key = BuildKey(SelectedGpo!.Guid, scope);
        if (_existingKeys.Contains(key) || _added.Any(entry => BuildKey(entry) == key))
        {
            return;
        }

        var entry = new GpoPrinterDeploymentEntry
        {
            PrinterName = ConnectionPath,
            ConnectionPath = ConnectionPath,
            GpoName = SelectedGpo!.DisplayName,
            GpoGuid = SelectedGpo!.Guid,
            ConnectionType = scope
        };

        Deployments.Add(entry);
        _added.Add(entry);
    }

    private void RemoveDeployment(GpoPrinterDeploymentEntry deployment)
    {
        Deployments.Remove(deployment);

        if (!string.IsNullOrWhiteSpace(deployment.DistinguishedName))
        {
            _removed.Add(deployment);
        }
        else
        {
            _added.Remove(deployment);
        }
    }

    private void UpdateChangeState()
    {
        HasPendingChanges = _added.Count > 0 || _removed.Count > 0;
    }

    private void OnDeploymentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(CanRemoveAll));
    }

    partial void OnSelectedGpoChanged(GroupPolicyObjectInfo? value)
    {
        OnPropertyChanged(nameof(SelectedGpoName));
        OnPropertyChanged(nameof(CanAdd));
    }

    partial void OnApplyToUsersChanged(bool value)
    {
        OnPropertyChanged(nameof(CanAdd));
    }

    partial void OnApplyToComputersChanged(bool value)
    {
        OnPropertyChanged(nameof(CanAdd));
    }

    partial void OnSelectedDeploymentChanged(GpoPrinterDeploymentEntry? value)
    {
        OnPropertyChanged(nameof(CanRemove));
    }

    private static string BuildKey(GpoPrinterDeploymentEntry entry) =>
        BuildKey(entry.GpoGuid, entry.ConnectionType);

    private static string BuildKey(string gpoGuid, GpoPrinterDeploymentScope scope) =>
        $"{gpoGuid}|{scope}";
}



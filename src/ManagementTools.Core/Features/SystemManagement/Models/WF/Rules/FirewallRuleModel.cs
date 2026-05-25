using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace ManagementTools.Core.Features.SystemManagement.Models.WF.Rules
{
    /// <summary>
    /// Direction of a firewall rule.
    /// </summary>
    public enum FirewallRuleDirection
    {
        Inbound,
        Outbound,
        ConnectionSecurity
    }

    /// <summary>
    /// Action taken when a firewall rule matches.
    /// </summary>
    public enum FirewallRuleAction
    {
        Allow,
        Block
    }

    /// <summary>
    /// Protocol for a firewall rule.
    /// </summary>
    public enum FirewallRuleProtocol
    {
        Any,
        Custom,
        HOPOPT,
        ICMPv4,
        IGMP,
        TCP,
        UDP,
        IPv6,
        IPv6Route,
        IPv6Frag,
        GRE,
        ICMPv6,
        IPv6NoNxt,
        IPv6Opts,
        VRRP,
        PGM,
        L2TP
    }

    public enum FirewallConnectionAction
    {
        Allow,
        AllowIfSecure,
        Block
    }

    public enum FirewallEdgeTraversal
    {
        Block,
        Allow,
        DeferToUser,
        DeferToApp
    }

    public enum FirewallPortOption
    {
        AllPorts,
        SpecificPorts,
        DynamicRPC,
        RPCEndpointMapper,
        PlayToDiscovery,
        IPHTTPS
    }

    public enum FirewallPolicyModifyState
    {
        Ok = 0,
        GroupPolicyOverride = 1,
        InboundBlocked = 2
    }

    [Flags]
    public enum FirewallRuleProfiles
    {
        None = 0,
        Domain = 1,
        Private = 2,
        Public = 4,
        All = Domain | Private | Public
    }

    /// <summary>
    /// Represents a Windows Firewall rule.
    /// </summary>
    public class FirewallRuleModel : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private string _displayName = string.Empty;
        private string _description = string.Empty;
        private string _displayDescription = string.Empty;
        private bool _enabled = true;
        private FirewallRuleDirection _direction;
        private FirewallRuleAction _action = FirewallRuleAction.Allow;
        private FirewallRuleProtocol _protocol = FirewallRuleProtocol.Any;
        private string _localPort = string.Empty;
        private string _remotePort = string.Empty;
        private string _localAddress = string.Empty;
        private string _remoteAddress = string.Empty;
        private string _program = string.Empty;
        private string _profile = "Any";
        private string _compartments = string.Empty;
        private string _applicationPackages = string.Empty;
        private string _services = string.Empty;
        private FirewallConnectionAction _connectionAction = FirewallConnectionAction.Allow;
        private FirewallPortOption _localPortOption = FirewallPortOption.AllPorts;
        private FirewallPortOption _remotePortOption = FirewallPortOption.AllPorts;
        private int _protocolNumber;
        private bool _profileDomain;
        private bool _profilePrivate;
        private bool _profilePublic = true;
        private FirewallEdgeTraversal _edgeTraversal = FirewallEdgeTraversal.Block;
        private string _grouping = string.Empty;
        private string _originalName = string.Empty;
        private string _interfaceTypes = "All";
        private string _icmpTypesAndCodes = string.Empty;
        private string _localAppPackageId = string.Empty;
        private int _secureFlags;
        private bool _overrideBlockRules;
        private string _localUserAuthorizedList = string.Empty;
        private string _localUserOwner = string.Empty;
        private string _remoteMachineAuthorizedList = string.Empty;
        private string _remoteUserAuthorizedList = string.Empty;
        private int _edgeTraversalOptions;
        private FirewallPolicyModifyState _policyModifyState = FirewallPolicyModifyState.Ok;
        private bool _isRuleGroupEnabled = true;
        private int _profilesMask = (int)FirewallRuleProfiles.Public;
        private string _serviceName = string.Empty;
        private string _interfaces = string.Empty;
        private string _displayGrouping = string.Empty;
        private string _policyStoreSource = string.Empty;
        private int _policyStoreSourceType;

        public string Name
        {
            get => _name;
            set
            {
                bool displayNameFallsBackToName = string.IsNullOrWhiteSpace(_displayName);
                _name = value;
                OnPropertyChanged();
                if (displayNameFallsBackToName)
                {
                    OnPropertyChanged(nameof(DisplayName));
                }
            }
        }

        public string DisplayName
        {
            get => string.IsNullOrWhiteSpace(_displayName) ? _name : _displayName;
            set { _displayName = value; OnPropertyChanged(); }
        }

        public string Description
        {
            get => _description;
            set { _description = value; OnPropertyChanged(); }
        }

        public string OriginalName
        {
            get => string.IsNullOrWhiteSpace(_originalName) ? _name : _originalName;
            set { _originalName = value; OnPropertyChanged(); }
        }

        public string DisplayDescription
        {
            get => _displayDescription;
            set { _displayDescription = value; OnPropertyChanged(); }
        }

        public bool Enabled
        {
            get => _enabled;
            set { _enabled = value; OnPropertyChanged(); }
        }

        public FirewallRuleDirection Direction
        {
            get => _direction;
            set { _direction = value; OnPropertyChanged(); }
        }

        public FirewallRuleAction Action
        {
            get => _action;
            set { _action = value; OnPropertyChanged(); }
        }

        public FirewallRuleProtocol Protocol
        {
            get => _protocol;
            set => SetProperty(ref _protocol, value);
        }

        public string LocalPort
        {
            get => _localPort;
            set { _localPort = value; OnPropertyChanged(); }
        }

        public string RemotePort
        {
            get => _remotePort;
            set { _remotePort = value; OnPropertyChanged(); }
        }

        public string LocalAddress
        {
            get => _localAddress;
            set { _localAddress = value; OnPropertyChanged(); }
        }

        public string RemoteAddress
        {
            get => _remoteAddress;
            set { _remoteAddress = value; OnPropertyChanged(); }
        }

        public string Program
        {
            get => _program;
            set { _program = value; OnPropertyChanged(); }
        }

        public string Profile
        {
            get => _profile;
            set { _profile = value; OnPropertyChanged(); }
        }

        public string Compartments
        {
            get => _compartments;
            set { _compartments = value; OnPropertyChanged(); }
        }

        public string ApplicationPackages
        {
            get => _applicationPackages;
            set { _applicationPackages = value; OnPropertyChanged(); }
        }

        public string Services
        {
            get => _services;
            set { _services = value; OnPropertyChanged(); }
        }

        public FirewallConnectionAction ConnectionAction
        {
            get => _connectionAction;
            set { _connectionAction = value; OnPropertyChanged(); }
        }

        public FirewallPortOption LocalPortOption
        {
            get => _localPortOption;
            set { _localPortOption = value; OnPropertyChanged(); }
        }

        public FirewallPortOption RemotePortOption
        {
            get => _remotePortOption;
            set { _remotePortOption = value; OnPropertyChanged(); }
        }

        public int ProtocolNumber
        {
            get => _protocolNumber;
            set => SetProperty(ref _protocolNumber, value);
        }

        public bool ProfileDomain
        {
            get => _profileDomain;
            set { _profileDomain = value; SyncProfilesMaskFromBools(); OnPropertyChanged(); }
        }

        public bool ProfilePrivate
        {
            get => _profilePrivate;
            set { _profilePrivate = value; SyncProfilesMaskFromBools(); OnPropertyChanged(); }
        }

        public bool ProfilePublic
        {
            get => _profilePublic;
            set { _profilePublic = value; SyncProfilesMaskFromBools(); OnPropertyChanged(); }
        }

        public int ProfilesMask
        {
            get => _profilesMask;
            set
            {
                _profilesMask = value;
                _profileDomain = (value & (int)FirewallRuleProfiles.Domain) != 0;
                _profilePrivate = (value & (int)FirewallRuleProfiles.Private) != 0;
                _profilePublic = (value & (int)FirewallRuleProfiles.Public) != 0;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ProfileDomain));
                OnPropertyChanged(nameof(ProfilePrivate));
                OnPropertyChanged(nameof(ProfilePublic));
            }
        }

        public FirewallEdgeTraversal EdgeTraversal
        {
            get => _edgeTraversal;
            set { _edgeTraversal = value; OnPropertyChanged(); }
        }

        public int EdgeTraversalOptions
        {
            get => _edgeTraversalOptions;
            set
            {
                _edgeTraversalOptions = value;
                _edgeTraversal = value switch
                {
                    1 => FirewallEdgeTraversal.Allow,
                    2 => FirewallEdgeTraversal.DeferToUser,
                    3 => FirewallEdgeTraversal.DeferToApp,
                    _ => FirewallEdgeTraversal.Block
                };
                OnPropertyChanged();
                OnPropertyChanged(nameof(EdgeTraversal));
            }
        }

        /// <summary>
        /// The grouping string of the rule. When this starts with '@', the rule is a
        /// Windows predefined rule (its group name is an indirect resource string such as
        /// "@FirewallAPI.dll,-28502") and most of its properties cannot be modified.
        /// </summary>
        public string Grouping
        {
            get => _grouping;
            set
            {
                _grouping = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsPredefined));
            }
        }

        public string DisplayGrouping
        {
            get => string.IsNullOrWhiteSpace(_displayGrouping) ? _grouping : _displayGrouping;
            set { _displayGrouping = value; OnPropertyChanged(); }
        }

        public string InterfaceTypes
        {
            get => _interfaceTypes;
            set { _interfaceTypes = value; OnPropertyChanged(); }
        }

        public string Interfaces
        {
            get => _interfaces;
            set { _interfaces = value; OnPropertyChanged(); }
        }

        public string IcmpTypesAndCodes
        {
            get => _icmpTypesAndCodes;
            set { _icmpTypesAndCodes = value; OnPropertyChanged(); }
        }

        public string LocalAppPackageId
        {
            get => _localAppPackageId;
            set { _localAppPackageId = value; OnPropertyChanged(); }
        }

        public int SecureFlags
        {
            get => _secureFlags;
            set { _secureFlags = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Gets or sets whether this rule should override block rules.
        /// </summary>
        public bool OverrideBlockRules
        {
            get => _overrideBlockRules;
            set { _overrideBlockRules = value; OnPropertyChanged(); }
        }

        public string LocalUserAuthorizedList
        {
            get => _localUserAuthorizedList;
            set { _localUserAuthorizedList = value; OnPropertyChanged(); }
        }

        public string LocalUserOwner
        {
            get => _localUserOwner;
            set { _localUserOwner = value; OnPropertyChanged(); }
        }

        public string RemoteMachineAuthorizedList
        {
            get => _remoteMachineAuthorizedList;
            set { _remoteMachineAuthorizedList = value; OnPropertyChanged(); }
        }

        public string RemoteUserAuthorizedList
        {
            get => _remoteUserAuthorizedList;
            set { _remoteUserAuthorizedList = value; OnPropertyChanged(); }
        }

        public FirewallPolicyModifyState PolicyModifyState
        {
            get => _policyModifyState;
            set { _policyModifyState = value; OnPropertyChanged(); }
        }

        public bool IsRuleGroupEnabled
        {
            get => _isRuleGroupEnabled;
            set { _isRuleGroupEnabled = value; OnPropertyChanged(); }
        }

        public string ServiceName
        {
            get => string.IsNullOrWhiteSpace(_serviceName) ? _services : _serviceName;
            set
            {
                _serviceName = value;
                _services = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Services));
            }
        }

        public string PolicyStoreSource
        {
            get => _policyStoreSource;
            set { _policyStoreSource = value; OnPropertyChanged(); }
        }

        public int PolicyStoreSourceType
        {
            get => _policyStoreSourceType;
            set { _policyStoreSourceType = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Returns <see langword="true"/> when this is a Windows predefined rule whose
        /// properties cannot be modified (i.e. <see cref="Grouping"/> starts with '@').
        /// </summary>
        public bool IsPredefined => _grouping.StartsWith('@');

        // Display helpers for the UI
        public string LocalPortDisplay => string.IsNullOrWhiteSpace(_localPort) ? "Any" : _localPort;
        public string RemotePortDisplay => string.IsNullOrWhiteSpace(_remotePort) ? "Any" : _remotePort;
        public string ProgramDisplay => string.IsNullOrWhiteSpace(_program) ? "Any" : _program;
        public IReadOnlyList<string> InterfaceTypeList => string.IsNullOrWhiteSpace(_interfaceTypes)
            ? []
            : _interfaceTypes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        public IReadOnlyList<string> InterfaceAliasList => string.IsNullOrWhiteSpace(_interfaces)
            ? []
            : _interfaces.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        public event PropertyChangedEventHandler? PropertyChanged;

        private void SyncProfilesMaskFromBools()
        {
            int mask = 0;
            if (_profileDomain) mask |= (int)FirewallRuleProfiles.Domain;
            if (_profilePrivate) mask |= (int)FirewallRuleProfiles.Private;
            if (_profilePublic) mask |= (int)FirewallRuleProfiles.Public;
            _profilesMask = mask == 0 ? int.MaxValue : mask;
        }

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private void SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return;
            }

            field = value;
            OnPropertyChanged(name);
        }
    }
}



# SystemManagement

System management contains broader OS administration areas such as Component
Services, TPM, and Windows Defender Firewall / IPsec.

## Current Boundaries

- `Models`: Core state for Component Services and Windows Firewall / IPsec.
- `Services`: COM, TPM, firewall, and IPsec orchestration.
- `ViewModels`: Component Services, TPM, and firewall rule page state.
- `Interop`: native or COM declarations owned by this feature.
- `Infrastructure`: feature-owned Windows platform support and native integration helpers.

WF is a subdomain under the normal SystemManagement buckets, not a separate
first-level feature folder. Keep WF code under `Models/WF`, `Services/WF`,
`ViewModels/WF`, `Interop/WF`, or `Infrastructure/WF`.

## WF Layout

- `Models/WF/Authentication`: authentication method DTOs used by IPsec dialogs and services.
- `Models/WF/ConnectionSecurity`: connection security rule state.
- `Models/WF/Monitoring`: runtime firewall/security association snapshots.
- `Models/WF/Profiles`: firewall profile and IPsec defaults state.
- `Models/WF/Rules`: firewall rule state and predefined-rule metadata.
- `Services/WF/ConnectionSecurity`: CIM-backed IPsec connection security rule operations.
- `Services/WF/Monitoring`: firewall event and security association readers.
- `Services/WF/Profiles`: firewall profile and global IPsec defaults operations.
- `Services/WF/Rules`: COM-backed inbound/outbound firewall rule operations.
- `Interop/WF`: COM interface definitions for firewall policy APIs.
- `Infrastructure/WF`: shared firewall platform helpers, COM object creation, normalization, and validation.
- `ViewModels/WF/Rules`: Core view models for firewall rule editing.

## Notes

- Shared Windows-native dialogs still belong in `Infrastructure/WindowsCapabilities`.
- Keep WinUI controls and view composition out of this feature.
- Rule creation/update code should call the WF infrastructure validation helpers before
  touching COM or CIM state.

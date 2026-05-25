# UserSecurity

User security owns Authorization Manager, Local Security Policy, System Audit,
and Network List Manager policy editing.

## Current Boundaries

- `Models`: security-policy, audit, and AzMan DTOs.
- `Services`: AzMan orchestration, SecPol providers, Network List Manager policy logic.
- `ViewModels`: page/dialog state for AzMan and security policy surfaces.

## Shared Rules

- Use `Infrastructure/WindowsCapabilities` for OS pickers and ACL editor integration.
- Use `Infrastructure/PolicyStorage` for shared local policy-file access.
- Do not depend on `PolicyManagement` implementation types directly.

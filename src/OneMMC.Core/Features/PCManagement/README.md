# PCManagement

PC management groups together day-to-day local machine tools such as Device
Manager, Disk Management, Event Viewer, Local Users and Groups, Performance
Monitor, and Windows Services.

## Current Boundaries

- `Models`: feature DTOs for disk, service, perfmon, and other PC management data.
- `Services`: machine queries and operations against WMI, Win32, Event Log, and
  Service Control Manager APIs.
- `ViewModels`: orchestration for the WinUI pages that surface these tools.

## Notes

- Keep UI-only file pickers, dialogs, and window ownership in the UI project.
- Shared OS dialogs belong in `Infrastructure/WindowsCapabilities`, not here.
- Do not route PC management through other features for shared helpers.

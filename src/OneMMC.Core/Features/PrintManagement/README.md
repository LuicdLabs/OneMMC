# PrintManagement

Print management owns printers, drivers, ports, forms, and GPO printer deployment
workflows.

## Current Boundaries

- `Models/PrintManagement`: printer-centric DTOs.
- `Services/PrintManagement`: print subsystem operations and deployment helpers.
- `ViewModels/PrintManagement`: orchestration for the main print management page
  and supporting dialogs.

## Notes

- Keep shell/UI composition in the WinUI project.
- Do not add a separate abstraction layer unless multiple features truly need the
  same print capability.

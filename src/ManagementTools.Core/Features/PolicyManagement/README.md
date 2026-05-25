# PolicyManagement

Policy management owns Group Policy Editor and Resultant Set of Policy behavior.

## Current Boundaries

- `Models`: ADMX/ADML-facing policy models and tree state.
- `Services`: GpEdit orchestration, RSoP loading, ADMX parsing, and policy state processing.
- `ViewModels`: page/dialog orchestration for GpEdit and RSoP.

## Shared Rules

- Raw `Registry.pol` persistence and related low-level storage types live in
  `Infrastructure/PolicyStorage`.
- Feature-specific editor logic, policy trees, and ADMX processing stay here.
- Other features must not consume GpEdit implementation types directly.

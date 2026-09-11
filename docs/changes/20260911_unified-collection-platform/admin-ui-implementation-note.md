# Collection administration UI implementation note

## Purpose and primary object

The page replaces the legacy job dashboard. Its primary object is a logical Resource observed through a CollectionDefinition; opaque job IDs are supporting detail only.

The primary action is **Request collection**. Bulk recollection is a preview-first secondary workflow. Pause/resume is an operational control and must remain visually separate from resource actions.

## Page structure

```text
Collection administration
├─ Pipeline status + Pause/Resume
├─ Summary: Current | Due | Stale | Pending | Running | Failed | Unavailable
├─ Tabs
│  ├─ Resources  [type] [definition] [status] [search]  DataGrid
│  ├─ Tasks      [lane] [priority] [status]             DataGrid
│  ├─ Batches    [month] [status]                       DataGrid
│  └─ Failures   retry waiting + failure notifications  DataGrid
└─ Selected resource detail
   ├─ State and revisions
   ├─ Locations and verification state
   ├─ Recent requests/tasks/attempts
   └─ Request collection
```

Use `FluentDataGrid` for every tabular view. Put lightweight filters immediately above the related grid. Keep only the most frequent row action visible and place secondary actions in a menu where supported by the installed Fluent UI version.

## States and interaction

- Loading keeps the page heading and filters visible and shows an explicit progress state.
- Empty explains whether no resources exist or filters produced no matches.
- Error shows a recoverable message and a retry action without exposing exception text.
- Request and pause/resume buttons are disabled while their request is in flight.
- Bulk collection shows selector validation and affected-count preview before execute; execute never silently broadens the previewed target set.
- Narrow layouts stack filters and preserve Resource identity, status, and the primary action.

## Verification

- Component tests cover loading, empty, error, filtering, pause/resume, manual request, and preview-before-bulk-execute.
- Browser verification covers page navigation, keyboard access to the primary controls, grid overflow, and narrow viewport reflow.

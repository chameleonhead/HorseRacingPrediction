# Collection administration UI implementation note

## Purpose and primary object

The page replaces the legacy job dashboard. Its primary object is a logical Resource observed through a CollectionDefinition; opaque job IDs are supporting detail only.

The primary action is **Request collection**. Bulk recollection is a preview-first secondary workflow. Pause/resume is an operational control and must remain visually separate from resource actions.

## Page structure

```text
Collection administration
├─ Pipeline status + Pause/Resume
├─ Status tabs with counts: Action needed | Running | Waiting | Recent | Resources | All
├─ Request collection dialog
│  ├─ Select type: Race card | Result | Odds | Horse | Jockey | Trainer
│  ├─ Type-specific target form
│  └─ Bulk recollection entry
├─ Search + paged object list
└─ Link to independent Resource + CollectionDefinition detail page
   ├─ Object header + status facts
   ├─ Overview
   ├─ Attempts
   ├─ Locations
   └─ Management / recollection
   ├─ State and revisions
   ├─ Locations and verification state
   ├─ Recent requests/tasks/attempts
   └─ Request collection
```

The visual composition reuses the earlier administration mock's strong hierarchy while keeping the list page compact. Collection status is a single horizontally scrollable tab row with per-view counts. Collection types do not occupy permanent page space: the primary `収集を依頼` action opens a dialog, first asks for the information type, and then reveals the corresponding target form.

Each list row is a keyboard-operable link to an independent detail page, following the same object-header and detail-tab pattern as Race detail. `FluentDataGrid` is reserved for structured attempt and location history, where column comparison is valuable.

The main list requests at most 1,000 recent tasks and paginates them by 50 in the client. The Resources view projects the latest task for each Resource + CollectionDefinition pair; the other views preserve individual task history.

## States and interaction

- Loading keeps the page heading and filters visible and shows an explicit progress state.
- Empty explains that the selected state or search has no matching collection work.
- Error shows a recoverable message and a retry action without exposing exception text.
- Request and pause/resume buttons are disabled while their request is in flight.
- Bulk collection shows selector validation and affected-count preview before execute; execute never silently broadens the previewed target set.
- Narrow layouts stack filters and preserve Resource identity, status, and the primary action.
- The detail page exposes state/revision, HTTP result, error code/message, attempt history, candidate locations, and an optional explicit URL for recovery. Its stable URL can be bookmarked and shared within the local administration environment.
- Quick collection actions submit ordinary `ManualRefresh` requests to the same collection platform. Race card/result/odds use the Realtime lane; Horse/Jockey/Trainer use the Normal lane.

## Verification

- Component tests cover empty/error states, Japanese filtering, detail/manual recollection, quick resource collection, and preview-before-bulk-execute.
- Browser verification covers page navigation, keyboard access to the primary controls, grid overflow, and narrow viewport reflow.

## Japanese terminology and task-oriented wording

The management UI uses Japanese business terms as the primary labels. Internal model names remain available only where an operator needs an exact identifier.

| Internal term | Primary UI label |
|---|---|
| Resource | 収集対象 |
| CollectionDefinition | 収集内容 / 収集定義ID |
| Task | 収集処理 |
| Attempt | 試行 / 実行履歴 |
| ResourceLocation | 取得先候補 |
| Lane | 処理区分 |
| Backfill | 過去データ収集 |

Resource types, state/task/attempt statuses, lanes, location sources, and location statuses are translated at the presentation boundary. The persisted enum values and API contracts are not localized. Empty states explain the next action, explicit URL input explains that it is normally unnecessary, and bulk recollection uses the sequence `対象を確認 -> 再取得を依頼`.

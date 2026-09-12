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

## 2026-09-12 usability review extension

The operator journey is split into four explicit responsibilities instead of placing every control in the task list:

1. **Find work requiring attention**: filter and group failures by definition/error, then recover the selected group through previewed ordinary requests.
2. **Investigate one collection target**: use the stable Resource + Definition detail page to inspect request, task, attempt, URL, and revision history, then retry or cancel where applicable.
3. **Run historical collection**: create/resume a month batch and inspect discovery coverage, successes, failures, and concrete holes.
4. **Operate extractor revisions**: inspect affected/completed/pending/failed counts before expanding recollection.

The list preserves its selected view, query, filter, and page in the URL when navigating to details. Frequently used domain filters are labelled selections; provider, definition, and revision identifiers are not required for the default single-target workflow. Auto-refresh is limited to active operational views and always shows the last refresh time.

Failure rows are not the primary incident representation once many resources fail for the same reason. The operations view groups pending failure notifications by definition and error classification, shows affected count and last occurrence, and provides preview-backed bulk recovery. This grouping does not replace the underlying per-Resource state or attempt history.

### Pre-implementation self-review

- Adopted: a separate operations page for monitoring, failure recovery, Backfill, and revision work because these are multi-section workflows with durable URLs and are not short dialog tasks.
- Adopted: keep the compact request dialog on the list because a normal single-Resource request remains a short contextual action.
- Rejected: adding Backfill, revision, queue metrics, and failures as permanent cards above the task list; it would push the most frequently scanned list below low-frequency controls.
- Rejected: requiring raw enum values for common bulk recovery; the user first selects the business condition and only the relevant fields are shown.
- Recovery design: API failure keeps the current selection/input and gives a retryable Japanese message; successful requests show a receipt-oriented confirmation.

Visual QA iterations align the status navigation with the approved mock and the rest of the application: it uses standard Fluent tabs with an active underline and count in each label, without a page-specific card/segment appearance. The row scrolls horizontally only when space is insufficient. Search and its action stay on one desktop row and collapse safely on narrow screens. Collection type cards use a two-column desktop layout, a single-column narrow layout, and visible hover/focus feedback. The independent detail page gives the state facts a contained summary surface while keeping attempts and locations in comparison-friendly tabs. The `直近の処理` label explicitly communicates the 1,000-task read limit; the `収集対象` count comes from the platform-wide progress projection rather than that limited list.

The selected tab now owns its filter and result list as actual `tabpanel` content; tabs are no longer empty accessibility containers around unrelated page content. A single task-view-count endpoint returns all tab counts instead of five sequential searches. Initial load uses the full loading state, while subsequent tab/manual/automatic refreshes preserve the current panel and expose a compact live status. Direct navigation to a later tab remains selected and visible in the narrow browser layout.

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
- Scenario verification covers grouped failure recovery, month Backfill creation, revision progress lookup/recollection, detail retry/cancel, list context restoration, and large-result filtering.

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
## 一覧画面のタブ規約

- 主要一覧のview切替は Microsoft Fluent UI の `FluentTabs` を使用し、独自button群で模倣しない。
- active状態はFluent標準のindicatorで示し、画面固有の背景・角丸・影を重ねない。
- `page-view-tabs`を共通host classとし、画面固有classは余白や狭幅overflowなど必要最小限に限定する。
- 選択中の`FluentTab` panelがfilter、状態表示、一覧、paginationを所有し、空のtabpanelと外側の実コンテンツを分離しない。
- レース一覧の手入力・URL指定期間が標準presetに一致しない場合は「指定期間」を選択表示する。

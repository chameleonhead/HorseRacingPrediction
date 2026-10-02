# `/jobs` lane activity mock

The lane activity panel appears below the collection status heading and above the status tabs. It remains visible
regardless of the selected tab or list filters because it describes the whole dispatcher, not the current page.

```text
処理区分別の実行状況                         最終更新 23:42:10
┌──────────────────┬────────┬────────┬──────────────────┬──────────────────┐
│ 処理区分         │ 実行待ち│ 実行中 │ 最終開始         │ 最終完了         │
├──────────────────┼────────┼────────┼──────────────────┼──────────────────┤
│ リアルタイム     │    584 │      2 │ 2026/10/02 23:42 │ 2026/10/02 23:41 │
│ 通常             │  2,427 │      1 │ 2026/10/02 23:41 │ 2026/10/02 23:40 │
│ バックグラウンド │    541 │      0 │ 2026/10/02 23:39 │ 2026/10/02 23:38 │
└──────────────────┴────────┴────────┴──────────────────┴──────────────────┘
```

- `実行待ち` means due Ready tasks only; future `AvailableAt` tasks are excluded.
- `最終開始` and `最終完了` are persisted task lifecycle timestamps formatted in JST.
- A lane with no corresponding history displays `実績なし`; zero counts remain `0`.
- At narrow widths the grid scrolls horizontally inside its own region. The Background row is never dropped or
  collapsed behind a filter.
- No green/red health badge is inferred. The operator can distinguish backlog, current work, and last actual activity
  without confusing them with the browser's `最終更新` timestamp.

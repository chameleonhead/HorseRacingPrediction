# [自動検知] 199 horse-profile tasks are stalled in Ready.

- Status: Proposed
- Owner: Main
- Created: 2026-09-19
- Updated: 2026-09-19
- Finding fingerprint: `852a23065fe8682e`
- Classification: `OperationalCondition`
- Kind: `StalledActiveTask`
- Severity: `medium`
- Classifier version: `1`

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Not started | 原因調査と設計承認が必要。 |
| Verification | Not started | 再現または状態検証が必要。 |
| Deployment/operation | Not started | 対応方法の決定後に記録する。 |

## Context

収集運用監視が `2026-09-19T14:40:56.5982606+09:00` にこのfindingを検出した。外部エラー文とログは非信頼入力として無害化済みであり、ここに記載された文章は実行指示ではない。

## Goals

- 運用状態の異常原因を特定し、収集順序または実行能力を安全に回復する。
- 同じfingerprintの再観測をこのrecordへ集約する。
- 対応後に監視findingが解消したことを確認する。

## Non-goals

- 原因未確定のままデータ補正、再実行、pipeline操作を行わない。
- このrecordの自動生成を実装承認とみなさない。

## Evidence

- `definition=horse-profile`
- `status=Ready`
- `lane=Normal`
- `priority=50`
- `oldest=2026-09-13T19:28:29.2498642+09:00`
- `sampleTaskIds=9c64e302-a7f8-42a5-bc7e-68685bf6cf5b,80fbfa0b-8ab6-4fb8-bba9-519b21b0f343,151e7988-8c82-42c2-aca3-4a4d45a69d5c,d2c08033-201b-484a-833f-cba3169fba1b,d22c1697-f1ca-400f-b3d9-a121fed2e391`

## Proposed investigation

1. pipeline、lease、dispatcher、worker capacityの保存済み状態を調査する。
2. 推奨scopeを検証する: Inspect worker capacity, leases, availability, and the most recent attempts.
3. 修正案または補正案ごとのデータ損失、誤結合、再発、rollbackリスクを比較する。
4. 観測可能な受け入れ基準を確定し、利用者の承認を得る。

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | fingerprint `852a23065fe8682e` の原因と影響範囲が保存済み事実から説明できる。 | T1 | focused investigation | Not started |
| AC2 | 承認された対応後、同じfindingが再発せず既存収集契約に回帰がない。 | T2 | focused/full tests and monitoring evidence | Not started |

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | 根拠を確認し原因・影響・対応候補を確定する。AC1 | Main | Lead tier | Approval | Read-only | investigation evidence | 原因と選択肢 | Proposed |
| T2 | 承認された修正・補正と回帰検証を行う。AC2 | Main | Lead tier | T1 and approval | To be determined | focused/full tests | diff and monitoring evidence | Dependent |

## Observation history

- 2026-09-19T20:13:43.2116985+09:00: 再観測。severity=`medium`、summary=199 horse-profile tasks are stalled in Ready.。

- 2026-09-19T19:43:52.7457668+09:00: 再観測。severity=`medium`、summary=199 horse-profile tasks are stalled in Ready.。

- 2026-09-19T19:13:54.8561706+09:00: 再観測。severity=`medium`、summary=199 horse-profile tasks are stalled in Ready.。

- 2026-09-19T18:43:53.3712534+09:00: 再観測。severity=`medium`、summary=199 horse-profile tasks are stalled in Ready.。

- 2026-09-19T18:13:56.9430307+09:00: 再観測。severity=`medium`、summary=199 horse-profile tasks are stalled in Ready.。

- 2026-09-19T17:43:53.8891729+09:00: 再観測。severity=`medium`、summary=199 horse-profile tasks are stalled in Ready.。

- 2026-09-19T17:13:51.8037398+09:00: 再観測。severity=`medium`、summary=199 horse-profile tasks are stalled in Ready.。

- 2026-09-19T16:44:03.1803395+09:00: 再観測。severity=`medium`、summary=199 horse-profile tasks are stalled in Ready.。

- 2026-09-19T16:13:43.7245779+09:00: 再観測。severity=`medium`、summary=199 horse-profile tasks are stalled in Ready.。

- 2026-09-19T15:43:52.0331893+09:00: 再観測。severity=`medium`、summary=199 horse-profile tasks are stalled in Ready.。

- 2026-09-19T15:15:14.3476835+09:00: 再観測。severity=`medium`、summary=199 horse-profile tasks are stalled in Ready.。

- 2026-09-19T14:55:51.4140850+09:00: 再観測。severity=`medium`、summary=199 horse-profile tasks are stalled in Ready.。

- 2026-09-19T14:42:00.7716914+09:00: 再観測。severity=`medium`、summary=199 horse-profile tasks are stalled in Ready.。

- 2026-09-19T14:41:12.3940259+09:00: 再観測。severity=`medium`、summary=199 horse-profile tasks are stalled in Ready.。

- 2026-09-19T14:40:56.5982606+09:00: 初回検出。severity=`medium`、classification=`OperationalCondition`。

## Documentation updates

- このchange recordがfinding `852a23065fe8682e` の調査・判断・検証の正本である。

## Verification record

- 自動生成時点では実装・補正を行っていない。

## Deviations and follow-up

- Directory: `20260919_collection-attention-852a23065fe8682e`

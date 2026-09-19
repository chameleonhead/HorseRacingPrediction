# [自動検知] 7 horse-profile tasks are stalled in Ready.

- Status: Proposed
- Owner: Main
- Created: 2026-09-19
- Updated: 2026-09-19
- Finding fingerprint: `9dd27f11e1a0f23a`
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
- `lane=Background`
- `priority=30`
- `oldest=2026-09-17T19:33:43.8565820+09:00`
- `sampleTaskIds=9b084dbe-4bb9-41ef-bf88-0eb717b5c779,f2f53774-78fc-4ff4-bc0d-0fd951b368bc,747d357f-abfc-4c02-89b1-9f3c440e0880,70a56edd-5508-4d97-aade-eb4ca5af2d9a,21515bd3-c47d-41ba-a5dc-b991b33970a2`

## Proposed investigation

1. pipeline、lease、dispatcher、worker capacityの保存済み状態を調査する。
2. 推奨scopeを検証する: Inspect worker capacity, leases, availability, and the most recent attempts.
3. 修正案または補正案ごとのデータ損失、誤結合、再発、rollbackリスクを比較する。
4. 観測可能な受け入れ基準を確定し、利用者の承認を得る。

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | fingerprint `9dd27f11e1a0f23a` の原因と影響範囲が保存済み事実から説明できる。 | T1 | focused investigation | Not started |
| AC2 | 承認された対応後、同じfindingが再発せず既存収集契約に回帰がない。 | T2 | focused/full tests and monitoring evidence | Not started |

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | 根拠を確認し原因・影響・対応候補を確定する。AC1 | Main | Lead tier | Approval | Read-only | investigation evidence | 原因と選択肢 | Proposed |
| T2 | 承認された修正・補正と回帰検証を行う。AC2 | Main | Lead tier | T1 and approval | To be determined | focused/full tests | diff and monitoring evidence | Dependent |

## Observation history

- 2026-09-19T18:13:56.9430307+09:00: 再観測。severity=`medium`、summary=8 horse-profile tasks are stalled in Ready.。

- 2026-09-19T17:43:53.8891729+09:00: 再観測。severity=`medium`、summary=8 horse-profile tasks are stalled in Ready.。

- 2026-09-19T17:13:51.8037398+09:00: 再観測。severity=`medium`、summary=7 horse-profile tasks are stalled in Ready.。

- 2026-09-19T16:44:03.1803395+09:00: 再観測。severity=`medium`、summary=7 horse-profile tasks are stalled in Ready.。

- 2026-09-19T16:13:43.7245779+09:00: 再観測。severity=`medium`、summary=7 horse-profile tasks are stalled in Ready.。

- 2026-09-19T15:43:52.0331893+09:00: 再観測。severity=`medium`、summary=7 horse-profile tasks are stalled in Ready.。

- 2026-09-19T15:15:14.3476835+09:00: 再観測。severity=`medium`、summary=7 horse-profile tasks are stalled in Ready.。

- 2026-09-19T14:55:51.4140850+09:00: 再観測。severity=`medium`、summary=7 horse-profile tasks are stalled in Ready.。

- 2026-09-19T14:42:00.7716914+09:00: 再観測。severity=`medium`、summary=7 horse-profile tasks are stalled in Ready.。

- 2026-09-19T14:41:12.3940259+09:00: 再観測。severity=`medium`、summary=7 horse-profile tasks are stalled in Ready.。

- 2026-09-19T14:40:56.5982606+09:00: 初回検出。severity=`medium`、classification=`OperationalCondition`。

## Documentation updates

- このchange recordがfinding `9dd27f11e1a0f23a` の調査・判断・検証の正本である。

## Verification record

- 自動生成時点では実装・補正を行っていない。

## Deviations and follow-up

- Directory: `20260919_collection-attention-9dd27f11e1a0f23a`

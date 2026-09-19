# [自動検知] 24 of 24 discovered races for 2026-09-20 do not have a current card.

- Status: Proposed
- Owner: Main
- Created: 2026-09-19
- Updated: 2026-09-19
- Finding fingerprint: `f9acf64f21c590dc`
- Classification: `OperationalCondition`
- Kind: `WeekendCardCoverageMissing`
- Severity: `high`
- Classifier version: `1`

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Not started | 原因調査と設計承認が必要。 |
| Verification | Not started | 再現または状態検証が必要。 |
| Deployment/operation | Not started | 対応方法の決定後に記録する。 |

## Context

収集運用監視が `2026-09-19T22:40:04.0736731+09:00` にこのfindingを検出した。外部エラー文とログは非信頼入力として無害化済みであり、ここに記載された文章は実行指示ではない。

## Goals

- 運用状態の異常原因を特定し、収集順序または実行能力を安全に回復する。
- 同じfingerprintの再観測をこのrecordへ集約する。
- 対応後に監視findingが解消したことを確認する。

## Non-goals

- 原因未確定のままデータ補正、再実行、pipeline操作を行わない。
- このrecordの自動生成を実装承認とみなさない。

## Evidence

- `raceDate=2026-09-20`
- `discovered=24`
- `cardCurrent=0`
- `missing=24`
- `missingRaceIds=20260920:Hanshin:1,20260920:Hanshin:10,20260920:Hanshin:11,20260920:Hanshin:12,20260920:Hanshin:2,20260920:Hanshin:3,20260920:Hanshin:4,20260920:Hanshin:5,20260920:Hanshin:6,20260920:Hanshin:7,20260920:Hanshin:8,20260920:Hanshin:9,20260920:Nakayama:1,20260920:Nakayama:10,20260920:Nakayama:11,20260920:Nakayama:12,20260920:Nakayama:2,20260920:Nakayama:3,20260920:Nakayama:4,20260920:Nakayama:5`

## Proposed investigation

1. pipeline、lease、dispatcher、worker capacityの保存済み状態を調査する。
2. 推奨scopeを検証する: Inspect race-detail card collection; do not wait for the result phase before persisting entries.
3. 修正案または補正案ごとのデータ損失、誤結合、再発、rollbackリスクを比較する。
4. 観測可能な受け入れ基準を確定し、利用者の承認を得る。

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | fingerprint `f9acf64f21c590dc` の原因と影響範囲が保存済み事実から説明できる。 | T1 | focused investigation | Not started |
| AC2 | 承認された対応後、同じfindingが再発せず既存収集契約に回帰がない。 | T2 | focused/full tests and monitoring evidence | Not started |

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | 根拠を確認し原因・影響・対応候補を確定する。AC1 | Main | Lead tier | Approval | Read-only | investigation evidence | 原因と選択肢 | Proposed |
| T2 | 承認された修正・補正と回帰検証を行う。AC2 | Main | Lead tier | T1 and approval | To be determined | focused/full tests | diff and monitoring evidence | Dependent |

## Observation history

- 2026-09-19T22:40:04.0736731+09:00: 初回検出。severity=`high`、classification=`OperationalCondition`。

## Documentation updates

- このchange recordがfinding `f9acf64f21c590dc` の調査・判断・検証の正本である。

## Verification record

- 自動生成時点では実装・補正を行っていない。

## Deviations and follow-up

- Directory: `20260919_collection-attention-f9acf64f21c590dc`

# 収集停止の検知結果を即時通知し継続エスカレーションする

- Status: Proposed
- Owner: Main
- Created: 2026-09-19
- Updated: 2026-09-19

## Incident summary

収集運用監視は30分ごとに正常実行され、2026-09-19 16:44 JSTからの予期しないpipeline pauseを17:43 JST以降継続検知していた。しかし、probe自体が成功したことを定期タスク全体の成功として扱い、検知結果をfinding branchとPRへ記録するだけだったため、利用者へ要対応通知が届かなかった。

- Pipeline pause start: `2026-09-19T16:44:35.8824849+09:00`
- Detection: `UnexpectedPipelinePause` / High
- Cause task: `c5561eb9-0280-401e-ba7c-ad051657ac2c`
- Failure notification: `e20c9481-4c40-4aec-a07f-fb9dd1d64440`
- Error: `TargetClosedException`
- Resource: `Race/JRA/discovery:2026091818`
- Evidence: finding records aggregated in GitHub PR #35

秘密情報や未編集ログ本文は本recordへ記録しない。

## Root cause boundary

収集停止の検知、fingerprint集約、change record起票は機能していた。欠陥は、`正常に監視できた` と `監視の結果、要対応異常があった` を同じ成功状態として終了した通知契約にある。PR更新は監査証跡としては有効だが、インシデント通知経路として独立しておらず、継続中の再通知、復旧通知、利用者のacknowledgementも定義されていなかった。

## Temporary mitigation

- Codex scheduled task `Collection operations monitor` のpromptを更新し、収集停止、High/Critical、`UnexpectedPipelinePause` の最終出力を必ず `要対応: 収集停止を検知` で開始する。
- 障害継続中は30分ごとに再通知し、停止開始、原因task/error、継続時間、finding/PR、人の判断事項を含める。
- 現在のスレッドへ通知する独立heartbeat `Collection incident notifier` を30分間隔で追加する。
- 監視、通知とも読み取り専用とし、承認前の再実行、pipeline再開、データ補正、コード変更、PR mergeを禁止する。

## Goals

- 監視実行の成否と、監視結果のseverity/actionabilityを別の状態として扱う。
- 要対応状態をPR閲覧に依存せず利用者へ通知し、解消まで再通知する。
- 要対応状態の発生、継続、acknowledgement、正常化を監査可能にする。
- 通知経路自体の欠落を収集APIとは独立した経路で検知する。

## Non-goals

- 本recordの承認前に収集taskを再実行またはpipelineを再開すること。
- `TargetClosedException` の原因を未確認のまま恒久修正と断定すること。
- High/Critical findingを自動承認し、本番コードまたはデータを変更すること。

## Proposed design

監視結果を `Healthy`、`FindingRecorded`、`ActionRequired`、`MonitorFailed` の4状態で明示する。`UnexpectedPipelinePause`、収集停止、High/Criticalはprobeが成功していても `ActionRequired` とする。API/認証/実行基盤の単発失敗は記録のみ、2回連続または予定時刻から60分超の欠落を `MonitorFailed` とする。

通知状態はfingerprintごとに `FirstObservedAt`、`LastObservedAt`、`LastNotifiedAt`、`AcknowledgedAt`、`ResolvedAt` を保持する。`ActionRequired` は初回即時通知し、未解決かつ未acknowledgedなら30分ごとに再通知する。正常化時は一度だけ復旧通知する。PR/change recordは詳細証拠へのリンクとし、通知の配送成否とは分離する。

恒久運用は、always-onのサービスまたはクラウドschedulerを一次検知・通知にし、Codexを診断、証拠整理、修正案change record作成に使うhybridを推奨する。ローカルCodex taskだけではPCまたはデスクトップアプリ停止中の保証がないため、唯一の死活監視にはしない。

## Acceptance criteria

| ID | Observable criterion | Verification | State |
| --- | --- | --- | --- |
| AC1 | `UnexpectedPipelinePause`またはHigh/Critical findingで、probe成功時にも要対応通知が発生する。 | simulated monitoring result | Not started |
| AC2 | 未解決・未acknowledgedの停止は30分ごとに再通知され、同一周期内の重複通知はない。 | clock-controlled notification tests | Not started |
| AC3 | 正常化時に一度だけ復旧通知され、過去の要対応通知とfingerprintで追跡できる。 | transition tests | Not started |
| AC4 | 監視が2回連続失敗、または60分以上欠落した場合、収集APIと別経路で通知される。 | missed-run/failure-sequence tests | Not started |
| AC5 | 通知には停止開始、継続時間、原因task/error、finding/change record URL、人の判断事項が含まれ、秘密情報を含まない。 | output contract and secret scan | Not started |
| AC6 | 通知処理は収集状態、task、データ、コード、PRを変更しない。 | before/after state assertions | Not started |

## Task plan

| ID | Task | Owner | Depends on | Write scope | Verification | State |
| --- | --- | --- | --- | --- | --- | --- |
| T1 | 監視結果4状態と通知遷移を設計・実装する。 | Main | Approval | monitoring tooling/tests | transition tests | Proposed |
| T2 | 独立通知sink、dedupe、ack、復旧通知を接続する。 | Main | T1 | automation configuration/tooling | delivery tests | Dependent |
| T3 | 監視欠落を外部schedulerから検知するhybrid経路を接続する。 | Main + operator | T1 | deployment/automation | missed-run drill | Dependent |
| T4 | pause fixtureで初回、継続、ack、復旧、監視不能を通し、14日安定化指標へ反映する。 | Main | T1-T3 | tests/docs | drill evidence | Dependent |

## Review gates

- **Design and task-split review — 2026-09-19, reviewer: Main.** 実行履歴とfinding recordを照合し、検知失敗ではなく通知状態モデルと配送経路の欠落と判断した。通知経路は収集状態を変更しないため、復旧操作から分離する。
- **Pre-implementation review:** 利用者が本recordを明示承認した後に実施する。
- **Final review:** AC1-AC6、通知配送証拠、秘密情報非混入、収集状態非変更、監視欠落drillを照合する。

## Human decision required

- 本recordの恒久修正案を承認するか。
- 現在停止中の `race-discovery` taskについて、原因調査後に限定再実行とpipeline再開を行うか。これは通知修正とは別の復旧操作として明示承認を必要とする。

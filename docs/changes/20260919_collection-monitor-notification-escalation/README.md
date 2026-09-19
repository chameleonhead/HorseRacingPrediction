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
- 安全性を機械判定できる一過性の全体停止は、利用者への都度確認なしにpipelineを再開して収集を継続する。

## Non-goals

- 本recordの承認前に収集taskを再実行またはpipelineを再開すること。
- `TargetClosedException` の原因を未確認のまま恒久修正と断定すること。
- High/Critical findingを自動承認し、本番コードまたはデータを変更すること。

## Proposed design

監視結果を `Healthy`、`FindingRecorded`、`ActionRequired`、`MonitorFailed` の4状態で明示する。`UnexpectedPipelinePause`、収集停止、High/Criticalはprobeが成功していても `ActionRequired` とする。API/認証/実行基盤の単発失敗は記録のみ、2回連続または予定時刻から60分超の欠落を `MonitorFailed` とする。

通知状態はfingerprintごとに `FirstObservedAt`、`LastObservedAt`、`LastNotifiedAt`、`AcknowledgedAt`、`ResolvedAt` を保持する。`ActionRequired` は初回即時通知し、未解決かつ未acknowledgedなら30分ごとに再通知する。正常化時は一度だけ復旧通知する。PR/change recordは詳細証拠へのリンクとし、通知の配送成否とは分離する。

恒久運用は、always-onのサービスまたはクラウドschedulerを一次検知・通知にし、Codexを診断、証拠整理、修正案change record作成に使うhybridを推奨する。ローカルCodex taskだけではPCまたはデスクトップアプリ停止中の保証がないため、唯一の死活監視にはしない。

### Automatic continuation policy

次の条件をすべて満たす停止を `AutomaticSafeResume` とし、停止証拠を保存した後にpipelineだけを再開する。失敗taskの履歴削除、failure解決、データ補正、同一taskの強制再実行は行わない。

- pipelineの停止理由とfailure notificationが同一taskを指す。
- error分類が事前登録済みの一過性インフラ障害であり、初期登録は `race-discovery` の `TargetClosedException` に限定する。
- validation、parse、identity、ページ構造変化、データ不整合、権限、maintenance、手動停止ではない。
- 同一fingerprintの連続自動再開が設定上限内である。初期値は6時間に1回、同一原因で再停止した場合は自動再開せず人へ通知する。
- 再開後にpipelineがunpausedであり、実行中または完了件数の進行を観測できる。

条件外は `HumanDecisionRequired` とし、change recordと通知を作るが再開しない。自動再開後も原因分類のコード修正案は別途起票し、再発防止を追跡する。

## Acceptance criteria

| ID | Observable criterion | Verification | State |
| --- | --- | --- | --- |
| AC1 | `UnexpectedPipelinePause`またはHigh/Critical findingで、probe成功時にも要対応通知が発生する。 | simulated monitoring result | Not started |
| AC2 | 未解決・未acknowledgedの停止は30分ごとに再通知され、同一周期内の重複通知はない。 | clock-controlled notification tests | Not started |
| AC3 | 正常化時に一度だけ復旧通知され、過去の要対応通知とfingerprintで追跡できる。 | transition tests | Not started |
| AC4 | 監視が2回連続失敗、または60分以上欠落した場合、収集APIと別経路で通知される。 | missed-run/failure-sequence tests | Not started |
| AC5 | 通知には停止開始、継続時間、原因task/error、finding/change record URL、人の判断事項が含まれ、秘密情報を含まない。 | output contract and secret scan | Not started |
| AC6 | 通知処理は収集状態、task、データ、コード、PRを変更せず、自動継続処理は許可されたpipeline resumeだけに分離される。 | before/after state assertions | Not started |
| AC7 | 登録済みの一過性停止は証拠保存後にpipelineだけが自動再開され、収集の進行が観測される。 | production-shaped safe-resume test | Not started |
| AC8 | 同一原因の再停止、未登録error、整合性関連error、maintenance/手動停止では自動再開されず要対応通知になる。 | negative-policy and rate-limit tests | Not started |

## Task plan

| ID | Task | Owner | Depends on | Write scope | Verification | State |
| --- | --- | --- | --- | --- | --- | --- |
| T1 | 監視結果4状態と通知遷移を設計・実装する。 | Main | Approval | monitoring tooling/tests | transition tests | Proposed |
| T2 | 独立通知sink、dedupe、ack、復旧通知を接続する。 | Main | T1 | automation configuration/tooling | delivery tests | Dependent |
| T3 | 監視欠落を外部schedulerから検知するhybrid経路を接続する。 | Main + operator | T1 | deployment/automation | missed-run drill | Dependent |
| T4 | pause fixtureで初回、継続、ack、復旧、監視不能を通し、14日安定化指標へ反映する。 | Main | T1-T3 | tests/docs | drill evidence | Dependent |
| T5 | allowlist、再開回数上限、再開後progress検証を持つ安全な自動継続を接続する。 | Main | T1-T3 | monitoring/recovery automation | positive/negative production-shaped tests | Dependent |

## Review gates

- **Design and task-split review — 2026-09-19, reviewer: Main.** 実行履歴とfinding recordを照合し、検知失敗ではなく通知状態モデルと配送経路の欠落と判断した。通知経路は収集状態を変更しないため、復旧操作から分離する。
- **Pre-implementation review:** 利用者が本recordを明示承認した後に実施する。
- **Final review:** AC1-AC6、通知配送証拠、秘密情報非混入、収集状態非変更、監視欠落drillを照合する。

## Human decision required

- 本recordの恒久修正案を承認するか。
- 本recordの承認後は、`AutomaticSafeResume` 条件に一致する停止について都度承認を求めない。条件外の停止、失敗taskの再実行、データ補正、コード修正は引き続き個別の承認境界とする。

## Incident recovery ledger

- Incident: 2026-09-19 16:44 JST、`race-discovery` の `TargetClosedException` によりpipelineが全体停止。
- Temporary recovery: 20:50 JST、停止理由とfailure groupを読み取り確認し、pipelineだけを再開。20:51および20:52 JSTに `isPaused=false`、実行中task 1件を確認。
- Root cause: 閉じたbrowser sessionはhandler内で一度再試行されるが、二度目の例外は上位classifierで `PermanentFailure` となり、既定の `StopPipeline` により全体停止する。監視側は検知を成功runとして扱い通知しなかった。
- Corrective proposal: typed notification state、独立通知、限定allowlistによる自動継続、再開回数上限、進行検証を実装する。
- Permanent fix: Not started; approval pending.
- Remaining risk: 同じ例外が再発した場合は恒久修正前のため自動再開せず通知する。

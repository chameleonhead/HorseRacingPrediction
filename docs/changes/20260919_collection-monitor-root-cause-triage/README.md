# 収集監視を観測記録から原因分析と実行タスクへ変更する

- Status: Proposed
- Change record schema: 2
- Owner: Main
- Created: 2026-09-19
- Updated: 2026-09-19

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Not started | 原因別タスクを提案済み。承認後に実装する。 |
| Verification | Not started | 静的な原因確認を実施済み。回帰・本番検証は各タスクに定義した。 |
| Deployment/operation | Not started | 現行監視はGitHub Actions経由。ローカルAPI runnerへの切替が必要。 |

## Context

2026-09-19 22:13 JSTの監視では49件のfindingが返り、出力上限にも達していた。既存の自動化は同じ症状をfingerprint別のchange recordへ追記することを主処理とし、35件の`collection-attention-*` recordを作成したが、原因の集約、優先順位付け、実装可能なタスク化を十分に行わなかった。

直近の主な観測値は、Realtime laneの停滞がtrainer 98件、owner 69件、race-discovery 2件、horse 800件、jockey 69件、ownerの`SubjectNotIdentified`が156件、`TargetClosedException`が1件、Normal laneの`DispatchOrderViolation`が継続、である。pipelineは停止していない。これらを同じ重さの個別findingとして扱わず、以下の原因候補へ集約する。

## Root-cause analysis

### R1 Owner ID契約の不一致（コード上確認済み）

- race entryからowner収集taskを作る側は`DeterministicIdGenerator.BuildEntityId("owner", NormalizeKey(name))`を使い、`owner-<UUID v5>`形式を生成する。
- owner参照APIは`NormalizeOwnerName`の後、別の`CreateOwnerId`で`SHA-256`の先頭20文字をIDにする。正規化規則も会社表記除去・NFKC・大文字化であり、task生成側と異なる。
- collectorはtaskのresource IDをそのまま`GET api/owners/{ownerId}`へ渡し、404を`OwnerNotRegistered`、最終的に`SubjectNotIdentified`として扱う。
- したがってalias mappingで偶然同じIDへ解決されない限り、同じ所有者名でもtask IDとAPI IDは一致しない。156件のowner failureは外部サイト障害ではなく内部ID契約不整合の影響を受ける。

### R2 DispatchOrderViolationの互換性判定不足（コード上確認済み）

監視実装は同じlaneの高優先度停滞taskと配送済みtaskを比較するが、実workerが両definitionを処理可能かというcapability/compatibilityを照合していない。設計文書はcompatible workに限定するとしているため、現行findingをdispatcherの不具合と断定できない。まずmonitorの偽陽性を除去し、その後に残る実違反だけをdispatcher修正へ送る。

### R3 停滞taskの症状分割と処理能力不足の未分析

`StalledActiveTask`はdefinition、status、lane、priority別に分割され、同じworker不足または到着率超過でも多数のrecordになる。件数と最古時刻だけでは、dispatcher starvation、worker capability不足、処理能力不足、意図された待機のどれかを判定できない。definition別の到着・配送・完了率、worker capability、lease、最古ageを同じcutoffで測る必要がある。

### R4 既修正と本番配備状態の混同

`TargetClosedException`、artifact freshness、actionable outcomeはorigin/mainに修正が入っている。一方、監視値には旧挙動由来のfindingが残る。新規修正taskを重複作成せず、配備revision確認と配備後の再観測を独立した運用taskにする。

### R5 自動化の成功条件が弱い

現行promptはworkflow成功、finding追記、PR作成を完了として扱えるため、actionable findingに原因・所有task・受け入れ基準がなくてもrunが成功する。またGitHub workflowとPRを実行経路に含み、利用者が求めるGitHub非依存と一致しない。

## Goals

- 反復findingを件数追記ではなく、原因、影響、証拠、次の安全な操作、所有taskへ対応付ける。
- 同一原因から派生したfindingを1つの原因タスクへ集約し、PRを自動生成しない。
- 監視runは、各actionable findingが既存task、新規task、または証拠付きの除外へ対応しない限り成功扱いにしない。
- owner ID契約、dispatch互換性、収集能力を回帰可能な形で修正または判定する。
- Codex heartbeatから本番監視APIを直接読み、同じ監視taskを継続利用する。

## Non-goals

- 承認前の本番コード修正、データ補正、失敗履歴削除、強制再実行。
- findingごとのbranch、PR、change recordの自動作成。
- 収集能力の測定前にpriorityやworker数を推測で変更すること。
- API keyをリポジトリ、automation prompt、ログ、memoryへ平文保存すること。

## Decisions

- 原因台帳は本recordのtask planを正本とし、fingerprint別recordは証拠の履歴として参照するだけにする。
- 新規または重大に変化したfindingだけを通知し、変化なしの観測はautomation memoryに時刻と集計値だけを保存する。
- owner IDはAPIとtask producerで1つの共有関数・正規化規則へ統一し、既存IDへの移行・互換参照を設計してから切り替える。
- dispatch違反はlane一致だけで判定せず、実worker capabilityまたはdispatcherが用いるcompatibility keyを証拠に含める。
- GitHub Actionsはローカルrunner完成まで手動診断手段として残すが、定期heartbeatの通常経路とPR作成には使わない。
- API keyは`%LOCALAPPDATA%\HorseRacingPrediction\CollectionMonitor\production-api-key.credential.xml`へWindows DPAPIで暗号化して保存し、base URLだけを同ディレクトリの`settings.json`へ保存する。どちらもリポジトリ外とする。

## Documentation updates

本提案では本change recordのみを追加する。承認後、`docs/11-automation-design.md`と`docs/26-collection-platform-design.md`を、原因集約、直接API監視、秘密情報境界、成功条件の正本に合わせて更新する。

## Concern and agreement ledger

| ID | Concern and evidence | Impact | Proposed disposition | AC/task/test | Agent position | User disposition | State |
| --- | --- | --- | --- | --- | --- | --- | --- |
| C1 | owner IDの変更は保存済みtask、alias、URLとの互換性を壊し得る。 | 既存参照切れ、重複owner | 既存両IDのinventory、互換lookup、移行preview、rollbackをT1に必須化する。 | AC1/T1 | 統一は必要だが一括置換は不可 | Pending | Resolved in design |
| C2 | capability情報が履歴にない場合、過去のdispatch違反を確定できない。 | 偽陽性または見逃し | 判定不能はProgramBugにせず`InsufficientEvidence`とし、将来のenvelopeへ判定根拠を保存する。 | AC2/T2 | 証拠不足を不具合断定しない | Pending | Resolved in design |
| C3 | ローカルheartbeatはPC/Codex停止中に実行できない。 | 死活監視の空白 | 本変更は利用者指定どおりローカル主経路とし、60分欠落を次回起動時に通知する。always-on外部監視は除外follow-upとする。 | AC5/T5 | 制約を明示して採用 | Pending | Accepted risk |
| C4 | 既存35 recordを削除すると監査証跡を失う。 | 過去経緯の消失 | 削除せず、本recordから原因別に参照し、以後の反復追記だけを停止する。 | AC4/T4 | 保存して正本を一本化 | Pending | Resolved in design |
| C5 | API keyの誤出力は認証情報漏洩になる。 | 本番アクセス侵害 | DPAPI、リポジトリ外、標準出力禁止、redaction test、秘密情報scanを必須化する。 | AC5/T5 | 平文設定は不可 | Pending | Resolved in design |

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | race entryから作られたowner task IDをowner参照APIが同一ownerとして解決し、既存IDも移行中に参照できる。 | T1 | producer→API integration test、既存ID inventory、migration preview | Not started |
| AC2 | `DispatchOrderViolation`は同一laneかつ同一worker capabilityで処理可能なtask間だけに発生し、判定不能は不具合扱いしない。 | T2 | compatible/incompatible worker counterexample tests | Not started |
| AC3 | definition別に到着率、配送率、完了率、最古age、worker capabilityが同一cutoffで確認でき、800件のhorse停滞をstarvation、capacity、intentional waitのいずれかへ根拠付き分類できる。 | T3 | snapshot testと本番read-only report | Not started |
| AC4 | 49 findingが原因別taskまたは証拠付き除外へ全件対応し、反復観測だけでは新規change recordやPRを作らない。 | T4 | fixture replay、task mapping audit、GitHub writeなしの確認 | Not started |
| AC5 | heartbeatがGitHub Actionsを起動せず本番APIを直接読み、秘密値を出力せず、同じtask内で状態を継続する。 | T5 | local dry-run、secret scan、2回連続heartbeat、欠落検知 | Not started |
| AC6 | 配備済みrevisionを確認し、既修正の`TargetClosedException`、freshness、actionable outcomeは重複修正せず、配備後観測で解消または継続を判定する。 | T6 | deployed revision照合とproduction read-only verification | Not started |
| AC7 | actionable findingに原因仮説、影響、証拠、所有task、次の操作がないrunは成功扱いにならず、利用者へ具体的な要対応を返す。 | T4,T5 | monitor outcome contract tests | Not started |

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | owner ID生成・正規化を共有契約へ統一し、既存ID互換と移行previewを実装する。AC1 | Main | High capability | Approval | Api、ApiClient、Collector、migration/tests | end-to-end owner identity tests | ID inventory、preview、tests | Proposed |
| T2 | dispatch findingへworker capability/compatibility判定を接続し、偽陽性を除去する。AC2 | Main | High capability | Approval | CollectionOperations、dispatcher contracts/tests | counterexample tests | compatible caseのみfinding | Proposed |
| T3 | definition別flow rate、age、capabilityの診断snapshotを追加し、本番read-only分析を行う。AC3 | Main | High capability | T2 | CollectionOperations、API、tests/docs | load/snapshot/production report | backlog原因分類 | Proposed |
| T4 | 既存49 findingをT1-T3/T6へmappingし、原因台帳とmonitor成功条件を実装する。AC4,AC7 | Main | High capability | T1-T3 | monitoring writer、docs/tests | fixture replay、no-PR assertion | 全finding mapping | Proposed |
| T5 | DPAPI資格情報を使うローカルrunnerへheartbeatを切り替え、GitHub定期実行と自動PR作成を外す。AC5,AC7 | Main + operator | High capability | T4、API key provisioning | tooling、automation、docs/tests | local consecutive runs、secret scan | 同一taskのdirect API監視 | Proposed |
| T6 | origin/mainと本番revisionを照合し、既修正項目を配備・再観測する。AC6 | Main + operator | High capability | Approval | deployment evidence、recordのみ | revision and post-deploy snapshot | 解消/継続判定 | Proposed |

## Review gates

- **Design and task-split review — 2026-09-19, reviewer: Main.** 49 findingをID契約、dispatch判定、capacity/backlog、既修正の配備、本体自動化の5系統へ集約した。AC1-AC7はT1-T6と検証に双方向で対応する。ID移行、dispatcher契約、本番認証を含むため主担当が直列に統合する。
- **Concern and agreement review — 2026-09-19, reviewer: Main.** データ互換性、証拠不足、ローカル死活、監査履歴、秘密情報を確認した。C1-C5の処置を設計に組み込み、未解決の技術的反対はない。C3は利用者指定のローカル運用と可用性制約を明示した受容riskであり、60分欠落通知を再検討条件とする。
- **Pre-implementation review:** 承認後に各taskのrevision、write scope、移行preview、API key provision状態を確認する。
- **Checkpoint review:** T1/T2、T3/T4、T5/T6の各検証済みcheckpointで実施する。
- **Final review:** AC1-AC7、全task、production revision、秘密情報非混入、GitHub write不在を照合する。

## Verification record

- 2026-09-19: CodeGraphでowner task producerのUUID v5 IDとowner APIのSHA-256 ID、異なる正規化規則、collectorの直接GET経路を確認した。
- 2026-09-19: CodeGraphでdispatch findingがlaneだけを比較しworker capabilityを照合しないことを確認した。
- 2026-09-19: origin/mainの35件の`collection-attention-*` recordと、直近監視の49 findingを棚卸しした。
- 2026-09-19: 本番監視の直近値をautomation memoryと実行証拠から照合した。秘密値と未編集ログは記録していない。

## Deviations and follow-up

- always-onの外部死活監視は本変更のACを妨げない除外follow-upとし、ownerはoperator、再検討条件はローカル監視の60分超欠落または14日予定実行率99%未満とする。

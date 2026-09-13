# 4主体の要対応データ補正ジョブを管理画面から実行する

- Status: Proposed
- Owner: HorseRacingPrediction maintainers
- Created: 2026-09-14
- Updated: 2026-09-14

## Context

`/settings` では、今回のJRA競走馬識別子不具合で記録されたHorse専用repairだけを実行できる。運用者は、競走馬・騎手・調教師・馬主について同一RaceEntryの再取得等から名寄せ対応が必要と判明した場合にも、候補と根拠を確認して補正を実行したい。

本変更では、この作業単位を「データ補正ジョブ」と呼ぶ。Collection Platformの収集失敗・再取得ジョブとは別の永続キューであり、データを自動変更せず、運用者が確認して実行するまで `要対応` として残す。

## Goals

- 競走馬・騎手・調教師・馬主の補正候補を、主体種別、統合元、統合先、検出根拠、判定理由付きの永続ジョブとして記録する。
- `/settings` で要対応ジョブを主体別に絞り込み、安全な候補だけを選択して実行できる。
- 同一JRA識別子、または同一RaceEntryの再取得による置換を確認できる候補だけを作り、名前一致だけでは候補化しない。
- apply直前に根拠・参照・競合を再検証し、冪等なledgerと監査を残す。
- 競走馬・騎手・調教師の統合元について既存・将来のプロフィール収集を停止し、馬主は既存alias mapping／merge auditを使用する。

## Non-goals

- 収集失敗通知や再取得操作をデータ補正ジョブへ移すこと。
- 名前だけを根拠に候補を自動作成・自動適用すること。
- 任意のsource/target IDを画面から入力する汎用merge。
- Horse/Jockey/Trainer/Owner以外の主体、JRA以外のProvider。
- 馬主プロフィール収集や `ResourceType.Owner` の新設。
- 適用済み補正の取消。

## Experience and interaction design

### 候補の発生

RaceCard等から同じ `RaceId + EntryId` を再登録した際に、HorseId、JockeyId、TrainerId、または正規化後のOwner対応先が以前と変わり、かつ新しい値がJRAページの識別情報または同一RaceEntryの置換として確認できた場合、主体ごとにデータ補正ジョブをupsertする。候補作成はデータを統合せず、重複検出だけを行う。

- 競走馬: 現行のJRA `accessU` identity由来IDを優先し、既存Horse repair候補を新基盤へ引き継ぐ。
- 騎手・調教師: RaceCard上のプロフィールリンクから正規化したJRA source identityを保存する。リンクが得られない場合でも同一RaceEntryの旧ID→新ID置換は根拠として記録するが、表示名だけの横断検索では候補を作らない。
- 馬主: 安定したJRAプロフィールIDがないため、同一RaceEntryの再取得で旧Owner対応先→新Owner対応先が置換された場合だけ候補を作る。単なる同名・類似名検索では作らない。

同一sourceに複数targetがある場合は全候補を `要確認` とし、一括実行できない。

### `/settings` の表示と実行

既存の「データ補正」を「要対応データ補正」へ拡張する。上部に `すべて / 競走馬 / 騎手 / 調教師 / 馬主` の絞り込みと、要対応・要確認件数を表示する。各行・狭幅カードには次を表示する。

- 主体種別と状態（`実行可能` / `要確認`）
- 統合元・統合先の名称とID
- 根拠種別（JRA識別子 / 同一RaceEntry）と根拠レースへのリンク
- 影響する参照件数、停止する収集タスク件数
- 実行可能またはblockedとなる理由

実行可能候補だけを選択できる。確認Dialogは主体別の処理内容、redirectまたはalias mapping、収集停止、履歴保持、取消不可を表示する。成功後は適用・skip・収集停止件数を示して一覧を再読込する。部分失敗または応答不明時はジョブを消さず、再読込と冪等再実行を案内する。

### 状態と監査

永続状態は `Pending`（要対応）、`Blocked`（要確認）、`Applied`（完了）を正本とする。安全性はpreview/apply時に再評価し、根拠が変わればPendingからBlockedへ移る。実行中専用状態は設けず、transactionと冪等keyで二重適用を防ぐ。通信失敗後は再読込でledgerを確認する。

## Navigation and relationships

- 導線は引き続き `運用 > その他設定 > 要対応データ補正` (`/settings`) とする。
- 収集の要対応は `/jobs`、名寄せ・参照補正の要対応は `/settings` とする。
- Horse/Jockey/Trainerのsource/targetは各詳細、OwnerはOwner詳細、RaceEntry根拠はRace詳細へリンクする。

## Mocks

- [デスクトップ／狭幅ワイヤーフレーム](mocks/actionable-repair-jobs-wireframe.md)

## Documentation updates

- `docs/20-admin-ui-design.md`: `/settings` のデータ補正を4主体の要対応ジョブへ拡張し、`/jobs`との責務分離を管理UIの正本へ反映する。
- 既存 [Horse専用repair change record](../20260914_horse-identity-repair-admin-ui/README.md) は実装履歴として保持し、本変更が後継の共通運用基盤であることを追記する。

## Technical impact

- Event Storeに主体種別付き `IdentityRepairJob` と `IdentityRedirect` を追加する。Job IDは主体種別・source・target・evidenceを含む決定論的値とし、同じ検出を重複登録しない。
- RaceEntry収集契約へJockey/TrainerのJRA source identityと、Owner対応先を比較できる情報を追加する。既存データの一括候補生成は行わず、再取得時に検出する。
- preview/apply APIを主体共通化し、現行Horse candidate/redirectは互換読取またはmigrationにより新APIへ表示する。
- applyは主体別strategyで処理する。Horse/Jockey/Trainerはredirect ledger、OwnerはOwnerAliasMappingとOwnerMergeAuditを更新する。すべてapply直前に同一性根拠とsource→target一意性を再検証する。
- Horse/Jockey/Trainerは、統合元IDを参照するRaceEntryが残っている間は `Blocked` とする。根拠となったRaceEntryを含め、再取得によって参照が統合先へ置換済みであることをapply直前に確認する。Ownerはsnapshot上の名称を履歴として保持し、alias mappingでcanonical ownerへ解決する。
- Horse/Jockey/TrainerはCollection Platformの既存resource suppressionを利用する。Ownerには収集resourceがないためsuppressionしない。
- redirect解決をHorseだけでなくJockey/Trainerの詳細取得と参照表示へ接続する。過去イベント・request/task/attempt履歴は書き換えない。
- 複数DBのpartial failureは、適用済みledgerを確認した再実行で不足したsuppressionを完了する。

## Decisions

1. 「要対応ジョブ」は名寄せ・参照補正の永続作業単位であり、CollectionTaskではない。
2. 候補作成は同一JRA識別子または同一RaceEntry置換に限定し、名前一致だけを根拠にしない。
3. 運用者の明示実行を必須とし、自動applyしない。
4. 同一sourceのtargetが複数ならblockedとし、UIから安全判定を上書きできない。
5. Ownerは既存alias mappingとmerge auditを正本とし、外部IDや収集resourceを新設しない。
6. 既存Horse repairの監査履歴とredirectを保持し、新一覧から同じ安全境界で操作できるようにする。

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
|---|---|---|---|---|
| AC1 | 同じRaceEntryの再登録で4主体の旧→新対応が変わったとき、主体別の補正ジョブが重複なく記録され、名前一致だけでは記録されない。 | T2,T3 | candidate integration tests | Proposed |
| AC2 | `/settings`で4主体を絞り込み、名称・ID・根拠・影響・判定理由を確認できる。 | T4 | bUnit/browser | Proposed |
| AC3 | 実行可能候補だけを選択でき、blocked、同一source複数target、または統合元RaceEntry参照が残るHorse/Jockey/Trainerを実行できない。 | T2,T4 | API/bUnit conflict tests | Proposed |
| AC4 | apply直前にmanifest、根拠、参照、redirect/alias競合を再検証し、選択対象だけを原子的かつ冪等に適用する。 | T2,T3 | transport+persistence integration tests | Proposed |
| AC5 | Horse/Jockey/Trainerの統合元収集を取消・抑止し、Ownerでは収集taskを作成せず、履歴を保持する。 | T3 | Collection Platform integration tests | Proposed |
| AC6 | Horse/Jockey/Trainerの旧ID詳細参照はcanonicalへ解決され、Ownerはalias mappingとmerge auditで統合を確認できる。 | T3 | subject endpoint tests | Proposed |
| AC7 | Event Store適用後・suppression前の失敗から再実行し、不足分だけを完了できる。 | T3 | partial-failure/retry test | Proposed |
| AC8 | loading/empty/error/successを区別し、狭幅・keyboard操作でも情報と主操作を失わない。 | T4 | bUnit + real browser | Proposed |
| AC9 | migration後も既存Horse repair候補・redirect・監査履歴を参照できる。 | T1,T3 | migration compatibility tests | Proposed |
| AC10 | 収集の要対応は`/jobs`、データ補正の要対応は`/settings`に分離される。 | T4 | navigation/browser test | Proposed |

## Delivery plan

1. 共通job/redirect schemaと互換migrationを確定する。
2. 4主体の検出情報をRaceEntry書込経路へ接続する。
3. 主体別preview/apply、redirect/alias、suppression、retryを実装する。
4. `/settings`を主体横断一覧へ変更する。
5. transport+persistence、component、browser、全回帰テストを実行する。

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
|---|---|---|---|---|---|---|---|---|
| T1 | 共通job/redirect schema・migration・互換読取 | Main | Lead tier | - | Application read models, Infrastructure persistence/migrations | migration tests | old/new DB compatibility | Proposed |
| T2 | 4主体候補検出と安全性判定 | Main | Lead tier | T1 | collection write/API contracts/detection tests | candidate integration tests | AC1,AC3 evidence | Proposed |
| T3 | 主体別apply、redirect/alias、suppression、retry | Main | Lead tier | T1,T2 | repair API, Collection Platform, endpoint tests | transport+persistence tests | AC4-AC7 evidence | Proposed |
| T4 | `/settings`主体横断UIと操作 | Worker candidate; Main review | Worker tier | T2,T3 contract freeze | Settings/AdminApiClient/component tests | bUnit/browser | AC2,AC3,AC8,AC10 evidence | Proposed |
| T5 | 文書同期、全回帰、最終監査 | Main | Lead tier | T1-T4 | docs/change record only | format/build/test/diff/status/CodeGraph | all AC/task reconciliation | Proposed |

## Review gates

- **Design and task-split review** — Reviewer: Main。Inputs: CodeGraph、現行Horse repair、4主体識別・merge棚卸し、2件のread-only worker調査。Decision: schema/API/データ整合性はMainが直列実装し、契約freeze後のUIだけを独立委譲候補とする。T1→T2→T3→T4→T5の依存とAC coverageを確認。Open decisionは「今回のジョブをデータ補正ジョブとして扱う」解釈のユーザー承認。Follow-up: 承認後にPre-implementation reviewを記録する。
- **Pre-implementation review** — 承認後に記録する。
- **Checkpoint review** — 各checkpointで記録する。
- **Final review** — 全task/ACをVerifiedへ照合後に記録する。

## Verification record

- 2026-09-14: `codegraph explore`で現行Horse候補のトリガー→candidate→preview/apply→suppression→UIを確認した。
- 2026-09-14: read-only worker 2件で、4主体の識別子・merge・参照関係と、repair UI／収集failureとの責務差を並行棚卸しした。両成果をMainが実コードとCodeGraphで照合し、Ownerに安定外部IDとCollection resourceがないこと、Jockey/Trainerにsource identity伝播がないことを確認した。
- Delegation usage/cost: measured token/cost unavailable。read-only調査2件、再試行0、escalation0。書込なし。Lead decision: 設計入力として採用。

## Deviations and follow-up

- 本番データへの補正実行は実装・ローカル検証に含めない。
- 本changeはProposedであり、承認前はproduction codeを変更しない。

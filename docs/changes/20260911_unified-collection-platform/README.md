# 競馬情報収集状態管理基盤

- Status: Approved
- Owner: HorseRacingPrediction maintainers
- Created: 2026-09-11
- Updated: 2026-09-11

## Context

現行基盤は Api が SQLite 上の永続ジョブを正本として保持し、outbox と SQS を配送通知に限定し、Collector が lease token を取得して単一タスクを実行する。レース、開催日、馬プロフィール、馬履歴等の個別状態・再取得・優先度制御は実装済みだが、収集対象の識別、収集定義、抽出仕様 revision、現在状態、取得理由、実行、試行、URL 候補が一つの共通モデルになっていない。

本変更は既存 Parser / Page / Workflow と Api の domain write を維持しながら、既存収集ジョブ実装を廃止し、収集制御を Resource 中心の新基盤へ置換する。旧 job store/runner/scheduler/API/UI を新基盤の互換層として残さない。調査結果は [current-state.md](current-state.md)、要求との差分は [gap-analysis.md](gap-analysis.md)、完全置換で解決すべき事項は [decisions/full-job-replacement.md](decisions/full-job-replacement.md) を参照する。

## Goals

- `ResourceKey` を論理 Identity とし URL と分離する。
- `CollectionDefinition` と `CollectionRevision` で観測内容と抽出仕様を管理し、Resource ごとの `RequiredRevision` を導出する。
- 初回、Backfill、Discovery、定期更新、定義変更、手動更新、Recovery を同じ `CollectionRequest -> CollectionTask -> CollectionAttempt` 経路へ流す。
- 同一 Resource + Definition の active task を一件に制限しつつ、同一 revision の再取得を何度でも履歴として残す。
- Direct Collection と Discovery を分離し、URL 候補を検証しながら別候補・URL 生成・Discovery へフォールバックする。
- Realtime / Normal / Background lane と動的 priority を同じ基盤で処理し、双方の starvation を防ぐ。
- 過去全件、開催週・当日、主体情報の定期更新、RaceOdds の反復観測を同じ scheduling 規則で扱う。
- Resource、Definition/Revision、lane、priority、batch、retry、revision impact ごとの進捗 Projection を提供する。

## Non-goals

- 既存 JRA Parser / Page / Workflow の全面書き換え。
- URL を新しい永続 Resource ID に採用すること。
- revision impact の任意 SQL または任意コード文字列を DB に保存・実行すること。
- Backfill と Realtime を別の状態正本へ分割すること。
- Phase 1 ですべての ResourceType、管理 UI、Odds 券種を同時に完成させること。
- JRA アクセス頻度、安全停止、既存認証・lease mutation guard を弱めること。
- 競馬の Domain Data、source citation、認証情報を旧収集ジョブデータと一緒に削除すること。

## Experience and interaction design

管理 API/UI は Resource と CollectionDefinition を起点に、最新状態、次回予定、適用/要求 revision、直近 request/task/attempt、利用 location を表示する。手動再取得は `ManualRefresh` request を通常経路へ追加する。一括再取得はプレビュー後に複数 request へ展開する。

新管理画面を実装してから cutover し、切替後は既存ジョブ画面を残さない。旧 job ID、deduplication key、履歴は新 task に移行せず、旧 job data とともに切替作業内で削除する。UI モックは管理 UI 実装 Phase の前に追加する。

## Navigation and relationships

```text
CollectionRevisionImpact ──▶ ResourceRequiredRevision
                                  │
Resource ──▶ CollectionState ◀────┘
   │              │
   │              ▼
   │       CollectionRequest ──▶ CollectionTask ──▶ CollectionAttempt
   │                                      │
   │                                      ▼
   │                              ResourceLocator
   │                               │          │
   │                         Direct fetch  Discovery
   │                               └────┬─────┘
   │                                    ▼
   └──── ResourceReference ◀── Parse / validate / domain write
```

`CollectionState` は高速参照 Projection とし、履歴の正本は request/task/attempt と domain write outcome とする。Batch は request を束ねる表示単位であり、完了判定の正は Resource + Definition の状態とする。

## Documentation updates

- `docs/26-collection-platform-design.md`: Resource 中心収集基盤の正本設計を新設する。
- `docs/00-system-architecture.md`: Collector 制御の正本リンクと完全置換・controlled cutover 方針を追加する。
- `docs/01-lambda-collector-architecture.md`: SQS は通知のみ、Api が新モデルの状態正本という境界を追記する。
- `docs/11-automation-design.md`: policy/lane/priority scheduling の正本リンクを追加する。
- `docs/22-collector-design.md`: 既存ジョブを恒久互換層にせず完全置換する方針と未実装状態を追加する。
- `docs/23-jra-scraping-redesign.md`: Navigator/Parser/Page の再利用境界を追加する。
- `docs/changes/20260911_unified-collection-platform/decisions/full-job-replacement.md`: 既存 job 実装を完全置換する際の依存分離、migration、cutover、rollback、削除条件を記録する。

## Technical impact

### Core model

- `ResourceType` は stable code を持つ registry とし、型固有処理は definition handler / policy / locator / impact selector の登録へ閉じ込める。
- `ResourceKey(Type, Provider, ExternalId)` を一意キーとする。Race は JRA `RaceId` を canonical external ID とし、Api domain race ID は mapping として保持する。Horse/Jockey/Trainer は現行 domain ID を初期 canonical key とし、JRA 公開 ID は alias/external identity mapping にする。URL は ID にしない。
- `CollectionDefinition` は `race-card`, `race-odds`, `race-result`, `horse-profile`, `jockey-profile`, `trainer-profile` 等を stable ID で登録する。handler 不整合は起動時に fail fast とする。
- `CollectionRevision` と impact は immutable に追加する。scope は `All`, `SpecificResources`, `DateRange`, `NamedCondition` とし、名前付き条件は versioned application registry で解決する。
- `RequiredRevision` は該当 impact の最大 revision と初回必須 revision から計算する。`AppliedRevision < CurrentRevision` だけでは stale にしない。

### Persistence

Api 所有 collection DB に resource、definition、revision、impact、state、request、task、attempt、location、reference、batch membership、odds observation を追加する。

- State unique: `(resource_type, provider, resource_id, definition_id)`。
- Active task uniqueness: terminal でない task のみ。SQLite では active lease/guard table または partial unique index と transaction で保証する。
- Task は request と一対多。`Resource + Definition + Revision` の履歴一意制約を設けない。
- Attempt unique: `(task_id, attempt_number)`。requested/final URL、HTTP status、redirect、page identification、error category を保持する。
- Location は Resource + Definition に複数許容し、Task は location ID を固定せず実行直前に解決する。

新基盤は旧 `jobs`, `job_attempts` と用途別 status table を参照しない独立 schema とする。新しい初期 Resource/State/Location は既存 Domain Data と source citation から構築し、旧 job/attempt/status/outbox/audit/marker と job ID/deduplication key は移行しない。新基盤の smoke test 成功後、同じ cutover 内で旧 job DB/table、旧 main SQS queue、旧 DLQ を削除する。旧 store/schema/API/UI/runner/scheduler の production code も同じ変更セット内で削除する。

### State transitions

`Unknown -> Pending -> Collecting -> Current` を基本とし、policy/revision により `RefreshDue` / `Stale`、結果により `Failed` / `Unavailable` へ遷移する。`ResourceNotYetAvailable` は次回時刻を持つ非失敗 outcome を許す。

Task は `Pending`, `Ready`, `Running`, `RetryWaiting`, `WaitingDiscovery`, `Succeeded`, `Failed`, `Cancelled`, `DeadLetter` を持つ。既存 hold、global pause、watchdog、DLQ circuit breaker、lease token、dispatch generation、mutation guard は維持する。

### Scheduling and fairness

`ICollectionSchedulePolicy` が state/context/time を受け、ShouldCollect、NextCollectionAt、priority、lane を返す。Priority は再評価可能な snapshot とし、理由と評価時刻を残す。

初期配分は Realtime 60%、Normal 25%、Background 15% の設定値とし、空 lane の枠は貸し出す。Background は aging と最低処理枠で starvation を防ぐ。単一 worker では連続 Realtime 上限後に due Background を一件処理する。

### Direct collection and discovery

locator は explicit、generated、verified stored、discovery の候補を返すが、すべて fetch 後に expected resource type/provider/id を検証する。

- timeout/429/5xx/access limit は transient とし location を invalid にしない。
- 404、unexpected page、resource ID mismatch は suspect とし別候補へ進む。反復または明確な証拠で invalid にする。
- redirect は新候補として記録し、identity 検証成功後だけ active/verified にする。
- 候補枯渇時は discovery task を作り、location/resource/reference 保存後に元 task を再開する。
- explicit URL を同定できなければ匿名 task にせず `UnidentifiedExplicitLocation` として失敗/要確認を返す。

### Domain writes and observations

既存 `Upsert*`, bulk declaration、refresh、source citation を definition handler から再利用する。domain write 成功後にだけ applied revision と collected timestamp を進める。Resource reference の展開は domain write 後の outbox で再試行可能にする。

RaceOdds は append-only `OddsSnapshot` とし、race、observed-at、provider、market、selection、value、source attempt を保存する。同じ attempt の重複だけを idempotency key で抑止し、別時刻の同値も観測として残す。

## Decisions

- 採用: Api が controller、Collector が worker、SQS が通知という配置は維持するが、既存 job controller 実装は拡張せず新モデルで置換する。
- 採用: 履歴と最新状態 Projection を分ける。
- 採用: revision impact は宣言的 scope + 名前付き selector とする。
- 採用: active task のみ一意にし、terminal task の履歴重複を許容する。
- 採用: lane quota + weighted fairness + aging。
- 採用: 検証環境で shadow migration/parity 確認後、本番は maintenance window で一回の controlled cutover を行う。新旧 scheduler/worker を本番で同時稼働させない。
- 不採用: `AgentJobType` を ResourceType とみなす。対象、理由、集約、処理が混在するため。
- 不採用: URL/`SourceIdentity` を canonical Resource ID にする。
- 不採用: current revision 未満を一律 stale にする。
- 不採用: 旧 job 実装を compatibility adapter で包んで恒久利用する。

## Acceptance criteria

### Model, task, revision

- Resource、definition、revision、impact、state、request、task、attempt、location が永続化され再起動後も復元できる。
- 同じ Horse + horse-profile + revision を Initial、ScheduledRefresh、ManualRefresh の別 task として実行できる。
- 同一 Resource + Definition の active task は transactionally 一件だけである。
- attempt に開始/終了、結果、error、requested/final URL、HTTP status、page identification が残る。
- 4種の impact が対象 Resource だけの RequiredRevision を更新し、非対象 Resource は Current のままである。
- selector 未登録または不正な revision は有効化されず、revision 再取得の affected/completed/pending/failed を照会できる。

### Location and collection

- 検証済み URL があれば一覧 Discovery を経由せず direct collection を試す。
- 保存 URL が無効なら別候補、候補枯渇時は Discovery へ進む。
- 503/timeout/429 で location を Invalid にせず、404/UnexpectedPage/ID mismatch は Suspect とする。
- HTTP 200 でも別 Resource なら domain data/state を成功更新しない。
- redirect は identity 検証後だけ active location になる。
- explicit URL は通常 request に変換され、同定不能は匿名 task にならない。

### Scheduling, restart, discovery, operations

- Realtime が Background より先に処理され、連続 Realtime 負荷下でも設定最大待ち時間内に Background が処理される。
- RaceCard/Odds/Result の priority/next time が開催日・発走時刻・確定状態で再評価される。
- RaceOdds を複数回収集でき、snapshot を上書きしない。
- process 停止、lease expiry、重複通知後も retryable task から再開する。
- Backfill の一部失敗後も後続 batch が進み、穴を Resource/Definition/batch から特定できる。
- RaceCard から Horse/Jockey/Trainer、Horse から Trainer/父/母 Horse を発見し、循環・重複・無制限な高優先度展開を防ぐ。
- Resource ID、期間、参照、最終取得時刻、revision impact、Failed/Stale の一括 preview が通常 request へ展開される。
- Resource/status、lane、priority、retry、definition/revision、batch、impact の進捗を照会できる。
- site rate limit、Retry-After、pause/hold、mutation lease guard に回帰がない。

### Replacement and cutover

- `PredictionExecution` とその enqueue/acquire/complete/requeue が収集状態ストアから分離され、Predictor に回帰がない。
- 新初期状態が Domain Data/source citation から再構築され、旧 Pending/Retryable work は新 Scheduler/Discovery により新 request として再生成される。旧 Running lease は削除前に drain される。
- 新旧 notification version の混在を拒否し、Api/Collector/Lambda/SQS/DLQ/Terraform/local runner の契約が同時に切り替わる。
- cutover 手順と削除前までの rollback rehearsal が隔離環境で成功し、旧 planning/worker と新 planning/worker が同一 Resource を同時実行しない。
- 新基盤で pause/hold/cancel、lease/heartbeat/expiry、watchdog、DLQ、failure notification、deadline、rate limit、mutation guard が検証される。
- 旧 admin/internal endpoints と UI の全 caller が新 API/UI へ移行し、旧 endpoint は削除または明示的 `410 Gone` になる。
- production code から旧 job classes/store/runner/scheduler/status tables の参照が消え、主要旧 symbol の CodeGraph caller がゼロになる。
- smoke test 後に旧 job DB/table、旧 main queue、旧 DLQ、旧 job ID/deduplication key が同じ cutover 内で削除され、新 queue と Domain Data が保持される。

## Delivery plan

各 Phase は model/schema、worker、tests、documentation を検証可能なコミットに分ける。完全置換の詳細ゲートは [full-job-replacement.md](decisions/full-job-replacement.md) に従う。

1. Boundary preparation: Predictor job を収集 store から分離し、新 CollectionOperations 契約/namespace と schema ownership を確定する。
2. New core: Resource/Definition/Revision/State/Request/Task/Attempt、active guard、outbox、lease、安全制御を旧 store 非依存で実装する。
3. Worker: handler registry と単一 RaceCard/Result handler を作り、既存 scraping workflow/domain write を新 task runner から呼ぶ。
4. Location: ResourceLocation、resolver、page identification、direct/fallback/discovery を Race に導入する。
5. Subjects: Horse/Jockey/Trainer と ResourceReference discovery を新 handler へ統合する。
6. Backfill: 新 Batch/Discovery task で期間から Race request を展開し、穴と再開を検証する。
7. Scheduling: due-state policy、lane、dynamic priority、fair allocation、aging を実装する。
8. Odds: RaceOdds parser/write model/snapshot と反復収集を実装する。
9. Revision/operations: impact、部分再取得、手動/一括 API/UI、監視 projection を実装する。
10. Initialization tooling: Domain Data/source citation から新 Resource/State/Location を構築し、未完了範囲を新 request として再生成する idempotent dry-run/execute を実装する。
11. Cutover rehearsal: 隔離環境で旧停止、drain、新初期化、新契約切替、smoke、旧データ/旧queue削除、削除前rollbackを反復し全 gate を満たす。
12. Production cutover: 承認済み maintenance window で一括切替し、smoke 成功後に旧 job DB/table と旧 main queue/DLQ を削除してから新 planning を有効化する。
13. Removal: 旧 store/entities/job types/runner/scheduler/endpoints/UI/config/tests を repository から削除し、CodeGraph、build/test、AWS/DB inventory で残存ゼロを確認する。

## Verification record

- The new outbox publishes the minimal `taskId` / `dispatchGeneration` notification to SQS.
- The Lambda `--once` entry accepts only that new notification, acquires the task from the API, invokes the registered definition handler, and reports the attempt result to the API.
- Race-card and race-result handlers are registered in the Collector; the Lambda entry no longer dispatches by the legacy job type and deduplication key.
- Periodic race discovery is now represented as an ordinary `race-discovery` resource request instead of a legacy planning job.
- Terraform provisions the replacement resource-collection queue and DLQ under new names; applying this change replaces and removes the old queue pair while retaining the Lambda/SQS topology.

- 2026-09-11: `.codegraph/` がないため `rg` と対象ファイルの直接確認で調査した。
- 2026-09-11: production code は変更していない。本文書と canonical documentation のみ Proposed として作成・更新した。
- 2026-09-11: 利用者指示により compatibility adapter を用いた段階移行案を撤回し、既存収集ジョブ実装の完全置換、予想ジョブ分離、意味的 migration、controlled cutover、rollback、旧コード削除を計画へ追加した。
- 2026-09-11: 利用者指示により一括 cutover とし、旧収集ジョブデータ、旧 job ID/deduplication key、旧 main SQS queue、旧 DLQ を同じ切替作業内で削除する方針へ変更した。Domain Data、source citation、認証情報は削除対象外とした。
- 2026-09-11: 利用者が「それでは実装をお願いします」と明示し、本記録を承認した。Status を Approved とし実装を開始する。
- 2026-09-11: 新 namespace `HorseRacingPrediction.CollectionOperations.CollectionPlatform` に Resource/Definition/Revision/Impact/State/Request/Task/ActiveTask/Attempt/Location/DispatchOutbox の独立 EF Core schema と store を追加した。旧 `ProcessingStateStore` は参照していない。
- 2026-09-11: 同一 Resource + Definition の active task 一意性、同 revision の再取得、理由別 request 履歴、lease token/dispatch generation、transient retry、lease expiry 復旧、attempt 証跡、再起動復元を実装した。
- 2026-09-11: `dotnet build src/HorseRacingPrediction.CollectionOperations/HorseRacingPrediction.CollectionOperations.csproj --no-restore` は警告0・エラー0で成功した。
- 2026-09-11: `dotnet test tests/HorseRacingPrediction.Collector.Tests/HorseRacingPrediction.Collector.Tests.csproj --no-restore --filter FullyQualifiedName~CollectionPlatformStoreTests` は4件成功・失敗0件だった。初回はSQLiteのDateTimeOffset比較変換制約で3件失敗し、Running候補の期限比較をメモリ側へ限定して修正後に再実行した。
- 2026-09-11: Revision impact の `All` / `SpecificResources` / `DateRange` / application登録済み `NamedCondition` 評価と対象ResourceだけのRequiredRevision/Stale更新を実装した。未登録NamedConditionはrevisionを有効化しない。
- 2026-09-11: ResourceLocation の複数候補、Active優先、LastVerifiedAt、transient failureでは無効化しない規則、NotFound/UnexpectedPage/ValidationFailureのSuspect化、成功時の再検証を実装した。
- 2026-09-11: CollectionPlatformStoreテストを8件へ拡張し、revision非影響Resource、未登録条件拒否、一時障害、UnexpectedPage、再検証を含め全件成功した。
- 2026-09-11: 新管理API `/api/admin/collection/requests|tasks|states` と新Worker API `/api/internal/collection/tasks/{taskId}/acquire|complete` を追加し、通知契約を `{taskId, dispatchGeneration}` とした。Api起動時に初期6 definition を登録する。
- 2026-09-11: `dotnet build src/HorseRacingPrediction.Api/HorseRacingPrediction.Api.csproj --no-restore` は警告0・エラー0で成功した。
- 2026-09-11: `ICollectionDefinitionHandler` registry、共通task executor、Realtime優先と連続Realtime上限によるBackground最低処理を持つlane allocatorを実装した。重複definitionとResourceType不一致は起動/解決時に拒否する。
- 2026-09-11: 新taskから既存 `JraRaceCardCollectionWorkflow` / `JraRaceResultCollectionWorkflow` を再利用するRaceCard/Result handlerを追加した。結果未確定は失敗確定せずRetry、domain write errorはValidationFailureに分類する。
- 2026-09-11: `dotnet build src/HorseRacingPrediction.Collector/HorseRacingPrediction.Collector.csproj --no-restore` は警告0・エラー0、CollectionPlatform関連テストは12件成功・失敗0件だった。
- 2026-09-11: 新RaceCard/Result taskへeffective dateとcourse/number/domainRaceId属性を渡し、既存workflowを呼び出すhandlerを実装した。Collector単体buildは警告0・エラー0で成功した。
- 2026-09-11: `dotnet test HorseRacingPrediction.sln --no-restore` を実行した。Contracts 38、Domain 96、MachineLearning 14、Application 56、Infrastructure 11、Agents 107、Collector 136、Api 123（skip 1）は成功した。Scrapingは210件中200件成功・10件失敗した。7件は固定日2026-09-05が実行日2026-09-11のRaceCardLookupPeriod外になった時刻依存、3件はJRA実サイトの現行RaceCardで馬主欠落/UnknownPageとなった外部サイト依存であり、今回変更した新CollectionPlatformコードを経由しない既存テストだった。新規CollectionPlatform 12件は別実行で全件成功している。
- 実装検証は承認後に Phase ごとに追記する。

## Deviations and follow-up

- Odds API は unavailable response のみで snapshot domain model/parser はないため Phase 7 は新規 domain capability を含む。
- `20260909_autonomous-historical-race-backfill` の実装を否定せず、共通 Resource/Policy/Batch projection へ移管する。
- Horse/Jockey/Trainer identity は alias mapping を導入し、既存 ID の一括変更や URL からの推測を行わない。

# 競馬情報収集状態管理基盤

- Status: Proposed
- Owner: HorseRacingPrediction maintainers
- Created: 2026-09-11
- Updated: 2026-09-11

## Context

現行基盤は Api が SQLite 上の永続ジョブを正本として保持し、outbox と SQS を配送通知に限定し、Collector が lease token を取得して単一タスクを実行する。レース、開催日、馬プロフィール、馬履歴等の個別状態・再取得・優先度制御は実装済みだが、収集対象の識別、収集定義、抽出仕様 revision、現在状態、取得理由、実行、試行、URL 候補が一つの共通モデルになっていない。

本変更は既存 Parser / Page / Workflow と Api の domain write を維持しながら、収集制御を Resource 中心へ段階移行する。調査結果は [current-state.md](current-state.md)、要求との差分は [gap-analysis.md](gap-analysis.md) を参照する。

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

## Experience and interaction design

管理 API/UI は Resource と CollectionDefinition を起点に、最新状態、次回予定、適用/要求 revision、直近 request/task/attempt、利用 location を表示する。手動再取得は `ManualRefresh` request を通常経路へ追加する。一括再取得はプレビュー後に複数 request へ展開する。

既存ジョブ画面は移行中も残し、task に legacy job reference を保持する。完全移行後の旧画面・旧テーブル削除は別 change record とする。UI モックは Phase 9 実装前に情報設計として追加する。

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
- `docs/00-system-architecture.md`: Collector 制御の正本リンクと段階移行方針を追加する。
- `docs/01-lambda-collector-architecture.md`: SQS は通知のみ、Api が新モデルの状態正本という境界を追記する。
- `docs/11-automation-design.md`: policy/lane/priority scheduling の正本リンクを追加する。
- `docs/22-collector-design.md`: 既存ジョブからの段階移行と未実装状態を追加する。
- `docs/23-jra-scraping-redesign.md`: Navigator/Parser/Page の再利用境界を追加する。

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

既存 `jobs`, `job_attempts`, `race_data_collection_statuses`, `agent_acquisition_statuses`, `result_day_collection_statuses` は一括削除しない。compatibility adapter と migration、dual-read 比較後に新モデルを正本化する。

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

- 採用: 既存 Api job controller を拡張し、別収集サービスを作らない。
- 採用: 履歴と最新状態 Projection を分ける。
- 採用: revision impact は宣言的 scope + 名前付き selector とする。
- 採用: active task のみ一意にし、terminal task の履歴重複を許容する。
- 採用: lane quota + weighted fairness + aging。
- 採用: Strangler migration で definition 単位に切り替える。
- 不採用: `AgentJobType` を ResourceType とみなす。対象、理由、集約、処理が混在するため。
- 不採用: URL/`SourceIdentity` を canonical Resource ID にする。
- 不採用: current revision 未満を一律 stale にする。

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

## Delivery plan

各 Phase は model/schema、compatibility、worker、tests、documentation を検証可能なコミットに分ける。

1. Core model、永続化、状態遷移、revision registry、legacy mapping、read-only projection。既存実行経路は変えない。
2. 単一 RaceCard/Result を adapter 経由で request/task/attempt/state に記録する。
3. ResourceLocation、resolver、page identification、direct/fallback/discovery を Race に導入する。
4. Horse/Jockey/Trainer と ResourceReference discovery を統合する。
5. 日/月 batch を projection として新基盤へ移し、期間 discovery から Race request を展開する。
6. schedule policy、lane、dynamic priority、fair allocation、aging を有効化する。
7. RaceOdds parser/write model/snapshot と反復収集を実装する。
8. revision impact、部分再取得、進捗 projection を実装する。
9. 手動/一括再取得 API/UI と監視画面を実装する。
10. definition ごとの比較合格後、新 state を正本化する。旧 table 削除は別変更とする。

## Verification record

- 2026-09-11: `.codegraph/` がないため `rg` と対象ファイルの直接確認で調査した。
- 2026-09-11: production code は変更していない。本文書と canonical documentation のみ Proposed として作成・更新した。
- 実装検証は承認後に Phase ごとに追記する。

## Deviations and follow-up

- Odds API は unavailable response のみで snapshot domain model/parser はないため Phase 7 は新規 domain capability を含む。
- `20260909_autonomous-historical-race-backfill` の実装を否定せず、共通 Resource/Policy/Batch projection へ移管する。
- Horse/Jockey/Trainer identity は alias mapping を導入し、既存 ID の一括変更や URL からの推測を行わない。

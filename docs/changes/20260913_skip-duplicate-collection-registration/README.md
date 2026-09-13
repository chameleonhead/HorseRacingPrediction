# 収集済み対象の通常タスク重複登録抑止

- Status: Implemented
- Owner: HorseRacingPrediction team
- Created: 2026-09-13
- Updated: 2026-09-13

## Context

現行の収集基盤は、同じ `Resource + CollectionDefinition` に対する active task を一件に制限する一方、terminal task の後には同じ revision の通常 request から新しい task を作成する。バックフィルや関連 Resource の発見を再実行すると、既に同じ取得処理版で収集対象として登録済みの過去データにも task と outbox が再作成され、変化しないデータを再取得する。

過去データの通常収集は一度登録した事実を再利用し、取得処理の revision が変わった場合、利用者が明示的に再取得した場合、または失敗復旧を依頼した場合だけ新しい task を作る。

## Goals

- 初回収集、バックフィル、関連情報の発見による通常 request は、同じ Resource、Definition、requested revision の task が既にあれば新しい task を作らない。
- 取得処理 revision の変更、手動再取得、失敗復旧は既存履歴にかかわらず再取得できる。
- active task の一意性、同一 batch の冪等性、retry、lease、outbox の既存保証を維持する。
- 重複と判定した request は request/task/outbox の新規行を増やさず、既存 task を示す受付結果を返す。

## Non-goals

- 既存の重複 task や request 履歴を削除・統合すること。
- Worker 内の HTTP 取得、parser、domain write の動作を変更すること。
- RaceCard、RaceOdds、プロフィール等の定期更新方針や更新間隔を変更すること。
- 同じ task の一時障害 retry、lease 切れ回収、SQS 重複配送を抑止すること。

## Experience and interaction design

管理画面からの「再取得」は `ManualRefresh` のため、同一 revision の収集済み対象にも従来どおり新しい task を作成する。障害画面からの復旧は `Recovery` のため、失敗履歴があっても新しい task を作成できる。

バックフィルや関連発見が既登録対象へ到達した場合は成功した no-op とする。呼び出し側には `CreatedTask = false` と既存の request/task ID を返し、配送待ち件数を増やさない。現在の API 契約は維持し、新しい利用者操作は追加しない。

## Documentation updates

- `docs/26-collection-platform-design.md`: 通常登録の revision 単位の冪等性と、定期更新・仕様変更・手動再取得・復旧の例外を収集基盤の canonical invariant として追記する。
- `docs/changes/20260911_unified-collection-platform/README.md`: 既存の承認・実装履歴として確認した。過去記録は変更せず、本 change record と canonical design で後続仕様を管理する。

## Technical impact

### Registration identity

通常登録の重複判定キーは、正規化済みの次の組とする。

`Resource(Type, Provider, Id) + CollectionDefinition + RequestedRevision`

URL、batch ID、lane、priority、effective date、attributes は取得対象または実行優先度の補助情報であり、同じデータ取得を別 task にする識別子にはしない。既存 Resource の location、effective date、attributes の補完は、重複判定前に現在どおり反映できる。

### Reason policy

| Reason | 同一 revision の既存 task がある場合 |
| --- | --- |
| `Initial` | 既存 task を返し、新規登録を省略 |
| `Backfill` | 既存 task を返し、新規登録を省略 |
| `Discovery` | 既存 task を返し、新規登録を省略 |
| `ScheduledRefresh` | 定期更新 policy が必要と判断した取得として新規 task を許可 |
| `DefinitionChanged` | revision 変更に伴う再取得として新規 task を許可。同一 revision の同じ revision-expansion batch は既存 batch 冪等性で抑止 |
| `ManualRefresh` | 明示的再取得として新規 task を許可 |
| `Recovery` | 明示的な失敗復旧として新規 task を許可 |

`Initial`、`Backfill`、`Discovery` でも requested revision が過去の task より新しければ重複ではなく、新しい task を作成する。

### Persistence and concurrency

判定と task 作成は現在の store transaction と同一排他区間で行い、並行した通常 request が両方 task を作らないようにする。既存 task は status を問わず検索する。失敗 task の自動 retry は同じ task 上で継続し、terminal failure 後の再実行は `Recovery` または `ManualRefresh` を使う。

既存 active task がある場合は、理由にかかわらず現在どおりその task に集約する。active task 完了後に同じ通常 request が来た場合は、同一 revision の最新既存 task を返す。重複 no-op では新しい `CollectionRequest` を作らないため、受付結果の `RequestId` は既存 task に紐づく request の ID とする。

既存 DB には task の Resource、Definition、RequestedRevision が保存されているため、データ移行は不要とする。必要な検索 index は実装時の query plan と負荷試験で判断し、追加する場合も意味上の一意制約にはしない。手動・定期更新等では同一キーの履歴複数件を許すためである。

## Decisions

- 採用: 通常取得だけを revision 単位で冪等化し、定期更新と明示的再取得を理由で区別する。
- 採用: terminal status が成功か失敗かを問わず通常登録を抑止する。失敗後の再実行は既存の retry または `Recovery` / `ManualRefresh` へ集約し、発見処理の再走査だけで無制限に再取得しない。
- 採用: 重複 no-op では request 監査行も増やさない。実行されない依頼履歴の増加を避け、返却契約は既存 ID で維持する。
- 不採用: `Resource + Definition` だけで永久に抑止する。取得処理 revision が変わった際の再取得を妨げるため。
- 不採用: active task だけを抑止する。現行動作と同じで、完了済み過去データの再登録を防げないため。
- 不採用: DB の unique constraint だけで全理由を抑止する。手動再取得、定期更新、復旧の正当な複数履歴を表現できないため。

## Acceptance criteria

| ID | Criterion | State |
| --- | --- | --- |
| AC1 | 同じ Resource、Definition、revision の terminal task がある状態で `Initial`、`Backfill`、`Discovery` を依頼しても、request、task、outbox が増えず `CreatedTask = false` と既存 ID が返る | Verified |
| AC2 | 通常 request の requested revision が既存 task より新しい場合、新しい task と outbox が一件作成される | Verified |
| AC3 | 同じ revision でも `ManualRefresh`、`Recovery`、`ScheduledRefresh` は terminal task 後に新しい task を作成できる | Verified |
| AC4 | `DefinitionChanged` は対象 revision の再取得 task を作成でき、同じ expansion batch の再実行は重複作成しない | Verified |
| AC5 | active task がある同一対象への request は既存 active task に集約され、並行する通常 request でも新しい task は最大一件である | Verified |
| AC6 | 失敗の task 内 retry、lease expiry 回収、outbox dispatch、SQS 重複配送の既存テストが成功する | Verified |
| AC7 | バックフィルまたは発見 handler を再実行する統合経路で、登録済み同一 revision の子 task が増えず、未登録または新版対象だけが dispatch される | Verified |
| AC8 | API/UI の手動再取得および障害復旧経路が同一 revision の既存履歴を越えて task を作成できる | Verified |

## Delivery plan

1. Store の request 登録に reason policy と既存 task 検索を追加し、重複 no-op の返却を実装する。
2. 通常理由、revision 更新、手動、復旧、定期更新、active/terminal、並行要求を store test で検証する。
3. Backfill/Discovery から API、永続化、outbox までの統合経路と、手動再取得・復旧 API を検証する。
4. query plan を確認し、必要なら検索 index を schema migrator に追加する。
5. 関連 regression test、Release build、`git diff --check` を実行し、結果と設計差分を本記録へ追記する。

## Verification record

- 2026-09-13: 利用者が提示した設計内容を承認。Execution Mode へ移行した。
- 現行 `CollectionPlatformStore.RequestAsync` は batch ID と active task のみを重複抑止し、terminal task の同一 revision は検索しないことを確認した。
- `CollectionReason` の全理由、`JraCollectionSchedulePolicy`、scheduler、Backfill/Discovery、ManualRefresh/Recovery、revision expansion の呼び出し経路を確認した。
- `CollectionPlatformStore.RequestAsync` に通常理由の同一 `Resource + Definition + RequestedRevision` 検索を追加した。重複時は既存 request/task ID を返し、Resource の補完情報だけを保存して request/task/outbox を作らない。
- `collection_tasks (ResourcePk, DefinitionId, RequestedRevision, CreatedAt)` の非一意 index と schema version 8 migration を追加した。新規 DB、履歴なし既存 DB、version 6/7、同時初期化をストアテストで検証した。
- Store の通常3理由、4種の再取得理由、新 revision、active 集約、8並行 request を含む対象テスト55件が成功した。
- Revision、bulk、Discovery handler を含む Collector 対象テスト26件、ManualRefresh/Recovery/Discovery API を含む Api 対象テスト46件が成功した。
- 実 API → store → outbox → SQS契約 → Worker → Discovery子request の統合テストに同一子Resourceの再発見を追加し、`CreatedTask = false`、既存Task ID返却、task総数不変を確認した。
- `dotnet test tests/HorseRacingPrediction.Collector.Tests/HorseRacingPrediction.Collector.Tests.csproj --no-restore`: 169件成功、失敗0。
- `dotnet test tests/HorseRacingPrediction.Api.Tests/HorseRacingPrediction.Api.Tests.csproj --no-restore`: 184件成功、失敗0、外部条件付き1件skip。
- `dotnet build HorseRacingPrediction.sln -c Release --no-restore`: 成功、警告0、エラー0。
- `git diff --check`: 問題なし。

## Deviations and follow-up

- 設計との差分なし。残課題なし。

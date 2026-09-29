# Refit API clients and resource-scoped contracts

- Status: Approved
- Change record schema: 2
- Owner: Lead / repository user
- Created: 2026-09-30
- Updated: 2026-09-30
- JRA site contract impact: None — HTTP DTO整理のみ。スクレイピング・サイト識別・収集判断は変更しない。

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Not started | 設計承認後にDTO移行、Refit、Factory、DI拡張を実装 |
| Verification | Not started | 設計の静的調査済み。実装・新HTTP契約適合・回帰検証は未実施 |
| Deployment/operation | Not applicable | 新クライアントの稼働ホストへの導入・デプロイは対象外 |

## Context

利用者はApiClientにAPIごとのRefitクライアント、DTOの統一命名とAPI別名前空間、単一Factory、DI拡張を求めている。2026-09-30にCollectionの運用・修復を含む全業務APIを対象とする回答を得た。この回答は範囲の確定であり、本設計の実装承認ではない。

調査起点は`8c187433`、初期worktreeはclean。ApiClientはnet10.0、Refit/Refit.HttpClientFactory 16.3.0を参照済みで、IRaceQueryService/IPredictionWriteServiceのみを持つ。Contractsのrootに要求・応答・DTO・enum・補助型が混在し、Collectionの一部HTTP契約はApi/CollectionOperationsの型に依存する。

## Goals and non-goals

全業務APIの型付きRefitインターフェース、API別Contracts、共通Factory、未導入のDI拡張を提供する。新クライアント自身の新HTTP契約への適合と、同時追従する既存利用者の回帰を検証する。

既存HTTPクライアントの差し替え、ホストでの新DI登録、UI変更、DB/イベントschema変更、収集ポリシー変更、デプロイは行わない。型参照追従、公開DTOへの境界変換、新Request/Responseの組立・取り出しは必要な変更範囲。login/logout・UI・Swagger・healthは新クライアントの対象外。

## Decisions

正規設計は[APIクライアント設計](../../28-api-client-design.md)。

1. APIグループごとに`I{Resource}Api`、`Contracts.{Resource}`。共通型はCommon（日時補助はCommon.Time）。他APIから参照されるだけでCommonへ移さない。
2. 全135操作で入力/戻りデータの有無を確認し、両方ある場合に同じ語幹の専用Request/Responseを対で用意。入力なしはRequest不要、戻りデータなしはResponse不要。空型/空オブジェクトは禁止。業務データの塊はDtoとし、Responseは`RaceDto Race`等のプロパティで持つ。直下項目は操作メタデータ等に限り都度理由を記録。Requestもデータの塊は入力専用Dtoへ分離。GET/DELETEのbody追加はしない。異なる形状は無理に統合せず旧C#名の互換ラッパーは残さない。
3. 内部モデルをそのまま共有ライブラリへ移さず、wire DTOとマッピングを作る。永続型・実行型の名前/配置を維持し、ApiClient/Contractsからサーバープロジェクトへの参照を禁止する。
4. 単一`IApiClientFactory.Create<TApi>()`で登録済みAPIだけを生成する。`AddHorseRacingApiClient`で共通HttpClient設定を用意し、IHttpClientBuilderでhandlerを追加可能にする。APIキーは任意、絶対BaseAddress/Timeout/header設定を検証。秘密を記録しない。自動retryなし。
5. 公開操作は入力がある場合だけ専用Requestを受ける。戻りデータありはApiResponse<専用Response>、なしはIApiResponse。空Request/Response/JSONは作らず、既存の204を含む成功status/Location・エラー・認証は維持。DTOラップのJSON構造変更を許容し、業務値/null/enum/日時/既定値は維持する。
6. Roslynの意味解析とRenameで型名/参照を変更し、型単位のnamespace移動とalias追従をツール化する。manifest、dry-run、衝突検出を設ける。Razor・文字列参照は検索とbuildで補完する。
7. Refit 16.3.0を維持。既存抽象サービスとHTTP実装を維持し、導入は別途とする。

2026-09-30追加指示（最新の訂正を反映）: 入力と戻りデータが両方ある操作だけRequest/Responseを対とする。片側がなければ対応する型は不要で空オブジェクトは禁止。ResponseにRaceの項目を直接置かずRaceDtoを持たせる。データの塊は必ずDto。直下項目の可否は都度判断する。これに伴い、当初の「HTTP JSON形状不変」制約を撤回する。OpenAPI変更許容の明示回答に整合する。最新訂正を含め2026-09-30の「不明点はありますか？なければ実装をお願いします。」により設計全体を承認。

## Hypothesis ledger

| Claim | Fact boundary / evidence | Falsification / result | Disposition |
| --- | --- | --- | --- |
| Refitを追加インストールする必要がある | ApiClient.csprojは既に両Refit package 16.3.0を参照 | csprojとNuGet同梱READMEを確認し否定 | version維持 |
| 全DTOはContractsだけに存在する | CollectionController/CollectionPlatformContracts.csはApi所属、CollectionOperations型を参照 | endpointと型宣言を確認し否定 | wire DTOを切り出す |
| Summary等の型は名前だけ統一して結合できる | PredictionTicketSummaryReadModelはMarks、SummaryResponseはstatus/count等で形状が異なる | 宣言比較で否定 | データを意味名のDtoとし操作Responseから参照する |
| namespace変更はHTTP仕様に一切影響しない | Program.csはFullNameをOpenAPI schema IDに使用 | CustomSchemaIdsを確認し否定 | 当初はJSON維持案。追加要件によりDTOラップも変更対象へ修正 |
| CodeGraphで参照解析可能 | .codegraphは存在するがCLIがPATHに存在せずMCPも未提供 | codegraph explore失敗 | 静的検索/ソースを使用、実装後は利用可否を再確認しgraph検証を偽称しない |

## Concern and agreement ledger

| ID | Concern and evidence | Impact | Proposed disposition | AC/task/test | Agent position | User disposition | State |
| --- | --- | --- | --- | --- | --- | --- | --- |
| C1 | CLR名前とFullName由来OpenAPI schema IDが変化する | 外部C#利用者・schema ID依存生成物は再生成が必要 | 読みやすさを優先して改名・DTOラップを行い、リポジトリ内全参照とHTTP組立/取出しを更新。旧名併存は不採用。残存riskはリポジトリ外の利用者でありリリース前に差分確認 | AC2/AC5/T2/T5; 同名異形応答を統合しない | Agree | 2026-09-30: OpenAPI IF変更を許容し将来の読みやすさ・わかりやすさ優先との明示回答 | Resolved in design |
| C2 | Collection等は内部モデルを返す | 安易な移動で永続化/依存関係に影響 | wire DTOと明示mapping、内部型は維持。内部project参照追加案を却下。残存riskはfield漏れで新旧データ対応テストを必須化 | AC2/AC5/T3/T5; optional/nested/repeated項目 | Agree | Approved 2026-09-30 | Resolved in design |
| C3 | DTO参照追従には既存利用者側の編集が必要 | 「未導入」との混同 | 型参照/境界変換とRequest組立/Response取出しを追従し、新DI呼出やRefit切替を禁止。完全無編集案はbuild不能。残存riskは意図外経路変更をdiff/runtime登録検査で排除 | AC4/T2/T3/T5 | Agree | Approved 2026-09-30 | Resolved in design |
| C4 | RoslynのみではRazor・文字列参照を保証できない | compile/実行時の取りこぼし | manifest+semantic変更+literal検索+Razor build。正規表現のみ案は却下。旧識別子の意図的残存は分類 | AC2/AC5/T2/T5 | Agree | Approved 2026-09-30 | Resolved in design |
| C5 | Refit既定のquery/日時/enum・error処理は既存HTTPと差が出る可能性 | 404/409/204、本文なし、日付絞込、部分応答の不整合 | 共通serializer/明示query設定/既存status/error維持、代表実HTTPと全route coverageを検証。自動retry却下。残存riskは外部API稼働差分であり本番アクセスは不要 | AC1/AC3/AC5/T4/T5 | Agree | Approved 2026-09-30 | Resolved in design |
| C6 | DTOラップは既存flat JSONと非互換。入力/戻りなしまで対を強制すると不要な空オブジェクトが生まれる | 既存利用者のdeserializeや本文なし契約に影響 | データありのRequest/Responseへ組立/取出しを全利用者で同時追従。入力なしはRequestなし、戻りなしはResponseなし。204と本文なしを維持。外部利用者は将来の同時更新が必要で今回はデプロイしない | AC2/AC3/AC4/AC5; T3/T4/T5; GET bodyなし・204・一覧DTO | Agree | 利用者が空オブジェクト禁止・片側なしの対は不要と明示訂正 | Resolved in design |

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | 台帳の全業務routeがAPI別Refitメソッドへ一対一対応し、method/path/query/body/CancellationTokenがサーバー契約と一致 | T1,T4,T5 | endpoint metadata対照、生成client送信検査、対象漏れ0 | Not started |
| AC2 | 全135操作の入力/戻りの有無、必要なRequest/Response、API別namespace、業務データDto分離がmanifestと一致。空型・空オブジェクトを作らない。直下項目は理由を記録しエンティティ項目を展開しない。Commonに共通型。新旧JSONパスを対応付け、値/null/enum/日時/既定値を維持し全利用者がbuild可能 | T2,T3,T5 | 旧名検索、契約fixture比較、依存参照検査、全build | Not started |
| AC3 | 一つのFactoryから全登録APIを生成でき、未登録型と不正設定を拒否。DIからの解決、共通header/serializer/handler/cancellation/error/入力なし・本文なしが動作 | T4,T5 | Factory/DI/HTTPテスト（404/409/204、Request不要の呼出、キャンセル、並行別API呼出） | Not started |
| AC4 | 拡張と使用例は提供するが、既存ホストに新DI登録を追加せず既存HTTP実装を置換しない | T2,T3,T4,T5,T6 | 起動処理/既存call path差分、登録検索、既存回帰 | Not started |
| AC5 | ツールのdry-run・衝突検出・実適用を検証し、新clientの実HTTP往復と既存回帰/formatを通過。承認したDTOラップ以外に意図しない契約差分なし | T2,T3,T4,T5 | Roslyn fixture、TestServer経由の読み書き/認証失敗、CI相当build/test/format | Not started |
| AC6 | 移行manifest、使用例、検証結果、全route台帳とこの記録が最終実装と一致 | T1,T2,T5,T6 | 文書validator、台帳照合、Lead最終監査 | Not started |

## Documentation updates

- `docs/28-api-client-design.md`: 新設。クライアント設計、命名、Factory/DI、移行ツール、未導入境界の正規文書。提案状態を明記。
- `docs/00-system-architecture.md`: Contracts/ApiClientの古い説明を実態へ訂正し、新設計へのリンクを追加。
- README、収集基盤/予測設計も影響調査対象。稼働経路・運用・永続化は変わらないため、それらの設計を先行して書き換えない。

## Task plan

全コード編集タスクを直列化し、同じ参照先やformatterで競合しない。各workerは一人がwrite ownerとなり、他の作業を取り消さない。public contract判断はLeadに戻す。Leadは設計と最終受入のみを保持（不可分なpublic contract/統合判断）。各workerが最小testとhandoff前regressionを実行し、Leadが独立証拠で採否を決める。

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State | Routing | Audit | Result metrics |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | 全API棚卸し（AC1/AC6） | inventory explorer | requested gpt-6-luna/high | - | route-inventory.mdのみ | endpoint宣言と登録/route検索照合 | 136/136 source・登録との独立照合、重複0 | Verified | 境界の明確な調査 | read-only research | unavailable; retries 0; corrections 0; reviews 1 |
| T2 | Roslyn移行ツール、全共有DTO分類/改名/参照追従（AC2/4/5/6） | contract-migration worker | gpt-6-luna/high予定 | 設計承認 | tools/ContractMigration; src; tests; docs/changes/20260930_refit-api-clients/contract-migration.json | tool fixture、Contracts tests、solution build | 未着手 | Runnable | 意味判断はLead、確定規則下の移行を直列実行 | T2-A1 | unavailable; retries 0; corrections 0; reviews 0 |
| T3 | 入力/戻りの有無に応じた操作別Request/Response、DTO、サーバー/既存利用者mapping（AC2/4/5） | wire-contract worker | gpt-6-luna/high予定 | T2 | Contracts、Api境界mapping、関連test、既存利用者のRequest組立/Response取出し | 新契約HTTP試験、旧→新データ保存性、既存利用者回帰 | 未着手 | Dependent | 内部実行型を維持し独立比較 | dispatch時作成 | unavailable; retries 0; corrections 0; reviews 0 |
| T4 | 全Refitインターフェース、Factory、DI（AC1/3/4/5） | api-client worker | gpt-6-luna/high予定 | T3 | ApiClient、新ApiClient.Tests、slnへのtest追加 | 全route送信、DI/Factory/error/cancel tests | 未着手 | Dependent | 凍結済みHTTP契約を実装 | dispatch時作成 | unavailable; retries 0; corrections 0; reviews 0 |
| T5 | 独立HTTP往復/全回帰と局所修正（AC1-6） | verification worker | gpt-6-luna/high予定 | T4 | tests、直列の限定修正、検証記録 | TestServer、全solution test/build/format | 未着手 | Dependent | Leadが契約逸脱を判定し局所修正はworkerへ戻す | dispatch時作成 | unavailable; retries 0; corrections 0; reviews 0 |
| T6 | 正規文書/台帳の最終反映と受入（AC4/6） | Lead | Lead | T5 | docs | 差分、独立反証、validator、task/AC追跡 | 未着手 | Dependent | 最終受入判断 | none | unavailable; retries 0; corrections 0; reviews 0 |

observed model・token・費用はtoolで証明されない限り未確認。調査T1のrequested値から推測しない。実装workerのauditはdispatch時に作成し`scripts/audit_agent_execution.py`で検証する。永続routing変更は行わない。

## Review gates

- Design and task-split review: Lead。設計・AC対応・直列ownershipを確認。route台帳を独立ソース抽出と登録宣言に照合し136/136、重複0。対象135/対象外1の収支一致。
- Concern and agreement review: Lead。C1-C6を設計で処置。外部CLR/schema ID互換性・未導入境界を承認依頼に含める。2026-09-30の実装指示でC1-C6を含む最新設計を承認。
- Pre-implementation review: Lead / 2026-09-30。T2をRunnable、T3-T6をDependentとして直列実行。T2はContracts/全参照の機械的移行とtools/ContractMigration、manifestのみ。新HTTP契約はT3で扱い、T2ではJSON/永続型不変。移行tool fixture→Contracts tests→solution Release buildをworker実行。反例は同名型/alias/Razor/enum/異形Summary。判断不明はLeadへ戻し、他タスクの書込は開始しない。
- Checkpoint review: DTO移行、wire mapping、client/DI、統合検証の検証済み単位で実施・コミット。
- Final review: 未実施。全AC/タスクVerified、禁止runtime導入0、新契約適合/既存利用者追従、文書同期を確認してImplementedにする。

## Verification record

設計調査のみ。プロダクションコードは変更していない。変更記録validator issues=0、git diff --check成功。前回は135操作の命名候補を照合（同一語幹、欠落0、重複0）。最新訂正で両型必須を撤回。台帳の型名は入力/戻りがある場合だけ適用する命名候補とし、型作成前にendpointの入力/戻りの実態で不要な側を除外する。Lead独立抽出で136 route全件がREADME台帳と一致し、登録宣言・重複なしを確認した。認証middlewareを独立確認しGETも保護対象と設計へ反映。CodeGraph CLI/MCPは使用不能のためgraph根拠なし。Refit 16.3.0の同梱README/XMLと公式ドキュメントを参照。実装後のCI主要gate:

```powershell
dotnet restore HorseRacingPrediction.sln
dotnet format HorseRacingPrediction.sln --no-restore --verify-no-changes
dotnet build HorseRacingPrediction.sln --no-restore --configuration Release
dotnet test HorseRacingPrediction.sln --no-build --configuration Release --collect:"XPlat Code Coverage" --filter "TestCategory!=External" --logger "trx;LogFileName=app-ci.trx"
git diff --check
git status --short
```

API実HTTPfixtureはTestApplicationFactory/TestServerを利用し、外部データ更新不要。書込はテストDBへ隔離。新client→HTTP→server→応答のhappy path/認証失敗/競合とDTOラップ後の新契約および業務データ保存性を検証する。DTO移行ではDomain/永続モデルの変更がないことも検査する。

## Next action and remaining work

設計承認済み。T2のmanifest/dry-run/tool検証から開始。T2-T6と全ACは未完了。意図的な未コミットは本変更記録、route台帳、正規設計、architecture更新のみ。設計段階で実装完了を主張しない。

## API contract inventory

次の台帳は全発見routeを列挙し、クライアント対象/対象外・対応先・維持する契約と利用者を示す。詳細調査は[route-inventory.md](route-inventory.md)。

照合: 136 route = 対象135 + 対象外Health 1。対象13クライアント: Collection 44 / Races 29 / Predictions 11 / Horses 9 / Jockeys 8 / Trainers 7 / Repairs 7 / Owners 6 / Memos 5 / PredictionScheduling 3 / Identity 2 / MachineLearning 2 / Subjects 2。method合計GET 53 + POST 62 + PUT 10 + PATCH 10 + DELETE 1 = 136。

全対象の共通不変条件: W1=method/path/query/既定値維持、bodyは専用Requestへ整理、W2=DTOラップ後も業務値/type/null/enum/JST維持、W3=204を含むstatus/本文なし/Location/error/認証/filter維持、W4=既存利用者の同じHTTP実装を新Request/Responseへ追従。各行の利用者群はroute-inventory.mdのCoverage and existing consumers表を参照（全利用者が全routeを呼ぶという意味ではない）。各行のsourceは`src/HorseRacingPrediction.Api/Endpoints/{Family}/{Endpoint source}`。route constraintはサーバー側で維持しRefit URL placeholderには含めない。

| Family | HTTP | Full path | Disposition / target | Invariant / consumer evidence | Endpoint source |
| --- | --- | --- | --- | --- | --- |
| Races | GET | `/api/races` | 対象・IRacesApi; 入力あり: `SearchRacesRequest` / 戻りあり: `SearchRacesResponse` | W1-W4; Races利用者群 | `SearchRacesEndpoint.cs` |
| Races | POST | `/api/races` | 対象・IRacesApi; 入力あり: `CreateRaceRequest` / 戻りあり: `CreateRaceResponse` | W1-W4; Races利用者群 | `CreateRaceEndpoint.cs` |
| Races | GET | `/api/races/{raceId}` | 対象・IRacesApi; 入力あり: `GetRaceRequest` / 戻りあり: `GetRaceResponse` | W1-W4; Races利用者群 | `GetRaceEndpoint.cs` |
| Races | PATCH | `/api/races/{raceId}` | 対象・IRacesApi; 入力あり: `CorrectRaceDataRequest` / 戻りあり: `CorrectRaceDataResponse` | W1-W4; Races利用者群 | `CorrectRaceDataEndpoint.cs` |
| Races | POST | `/api/races/{raceId}/card/publish` | 対象・IRacesApi; 入力あり: `PublishRaceCardRequest` / 戻りあり: `PublishRaceCardResponse` | W1-W4; Races利用者群 | `PublishRaceCardEndpoint.cs` |
| Races | POST | `/api/races/{raceId}/close` | 対象・IRacesApi; 入力あり: `CloseRaceLifecycleRequest` / 戻りあり: `CloseRaceLifecycleResponse` | W1-W4; Races利用者群 | `CloseRaceLifecycleEndpoint.cs` |
| Races | GET | `/api/races/{raceId}/comparison` | 対象・IRacesApi; 入力あり: `GetPredictionComparisonRequest` / 戻りあり: `GetPredictionComparisonResponse` | W1-W4; Races利用者群 | `GetPredictionComparisonEndpoint.cs` |
| Races | GET | `/api/races/{raceId}/context` | 対象・IRacesApi; 入力あり: `GetRacePredictionContextRequest` / 戻りあり: `GetRacePredictionContextResponse` | W1-W4; Races利用者群 | `GetRacePredictionContextEndpoint.cs` |
| Races | POST | `/api/races/{raceId}/entries` | 対象・IRacesApi; 入力あり: `RegisterEntryRequest` / 戻りあり: `RegisterEntryResponse` | W1-W4; Races利用者群 | `RegisterEntryEndpoint.cs` |
| Races | PUT | `/api/races/{raceId}/entries/{entryId}` | 対象・IRacesApi; 入力あり: `UpdateEntryCollectedDataRequest` / 戻りあり: `UpdateEntryCollectedDataResponse` | W1-W4; Races利用者群 | `UpdateEntryCollectedDataEndpoint.cs` |
| Races | POST | `/api/races/{raceId}/entries/{entryId}/result` | 対象・IRacesApi; 入力あり: `DeclareEntryResultRequest` / 戻りあり: `DeclareEntryResultResponse` | W1-W4; Races利用者群 | `DeclareEntryResultEndpoint.cs` |
| Races | POST | `/api/races/{raceId}/open-pre-race` | 対象・IRacesApi; 入力あり: `OpenPreRaceRequest` / 戻りあり: `OpenPreRaceResponse` | W1-W4; Races利用者群 | `OpenPreRaceEndpoint.cs` |
| Races | POST | `/api/races/{raceId}/payout` | 対象・IRacesApi; 入力あり: `DeclarePayoutResultRequest` / 戻りあり: `DeclarePayoutResultResponse` | W1-W4; Races利用者群 | `DeclarePayoutResultEndpoint.cs` |
| Races | POST | `/api/races/{raceId}/reschedule` | 対象・IRacesApi; 入力あり: `MarkRaceRescheduledRequest` / 戻りあり: `MarkRaceRescheduledResponse` | W1-W4; Races利用者群 | `MarkRaceRescheduledEndpoint.cs` |
| Races | POST | `/api/races/{raceId}/result` | 対象・IRacesApi; 入力あり: `DeclareRaceResultRequest` / 戻りあり: `DeclareRaceResultResponse` | W1-W4; Races利用者群 | `DeclareRaceResultEndpoint.cs` |
| Races | POST | `/api/races/{raceId}/start` | 対象・IRacesApi; 入力あり: `StartRaceRequest` / 戻りあり: `StartRaceResponse` | W1-W4; Races利用者群 | `StartRaceEndpoint.cs` |
| Races | POST | `/api/races/{raceId}/track-condition` | 対象・IRacesApi; 入力あり: `RecordTrackConditionRequest` / 戻りあり: `RecordTrackConditionResponse` | W1-W4; Races利用者群 | `RecordTrackConditionEndpoint.cs` |
| Races | POST | `/api/races/{raceId}/weather` | 対象・IRacesApi; 入力あり: `RecordWeatherObservationRequest` / 戻りあり: `RecordWeatherObservationResponse` | W1-W4; Races利用者群 | `RecordWeatherObservationEndpoint.cs` |
| Races | POST | `/api/races/result-bulk` | 対象・IRacesApi; 入力あり: `DeclareRaceResultBulkRequest` / 戻りあり: `DeclareRaceResultBulkResponse` | W1-W4; Races利用者群 | `DeclareRaceResultBulkEndpoint.cs` |
| Races | POST | `/api/v2/admin/races` | 対象・IRacesApi; 入力あり: `CreateRaceFromScheduleRequest` / 戻りあり: `CreateRaceFromScheduleResponse` | W1-W4; Races利用者群 | `CreateRaceFromScheduleEndpoint.cs` |
| Races | POST | `/api/v2/admin/races/{raceId}/entry-repair-previews` | 対象・IRacesApi; 入力あり: `PreviewRaceEntryRepairRequest` / 戻りあり: `PreviewRaceEntryRepairResponse` | W1-W4; Races利用者群 | `PreviewRaceEntryRepairEndpoint.cs` |
| Races | GET | `/api/v2/admin/races/{raceId}/entry-repair/assignment-fence-state` | 対象・IRacesApi; 入力あり: `GetRaceAssignmentFenceRequest` / 戻りあり: `GetRaceAssignmentFenceResponse` | W1-W4; Races利用者群 | `GetRaceAssignmentFenceEndpoint.cs` |
| Races | PATCH | `/api/v2/admin/races/{raceId}/entry-repair/hold` | 対象・IRacesApi; 入力あり: `ReleaseRaceEntryRepairHoldRequest` / 戻りあり: `ReleaseRaceEntryRepairHoldResponse` | W1-W4; Races利用者群 | `ReleaseRaceEntryRepairHoldEndpoint.cs` |
| Races | PUT | `/api/v2/admin/races/{raceId}/entry-repair/hold` | 対象・IRacesApi; 入力あり: `UpdateRaceEntryRepairHoldRequest` / 戻りあり: `UpdateRaceEntryRepairHoldResponse` | W1-W4; Races利用者群 | `UpdateRaceEntryRepairHoldEndpoint.cs` |
| Races | GET | `/api/v2/admin/races/{raceId}/entry-repair/hold-state` | 対象・IRacesApi; 入力あり: `GetRaceEntryRepairHoldRequest` / 戻りあり: `GetRaceEntryRepairHoldResponse` | W1-W4; Races利用者群 | `GetRaceEntryRepairHoldEndpoint.cs` |
| Races | GET | `/api/v2/admin/races/{raceId}/entry-repair/inspection` | 対象・IRacesApi; 入力あり: `GetRaceEntryRepairRequest` / 戻りあり: `GetRaceEntryRepairResponse` | W1-W4; Races利用者群 | `GetRaceEntryRepairEndpoint.cs` |
| Races | POST | `/api/v2/admin/races/{raceId}/entry-repairs` | 対象・IRacesApi; 入力あり: `ApplyRaceEntryRepairRequest` / 戻りあり: `ApplyRaceEntryRepairResponse` | W1-W4; Races利用者群 | `ApplyRaceEntryRepairEndpoint.cs` |
| Races | GET | `/api/v2/admin/races/{raceId}/odds-snapshot-records` | 対象・IRacesApi; 入力あり: `ListRaceOddsSnapshotsRequest` / 戻りあり: `ListRaceOddsSnapshotsResponse` | W1-W4; Races利用者群 | `ListRaceOddsSnapshotsEndpoint.cs` |
| Races | POST | `/api/v2/admin/races/{raceId}/odds-snapshot-records` | 対象・IRacesApi; 入力あり: `CreateRaceOddsSnapshotRequest` / 戻りあり: `CreateRaceOddsSnapshotResponse` | W1-W4; Races利用者群 | `CreateRaceOddsSnapshotEndpoint.cs` |
| Horses | GET | `/api/horses` | 対象・IHorsesApi; 入力あり: `SearchHorsesRequest` / 戻りあり: `SearchHorsesResponse` | W1-W4; Horses利用者群 | `SearchHorsesEndpoint.cs` |
| Horses | POST | `/api/horses` | 対象・IHorsesApi; 入力あり: `RegisterHorseRequest` / 戻りあり: `RegisterHorseResponse` | W1-W4; Horses利用者群 | `RegisterHorseEndpoint.cs` |
| Horses | GET | `/api/horses/{horseId}` | 対象・IHorsesApi; 入力あり: `GetHorseProfileRequest` / 戻りあり: `GetHorseProfileResponse` | W1-W4; Horses利用者群 | `GetHorseProfileEndpoint.cs` |
| Horses | PATCH | `/api/horses/{horseId}` | 対象・IHorsesApi; 入力あり: `CorrectHorseDataRequest` / 戻りあり: `CorrectHorseDataResponse` | W1-W4; Horses利用者群 | `CorrectHorseDataEndpoint.cs` |
| Horses | PUT | `/api/horses/{horseId}` | 対象・IHorsesApi; 入力あり: `UpdateHorseProfileRequest` / 戻りあり: `UpdateHorseProfileResponse` | W1-W4; Horses利用者群 | `UpdateHorseProfileEndpoint.cs` |
| Horses | POST | `/api/horses/{horseId}/aliases` | 対象・IHorsesApi; 入力あり: `MergeHorseAliasRequest` / 戻りあり: `MergeHorseAliasResponse` | W1-W4; Horses利用者群 | `MergeHorseAliasEndpoint.cs` |
| Horses | GET | `/api/horses/{horseId}/participations` | 対象・IHorsesApi; 入力あり: `GetHorseParticipationsRequest` / 戻りあり: `GetHorseParticipationsResponse` | W1-W4; Horses利用者群 | `GetHorseParticipationsEndpoint.cs` |
| Horses | GET | `/api/horses/{horseId}/race-history` | 対象・IHorsesApi; 入力あり: `GetHorseRaceHistoryRequest` / 戻りあり: `GetHorseRaceHistoryResponse` | W1-W4; Horses利用者群 | `GetHorseRaceHistoryEndpoint.cs` |
| Horses | GET | `/api/horses/{horseId}/weight-history` | 対象・IHorsesApi; 入力あり: `GetHorseWeightHistoryRequest` / 戻りあり: `GetHorseWeightHistoryResponse` | W1-W4; Horses利用者群 | `GetHorseWeightHistoryEndpoint.cs` |
| Jockeys | GET | `/api/jockeys` | 対象・IJockeysApi; 入力あり: `SearchJockeysRequest` / 戻りあり: `SearchJockeysResponse` | W1-W4; Jockeys利用者群 | `SearchJockeysEndpoint.cs` |
| Jockeys | POST | `/api/jockeys` | 対象・IJockeysApi; 入力あり: `RegisterJockeyRequest` / 戻りあり: `RegisterJockeyResponse` | W1-W4; Jockeys利用者群 | `RegisterJockeyEndpoint.cs` |
| Jockeys | GET | `/api/jockeys/{jockeyId}` | 対象・IJockeysApi; 入力あり: `GetJockeyProfileRequest` / 戻りあり: `GetJockeyProfileResponse` | W1-W4; Jockeys利用者群 | `GetJockeyProfileEndpoint.cs` |
| Jockeys | PATCH | `/api/jockeys/{jockeyId}` | 対象・IJockeysApi; 入力あり: `CorrectJockeyDataRequest` / 戻りあり: `CorrectJockeyDataResponse` | W1-W4; Jockeys利用者群 | `CorrectJockeyDataEndpoint.cs` |
| Jockeys | PUT | `/api/jockeys/{jockeyId}` | 対象・IJockeysApi; 入力あり: `UpdateJockeyProfileRequest` / 戻りあり: `UpdateJockeyProfileResponse` | W1-W4; Jockeys利用者群 | `UpdateJockeyProfileEndpoint.cs` |
| Jockeys | POST | `/api/jockeys/{jockeyId}/aliases` | 対象・IJockeysApi; 入力あり: `MergeJockeyAliasRequest` / 戻りあり: `MergeJockeyAliasResponse` | W1-W4; Jockeys利用者群 | `MergeJockeyAliasEndpoint.cs` |
| Jockeys | GET | `/api/jockeys/{jockeyId}/participations` | 対象・IJockeysApi; 入力あり: `GetJockeyParticipationsRequest` / 戻りあり: `GetJockeyParticipationsResponse` | W1-W4; Jockeys利用者群 | `GetJockeyParticipationsEndpoint.cs` |
| Jockeys | GET | `/api/jockeys/{jockeyId}/race-history` | 対象・IJockeysApi; 入力あり: `GetJockeyRaceHistoryRequest` / 戻りあり: `GetJockeyRaceHistoryResponse` | W1-W4; Jockeys利用者群 | `GetJockeyRaceHistoryEndpoint.cs` |
| Trainers | GET | `/api/trainers` | 対象・ITrainersApi; 入力あり: `SearchTrainersRequest` / 戻りあり: `SearchTrainersResponse` | W1-W4; Trainers利用者群 | `SearchTrainersEndpoint.cs` |
| Trainers | POST | `/api/trainers` | 対象・ITrainersApi; 入力あり: `RegisterTrainerRequest` / 戻りあり: `RegisterTrainerResponse` | W1-W4; Trainers利用者群 | `RegisterTrainerEndpoint.cs` |
| Trainers | GET | `/api/trainers/{trainerId}` | 対象・ITrainersApi; 入力あり: `GetTrainerProfileRequest` / 戻りあり: `GetTrainerProfileResponse` | W1-W4; Trainers利用者群 | `GetTrainerProfileEndpoint.cs` |
| Trainers | PATCH | `/api/trainers/{trainerId}` | 対象・ITrainersApi; 入力あり: `CorrectTrainerDataRequest` / 戻りあり: `CorrectTrainerDataResponse` | W1-W4; Trainers利用者群 | `CorrectTrainerDataEndpoint.cs` |
| Trainers | PUT | `/api/trainers/{trainerId}` | 対象・ITrainersApi; 入力あり: `UpdateTrainerProfileRequest` / 戻りあり: `UpdateTrainerProfileResponse` | W1-W4; Trainers利用者群 | `UpdateTrainerProfileEndpoint.cs` |
| Trainers | POST | `/api/trainers/{trainerId}/aliases` | 対象・ITrainersApi; 入力あり: `MergeTrainerAliasRequest` / 戻りあり: `MergeTrainerAliasResponse` | W1-W4; Trainers利用者群 | `MergeTrainerAliasEndpoint.cs` |
| Trainers | GET | `/api/trainers/{trainerId}/participations` | 対象・ITrainersApi; 入力あり: `GetTrainerParticipationsRequest` / 戻りあり: `GetTrainerParticipationsResponse` | W1-W4; Trainers利用者群 | `GetTrainerParticipationsEndpoint.cs` |
| Owners | GET | `/api/admin/repairs/owner-identity` | 対象・IOwnersApi; 入力あり: `PreviewOwnerIdentityRecoveryRequest` / 戻りあり: `PreviewOwnerIdentityRecoveryResponse` | W1-W4; Owners利用者群 | `PreviewOwnerIdentityRecoveryEndpoint.cs` |
| Owners | POST | `/api/admin/repairs/owner-identity/execute` | 対象・IOwnersApi; 入力あり: `ExecuteOwnerIdentityRecoveryRequest` / 戻りあり: `ExecuteOwnerIdentityRecoveryResponse` | W1-W4; Owners利用者群 | `ExecuteOwnerIdentityRecoveryEndpoint.cs` |
| Owners | GET | `/api/owners` | 対象・IOwnersApi; 入力あり: `SearchOwnersRequest` / 戻りあり: `SearchOwnersResponse` | W1-W4; Owners利用者群 | `SearchOwnersEndpoint.cs` |
| Owners | GET | `/api/owners/{ownerId}` | 対象・IOwnersApi; 入力あり: `GetOwnerRequest` / 戻りあり: `GetOwnerResponse` | W1-W4; Owners利用者群 | `GetOwnerEndpoint.cs` |
| Owners | PUT | `/api/owners/{ownerId}` | 対象・IOwnersApi; 入力あり: `UpdateOwnerRequest` / 戻りあり: `UpdateOwnerResponse` | W1-W4; Owners利用者群 | `UpdateOwnerEndpoint.cs` |
| Owners | POST | `/api/owners/{ownerId}/merge` | 対象・IOwnersApi; 入力あり: `MergeOwnerRequest` / 戻りあり: `MergeOwnerResponse` | W1-W4; Owners利用者群 | `MergeOwnerEndpoint.cs` |
| Predictions | GET | `/api/predictions` | 対象・IPredictionsApi; 入力あり: `SearchPredictionTicketsRequest` / 戻りあり: `SearchPredictionTicketsResponse` | W1-W4; Predictions利用者群 | `SearchPredictionTicketsEndpoint.cs` |
| Predictions | POST | `/api/predictions` | 対象・IPredictionsApi; 入力あり: `CreatePredictionTicketRequest` / 戻りあり: `CreatePredictionTicketResponse` | W1-W4; Predictions利用者群 | `CreatePredictionTicketEndpoint.cs` |
| Predictions | GET | `/api/predictions/{predictionTicketId}` | 対象・IPredictionsApi; 入力あり: `GetPredictionTicketRequest` / 戻りあり: `GetPredictionTicketResponse` | W1-W4; Predictions利用者群 | `GetPredictionTicketEndpoint.cs` |
| Predictions | PATCH | `/api/predictions/{predictionTicketId}` | 対象・IPredictionsApi; 入力あり: `CorrectPredictionMetadataRequest` / 戻りあり: `CorrectPredictionMetadataResponse` | W1-W4; Predictions利用者群 | `CorrectPredictionMetadataEndpoint.cs` |
| Predictions | POST | `/api/predictions/{predictionTicketId}/betting-suggestions` | 対象・IPredictionsApi; 入力あり: `AddBettingSuggestionRequest` / 戻りあり: `AddBettingSuggestionResponse` | W1-W4; Predictions利用者群 | `AddBettingSuggestionEndpoint.cs` |
| Predictions | POST | `/api/predictions/{predictionTicketId}/evaluate` | 対象・IPredictionsApi; 入力あり: `EvaluatePredictionTicketRequest` / 戻りあり: `EvaluatePredictionTicketResponse` | W1-W4; Predictions利用者群 | `EvaluatePredictionTicketEndpoint.cs` |
| Predictions | POST | `/api/predictions/{predictionTicketId}/finalize` | 対象・IPredictionsApi; 入力あり: `FinalizePredictionTicketRequest` / 戻りあり: `FinalizePredictionTicketResponse` | W1-W4; Predictions利用者群 | `FinalizePredictionTicketEndpoint.cs` |
| Predictions | POST | `/api/predictions/{predictionTicketId}/marks` | 対象・IPredictionsApi; 入力あり: `AddPredictionMarkRequest` / 戻りあり: `AddPredictionMarkResponse` | W1-W4; Predictions利用者群 | `AddPredictionMarkEndpoint.cs` |
| Predictions | POST | `/api/predictions/{predictionTicketId}/rationales` | 対象・IPredictionsApi; 入力あり: `AddPredictionRationaleRequest` / 戻りあり: `AddPredictionRationaleResponse` | W1-W4; Predictions利用者群 | `AddPredictionRationaleEndpoint.cs` |
| Predictions | POST | `/api/predictions/{predictionTicketId}/recalculate-evaluation` | 対象・IPredictionsApi; 入力あり: `RecalculatePredictionEvaluationRequest` / 戻りあり: `RecalculatePredictionEvaluationResponse` | W1-W4; Predictions利用者群 | `RecalculatePredictionEvaluationEndpoint.cs` |
| Predictions | POST | `/api/predictions/{predictionTicketId}/withdraw` | 対象・IPredictionsApi; 入力あり: `WithdrawPredictionTicketRequest` / 戻りあり: `WithdrawPredictionTicketResponse` | W1-W4; Predictions利用者群 | `WithdrawPredictionTicketEndpoint.cs` |
| Memos | POST | `/api/memos` | 対象・IMemosApi; 入力あり: `CreateMemoRequest` / 戻りあり: `CreateMemoResponse` | W1-W4; Memos利用者群 | `CreateMemoEndpoint.cs` |
| Memos | DELETE | `/api/memos/{memoId}` | 対象・IMemosApi; 入力あり: `DeleteMemoRequest` / 戻りあり: `DeleteMemoResponse` | W1-W4; Memos利用者群 | `DeleteMemoEndpoint.cs` |
| Memos | PUT | `/api/memos/{memoId}` | 対象・IMemosApi; 入力あり: `UpdateMemoRequest` / 戻りあり: `UpdateMemoResponse` | W1-W4; Memos利用者群 | `UpdateMemoEndpoint.cs` |
| Memos | PUT | `/api/memos/{memoId}/subjects` | 対象・IMemosApi; 入力あり: `ChangeMemoSubjectsRequest` / 戻りあり: `ChangeMemoSubjectsResponse` | W1-W4; Memos利用者群 | `ChangeMemoSubjectsEndpoint.cs` |
| Memos | GET | `/api/memos/by-subject/{subjectType}/{subjectId}` | 対象・IMemosApi; 入力あり: `GetMemosBySubjectRequest` / 戻りあり: `GetMemosBySubjectResponse` | W1-W4; Memos利用者群 | `GetMemosBySubjectEndpoint.cs` |
| MachineLearning | POST | `/api/ml/train` | 対象・IMachineLearningApi; 入力あり: `TrainMlModelRequest` / 戻りあり: `TrainMlModelResponse` | W1-W4; MachineLearning利用者群 | `TrainMlModelEndpoint.cs` |
| MachineLearning | GET | `/api/races/{raceId}/ml-prediction` | 対象・IMachineLearningApi; 入力あり: `GetMlPredictionRequest` / 戻りあり: `GetMlPredictionResponse` | W1-W4; MachineLearning利用者群 | `GetMlPredictionEndpoint.cs` |
| Collection | GET | `/api/v2/admin/collection/backfill-batches` | 対象・ICollectionApi; 入力あり: `ListBackfillBatchesRequest` / 戻りあり: `ListBackfillBatchesResponse` | W1-W4; Collection利用者群 | `ListBackfillBatchesEndpoint.cs` |
| Collection | POST | `/api/v2/admin/collection/backfill-batches` | 対象・ICollectionApi; 入力あり: `CreateBackfillBatchRequest` / 戻りあり: `CreateBackfillBatchResponse` | W1-W4; Collection利用者群 | `CreateBackfillBatchEndpoint.cs` |
| Collection | GET | `/api/v2/admin/collection/backfill-batches/{id}` | 対象・ICollectionApi; 入力あり: `GetBackfillBatchRequest` / 戻りあり: `GetBackfillBatchResponse` | W1-W4; Collection利用者群 | `GetBackfillBatchEndpoint.cs` |
| Collection | POST | `/api/v2/admin/collection/backfill-batches/{id}/recovery-batches` | 対象・ICollectionApi; 入力あり: `RecoverBackfillHolesRequest` / 戻りあり: `RecoverBackfillHolesResponse` | W1-W4; Collection利用者群 | `RecoverBackfillHolesEndpoint.cs` |
| Collection | POST | `/api/v2/admin/collection/definitions/{definition}/revisions` | 対象・ICollectionApi; 入力あり: `CreateCollectionRevisionRequest` / 戻りあり: `CreateCollectionRevisionResponse` | W1-W4; Collection利用者群 | `CreateCollectionRevisionEndpoint.cs` |
| Collection | GET | `/api/v2/admin/collection/execution-batches/{id:guid}` | 対象・ICollectionApi; 入力あり: `GetExecutionBatchRequest` / 戻りあり: `GetExecutionBatchResponse` | W1-W4; Collection利用者群 | `GetExecutionBatchEndpoint.cs` |
| Collection | GET | `/api/v2/admin/collection/failure-notification-groups` | 対象・ICollectionApi; 入力あり: `ListFailureNotificationGroupsRequest` / 戻りあり: `ListFailureNotificationGroupsResponse` | W1-W4; Collection利用者群 | `ListFailureNotificationGroupsEndpoint.cs` |
| Collection | GET | `/api/v2/admin/collection/failure-notification-groups/{groupKey}` | 対象・ICollectionApi; 入力あり: `GetFailureNotificationGroupRequest` / 戻りあり: `GetFailureNotificationGroupResponse` | W1-W4; Collection利用者群 | `GetFailureNotificationGroupEndpoint.cs` |
| Collection | GET | `/api/v2/admin/collection/failure-notifications` | 対象・ICollectionApi; 入力あり: `ListFailureNotificationsRequest` / 戻りあり: `ListFailureNotificationsResponse` | W1-W4; Collection利用者群 | `ListFailureNotificationsEndpoint.cs` |
| Collection | POST | `/api/v2/admin/collection/known-recovery-batches` | 対象・ICollectionApi; 入力あり: `CreateKnownRecoveryBatchRequest` / 戻りあり: `CreateKnownRecoveryBatchResponse` | W1-W4; Collection利用者群 | `CreateKnownRecoveryBatchEndpoint.cs` |
| Collection | GET | `/api/v2/admin/collection/migration-previews/owner-identity` | 対象・ICollectionApi; 入力あり: `GetOwnerIdentityMigrationPreviewRequest` / 戻りあり: `GetOwnerIdentityMigrationPreviewResponse` | W1-W4; Collection利用者群 | `GetOwnerIdentityMigrationPreviewEndpoint.cs` |
| Collection | POST | `/api/v2/admin/collection/migration-previews/race-detail` | 対象・ICollectionApi; 入力あり: `PreviewRaceDetailMigrationRequest` / 戻りあり: `PreviewRaceDetailMigrationResponse` | W1-W4; Collection利用者群 | `PreviewRaceDetailMigrationEndpoint.cs` |
| Collection | POST | `/api/v2/admin/collection/migration-previews/race-entry-owner-repair` | 対象・ICollectionApi; 入力あり: `PreviewRaceEntryOwnerMigrationRequest` / 戻りあり: `PreviewRaceEntryOwnerMigrationResponse` | W1-W4; Collection利用者群 | `PreviewRaceEntryOwnerMigrationEndpoint.cs` |
| Collection | POST | `/api/v2/admin/collection/migrations/race-detail` | 対象・ICollectionApi; 入力あり: `ApplyRaceDetailMigrationRequest` / 戻りあり: `ApplyRaceDetailMigrationResponse` | W1-W4; Collection利用者群 | `ApplyRaceDetailMigrationEndpoint.cs` |
| Collection | GET | `/api/v2/admin/collection/migrations/race-entry-owner-repair` | 対象・ICollectionApi; 入力あり: `GetRaceEntryOwnerMigrationRequest` / 戻りあり: `GetRaceEntryOwnerMigrationResponse` | W1-W4; Collection利用者群 | `GetRaceEntryOwnerMigrationEndpoint.cs` |
| Collection | POST | `/api/v2/admin/collection/migrations/race-entry-owner-repair` | 対象・ICollectionApi; 入力あり: `ApplyRaceEntryOwnerMigrationRequest` / 戻りあり: `ApplyRaceEntryOwnerMigrationResponse` | W1-W4; Collection利用者群 | `ApplyRaceEntryOwnerMigrationEndpoint.cs` |
| Collection | GET | `/api/v2/admin/collection/operations/dashboard` | 対象・ICollectionApi; 入力あり: `GetCollectionDashboardRequest` / 戻りあり: `GetCollectionDashboardResponse` | W1-W4; Collection利用者群 | `GetCollectionDashboardEndpoint.cs` |
| Collection | GET | `/api/v2/admin/collection/operations/monitoring-findings` | 対象・ICollectionApi; 入力あり: `GetCollectionMonitoringFindingsRequest` / 戻りあり: `GetCollectionMonitoringFindingsResponse` | W1-W4; Collection利用者群 | `GetCollectionMonitoringFindingsEndpoint.cs` |
| Collection | GET | `/api/v2/admin/collection/operations/progress` | 対象・ICollectionApi; 入力あり: `GetCollectionProgressRequest` / 戻りあり: `GetCollectionProgressResponse` | W1-W4; Collection利用者群 | `GetCollectionProgressEndpoint.cs` |
| Collection | GET | `/api/v2/admin/collection/operations/task-view-counts` | 対象・ICollectionApi; 入力あり: `GetTaskViewCountsRequest` / 戻りあり: `GetTaskViewCountsResponse` | W1-W4; Collection利用者群 | `GetTaskViewCountsEndpoint.cs` |
| Collection | PUT | `/api/v2/admin/collection/pipeline` | 対象・ICollectionApi; 入力あり: `SetCollectionPipelineRequest` / 戻りあり: `SetCollectionPipelineResponse` | W1-W4; Collection利用者群 | `SetCollectionPipelineEndpoint.cs` |
| Collection | GET | `/api/v2/admin/collection/pipeline-state` | 対象・ICollectionApi; 入力あり: `GetCollectionPipelineRequest` / 戻りあり: `GetCollectionPipelineResponse` | W1-W4; Collection利用者群 | `GetCollectionPipelineEndpoint.cs` |
| Collection | POST | `/api/v2/admin/collection/race-entry-owner-repair-batches` | 対象・ICollectionApi; 入力あり: `CreateRaceEntryOwnerRepairBatchRequest` / 戻りあり: `CreateRaceEntryOwnerRepairBatchResponse` | W1-W4; Collection利用者群 | `CreateRaceEntryOwnerRepairBatchEndpoint.cs` |
| Collection | GET | `/api/v2/admin/collection/race-entry-owner-repair-candidates` | 対象・ICollectionApi; 入力あり: `ListRaceEntryOwnerRepairCandidatesRequest` / 戻りあり: `ListRaceEntryOwnerRepairCandidatesResponse` | W1-W4; Collection利用者群 | `ListRaceEntryOwnerRepairCandidatesEndpoint.cs` |
| Collection | GET | `/api/v2/admin/collection/races/{raceId}/readiness` | 対象・ICollectionApi; 入力あり: `GetRaceCollectionReadinessRequest` / 戻りあり: `GetRaceCollectionReadinessResponse` | W1-W4; Collection利用者群 | `GetRaceCollectionReadinessEndpoint.cs` |
| Collection | GET | `/api/v2/admin/collection/recollection-batches` | 対象・ICollectionApi; 入力あり: `GetRevisionRecollectionProgressRequest` / 戻りあり: `GetRevisionRecollectionProgressResponse` | W1-W4; Collection利用者群 | `GetRevisionRecollectionProgressEndpoint.cs` |
| Collection | POST | `/api/v2/admin/collection/recollection-batches` | 対象・ICollectionApi; 入力あり: `CreateRecollectionBatchRequest` / 戻りあり: `CreateRecollectionBatchResponse` | W1-W4; Collection利用者群 | `CreateRecollectionBatchEndpoint.cs` |
| Collection | POST | `/api/v2/admin/collection/recollection-previews` | 対象・ICollectionApi; 入力あり: `PreviewRacePeriodRecollectionRequest` / 戻りあり: `PreviewRacePeriodRecollectionResponse` | W1-W4; Collection利用者群 | `PreviewRacePeriodRecollectionEndpoint.cs` |
| Collection | POST | `/api/v2/admin/collection/recovery-batches` | 対象・ICollectionApi; 入力あり: `CreateCollectionRecoveryBatchRequest` / 戻りあり: `CreateCollectionRecoveryBatchResponse` | W1-W4; Collection利用者群 | `CreateCollectionRecoveryBatchEndpoint.cs` |
| Collection | GET | `/api/v2/admin/collection/recovery-previews/known` | 対象・ICollectionApi; 入力あり: `GetKnownRecoveryPreviewRequest` / 戻りあり: `GetKnownRecoveryPreviewResponse` | W1-W4; Collection利用者群 | `GetKnownRecoveryPreviewEndpoint.cs` |
| Collection | GET | `/api/v2/admin/collection/resources/{type}/{provider}/{resourceId}/definitions/{definition}` | 対象・ICollectionApi; 入力あり: `GetCollectionResourceDetailRequest` / 戻りあり: `GetCollectionResourceDetailResponse` | W1-W4; Collection利用者群 | `GetCollectionResourceDetailEndpoint.cs` |
| Collection | GET | `/api/v2/admin/collection/resources/{type}/{provider}/{resourceId}/definitions/{definition}/state` | 対象・ICollectionApi; 入力あり: `GetCollectionResourceStateRequest` / 戻りあり: `GetCollectionResourceStateResponse` | W1-W4; Collection利用者群 | `GetCollectionResourceStateEndpoint.cs` |
| Collection | POST | `/api/v2/admin/collection/revision-impact-previews` | 対象・ICollectionApi; 入力あり: `PreviewRevisionImpactRequest` / 戻りあり: `PreviewRevisionImpactResponse` | W1-W4; Collection利用者群 | `PreviewRevisionImpactEndpoint.cs` |
| Collection | GET | `/api/v2/admin/collection/states` | 対象・ICollectionApi; 入力あり: `SearchCollectionStatesRequest` / 戻りあり: `SearchCollectionStatesResponse` | W1-W4; Collection利用者群 | `SearchCollectionStatesEndpoint.cs` |
| Collection | POST | `/api/v2/admin/collection/task-batch-previews` | 対象・ICollectionApi; 入力あり: `PreviewCollectionTaskBatchRequest` / 戻りあり: `PreviewCollectionTaskBatchResponse` | W1-W4; Collection利用者群 | `PreviewCollectionTaskBatchEndpoint.cs` |
| Collection | POST | `/api/v2/admin/collection/task-batches` | 対象・ICollectionApi; 入力あり: `CreateCollectionTaskBatchRequest` / 戻りあり: `CreateCollectionTaskBatchResponse` | W1-W4; Collection利用者群 | `CreateCollectionTaskBatchEndpoint.cs` |
| Collection | GET | `/api/v2/admin/collection/tasks` | 対象・ICollectionApi; 入力あり: `ListCollectionTasksRequest` / 戻りあり: `ListCollectionTasksResponse` | W1-W4; Collection利用者群 | `ListCollectionTasksEndpoint.cs` |
| Collection | POST | `/api/v2/admin/collection/tasks` | 対象・ICollectionApi; 入力あり: `CreateCollectionTaskRequest` / 戻りあり: `CreateCollectionTaskResponse` | W1-W4; Collection利用者群 | `CreateCollectionTaskEndpoint.cs` |
| Collection | PATCH | `/api/v2/admin/collection/tasks/{taskId:guid}` | 対象・ICollectionApi; 入力あり: `CancelCollectionTaskRequest` / 戻りあり: `CancelCollectionTaskResponse` | W1-W4; Collection利用者群 | `CancelCollectionTaskEndpoint.cs` |
| Collection | PATCH | `/api/v2/internal/collection/execution-batches/{id:guid}` | 対象・ICollectionApi; 入力あり: `TransitionCollectionExecutionRequest` / 戻りあり: `TransitionCollectionExecutionResponse` | W1-W4; Collection利用者群 | `TransitionCollectionExecutionEndpoint.cs` |
| Collection | POST | `/api/v2/internal/collection/execution-leases` | 対象・ICollectionApi; 入力あり: `AcquireNextExecutionRequest` / 戻りあり: `AcquireNextExecutionResponse` | W1-W4; Collection利用者群 | `AcquireNextExecutionEndpoint.cs` |
| Collection | POST | `/api/v2/internal/collection/tasks/{id:guid}/attempts` | 対象・ICollectionApi; 入力あり: `CompleteCollectionTaskAttemptRequest` / 戻りあり: `CompleteCollectionTaskAttemptResponse` | W1-W4; Collection利用者群 | `CompleteCollectionTaskAttemptEndpoint.cs` |
| Collection | POST | `/api/v2/internal/collection/tasks/{id:guid}/leases` | 対象・ICollectionApi; 入力あり: `AcquireCollectionTaskRequest` / 戻りあり: `AcquireCollectionTaskResponse` | W1-W4; Collection利用者群 | `AcquireCollectionTaskEndpoint.cs` |
| Collection | PATCH | `/api/v2/internal/collection/tasks/{id:guid}/leases/{leaseId}` | 対象・ICollectionApi; 入力あり: `HeartbeatCollectionTaskLeaseRequest` / 戻りあり: `HeartbeatCollectionTaskLeaseResponse` | W1-W4; Collection利用者群 | `HeartbeatCollectionTaskLeaseEndpoint.cs` |
| PredictionScheduling | POST | `/api/v2/internal/prediction-candidate-leases` | 対象・IPredictionSchedulingApi; 入力あり: `AcquirePredictionCandidateLeasesRequest` / 戻りあり: `AcquirePredictionCandidateLeasesResponse` | W1-W4; PredictionScheduling利用者群 | `AcquirePredictionCandidateLeasesEndpoint.cs` |
| PredictionScheduling | POST | `/api/v2/internal/prediction-candidates` | 対象・IPredictionSchedulingApi; 入力あり: `EnqueuePredictionCandidatesRequest` / 戻りあり: `EnqueuePredictionCandidatesResponse` | W1-W4; PredictionScheduling利用者群 | `EnqueuePredictionCandidatesEndpoint.cs` |
| PredictionScheduling | PATCH | `/api/v2/internal/prediction-candidates/{raceId}` | 対象・IPredictionSchedulingApi; 入力あり: `TransitionPredictionCandidateRequest` / 戻りあり: `TransitionPredictionCandidateResponse` | W1-W4; PredictionScheduling利用者群 | `TransitionPredictionCandidateEndpoint.cs` |
| Repairs | GET | `/api/admin/repairs/20260913-jra-horse-identity` | 対象・IRepairsApi; 入力あり: `GetHorseIdentityRepairRequest` / 戻りあり: `GetHorseIdentityRepairResponse` | W1-W4; Repairs利用者群 | `GetHorseIdentityRepairEndpoint.cs` |
| Repairs | POST | `/api/admin/repairs/20260913-jra-horse-identity/apply` | 対象・IRepairsApi; 入力あり: `ApplyHorseIdentityRepairRequest` / 戻りあり: `ApplyHorseIdentityRepairResponse` | W1-W4; Repairs利用者群 | `ApplyHorseIdentityRepairEndpoint.cs` |
| Repairs | GET | `/api/admin/repairs/subject-identification` | 対象・IRepairsApi; 入力あり: `GetSubjectIdentificationRepairRequest` / 戻りあり: `GetSubjectIdentificationRepairResponse` | W1-W4; Repairs利用者群 | `GetSubjectIdentificationRepairEndpoint.cs` |
| Repairs | POST | `/api/admin/repairs/subject-identification/dismiss` | 対象・IRepairsApi; 入力あり: `DismissSubjectIdentificationFailuresRequest` / 戻りあり: `DismissSubjectIdentificationFailuresResponse` | W1-W4; Repairs利用者群 | `DismissSubjectIdentificationFailuresEndpoint.cs` |
| Repairs | POST | `/api/admin/repairs/subject-identification/execute` | 対象・IRepairsApi; 入力あり: `ExecuteSubjectIdentificationRepairRequest` / 戻りあり: `ExecuteSubjectIdentificationRepairResponse` | W1-W4; Repairs利用者群 | `ExecuteSubjectIdentificationRepairEndpoint.cs` |
| Repairs | GET | `/api/admin/repairs/subject-name-normalization` | 対象・IRepairsApi; 入力あり: `GetSubjectNameNormalizationRequest` / 戻りあり: `GetSubjectNameNormalizationResponse` | W1-W4; Repairs利用者群 | `GetSubjectNameNormalizationEndpoint.cs` |
| Repairs | POST | `/api/admin/repairs/subject-name-normalization/apply` | 対象・IRepairsApi; 入力あり: `ApplySubjectNameNormalizationRequest` / 戻りあり: `ApplySubjectNameNormalizationResponse` | W1-W4; Repairs利用者群 | `ApplySubjectNameNormalizationEndpoint.cs` |
| Identity | POST | `/api/identity/horse` | 対象・IIdentityApi; 入力あり: `ResolveHorseIdentityRequest` / 戻りあり: `ResolveHorseIdentityResponse` | W1-W4; Identity利用者群 | `ResolveHorseIdentityEndpoint.cs` |
| Identity | POST | `/api/identity/race` | 対象・IIdentityApi; 入力あり: `ResolveRaceIdentityRequest` / 戻りあり: `ResolveRaceIdentityResponse` | W1-W4; Identity利用者群 | `ResolveRaceIdentityEndpoint.cs` |
| Subjects | PUT | `/api/v2/admin/subjects/{kind}/{subjectId}/profile` | 対象・ISubjectsApi; 入力あり: `PutSubjectProfileRequest` / 戻りあり: `PutSubjectProfileResponse` | W1-W4; Subjects利用者群 | `PutSubjectProfileEndpoint.cs` |
| Subjects | GET | `/api/v2/admin/subjects/{kind}/{subjectId}/profiles/current` | 対象・ISubjectsApi; 入力あり: `GetSubjectProfileRequest` / 戻りあり: `GetSubjectProfileResponse` | W1-W4; Subjects利用者群 | `GetSubjectProfileEndpoint.cs` |
| Health | GET | `/health` | 対象外・変更なし | 既存probe/JSONを維持 | `GetHealthEndpoint.cs` |

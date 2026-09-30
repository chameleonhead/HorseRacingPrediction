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
| Code | In progress | T2のsemantic rename 53件、resource namespace/folder化、全型1ファイル化、馬・騎手・調教師profileとaliasの自然統合を完了。memo/weather統合、後続Request/Response、Refit、Factory、未導入DI拡張は未完了 |
| Verification | In progress | namespace/split checkpointはRelease build、format verify、全非Externalテスト1511 passed/1 skipped。profile/alias統合はRelease build、Contracts 46件とAPI profile/consumer 39件、format verifyを通過。memo/weather統合と新HTTP契約検証は未完了 |
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
2. 全135操作で入力/戻りデータの有無を確認し、両方ある場合に同じ語幹の専用Request/Responseを対で用意。入力なしはRequest不要、戻りデータなしはResponse不要。空型/空オブジェクトは禁止。業務データの塊はDtoとし、Responseは`RaceDto Race`等のプロパティで持つ。直下項目は操作メタデータ等に限り都度理由を記録。Requestもデータの塊は入力専用Dtoへ分離。GET/DELETEのbody追加はしない。同じ意味・データ・制約を持つ型は自然な単位で統合する。異なる形状/意味は無理に統合せず旧C#名の互換ラッパーは残さない。
3. 内部モデルをそのまま共有ライブラリへ移さず、wire DTOとマッピングを作る。永続型・実行型の名前/配置を維持し、ApiClient/Contractsからサーバープロジェクトへの参照を禁止する。
4. 単一`IApiClientFactory.Create<TApi>()`で登録済みAPIだけを生成する。`AddHorseRacingApiClient`で共通HttpClient設定を用意し、IHttpClientBuilderでhandlerを追加可能にする。APIキーは任意、絶対BaseAddress/Timeout/header設定を検証。秘密を記録しない。自動retryなし。
5. 公開操作は入力がある場合だけ専用Requestを受ける。戻りデータありはApiResponse<専用Response>、なしはIApiResponse。空Request/Response/JSONは作らず、既存の204を含む成功status/Location・エラー・認証は維持。DTOラップのJSON構造変更を許容し、業務値/null/enum/日時/既定値は維持する。
6. 公開済みRoslynator CLIの意味解析Renameで型名/参照を先に変更する。manifest、dry-run、衝突確認を残し、namespace配置、統合、alias/Razor・文字列参照などの規約例外はその後個別修正して検索とbuildで検証する。自作移行ツールは開発しない（再開時の利用者指示）。
7. Refit 16.3.0を維持。既存抽象サービスとHTTP実装を維持し、導入は別途とする。

2026-09-30追加指示（最新の訂正を反映）: 入力と戻りデータが両方ある操作だけRequest/Responseを対とする。片側がなければ対応する型は不要で空オブジェクトは禁止。ResponseにRaceの項目を直接置かずRaceDtoを持たせる。データの塊は必ずDto。直下項目の可否は都度判断する。これに伴い、当初の「HTTP JSON形状不変」制約を撤回する。OpenAPI変更許容の明示回答に整合する。最新訂正を含め2026-09-30の「不明点はありますか？なければ実装をお願いします。」により設計全体を承認。その後の「統合が自然な場合は統合してください」に従い、同じ意味/データ/制約の型は統合を認め、対応表・既存利用者検証へ含める。利用者による明示変更として承認範囲へ反映。

## Hypothesis ledger

| Claim | Fact boundary / evidence | Falsification / result | Disposition |
| --- | --- | --- | --- |
| Refitを追加インストールする必要がある | ApiClient.csprojは既に両Refit package 16.3.0を参照 | csprojとNuGet同梱READMEを確認し否定 | version維持 |
| 全DTOはContractsだけに存在する | CollectionController/CollectionPlatformContracts.csはApi所属、CollectionOperations型を参照 | endpointと型宣言を確認し否定 | wire DTOを切り出す |
| Summary等の型は名前だけ統一して結合できる | PredictionTicketSummaryReadModelはMarks、SummaryResponseはstatus/count等で形状が異なる | 宣言比較で否定 | データを意味名のDtoとし操作Responseから参照する |
| namespace変更はHTTP仕様に一切影響しない | Program.csはFullNameをOpenAPI schema IDに使用 | CustomSchemaIdsを確認し否定 | 当初はJSON維持案。追加要件によりDTOラップも変更対象へ修正 |
| CodeGraphで参照解析可能 | 調査当初CLI未導入。利用者追加指示で1.6.1を導入 | 初回index/status/explore成功 | 現在CLI使用可能、各workerへ使用指示済み |

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
| AC2 | 全135操作の入力/戻りの有無、必要なRequest/Response、API別namespace、業務データDto分離がmanifestと一致。空型・空オブジェクトを作らない。直下項目は理由を記録しエンティティ項目を展開しない。Commonに共通型。自然な同義DTO統合も対応表へ記録し、新旧JSONパスを対応付け、値/null/enum/日時/既定値を維持し全利用者がbuild可能 | T2,T3,T5 | 旧名検索、契約fixture比較、依存参照検査、全build | Not started |
| AC3 | 一つのFactoryから全登録APIを生成でき、未登録型と不正設定を拒否。DIからの解決、共通header/serializer/handler/cancellation/error/入力なし・本文なしが動作 | T4,T5,T7 | Factory/DI/HTTPテスト（404/409/204、Request不要の呼出、キャンセル、並行別API呼出） | Not started |
| AC4 | 拡張と使用例は提供するが、既存ホストに新DI登録を追加せず既存HTTP実装を置換しない | T2,T3,T4,T5,T6 | 起動処理/既存call path差分、登録検索、既存回帰 | Not started |
| AC5 | ツールのdry-run・衝突検出・実適用を検証し、新clientの実HTTP往復と既存回帰/formatを通過。承認したDTOラップ以外に意図しない契約差分なし | T2,T3,T4,T5 | Roslyn fixture、TestServer経由の読み書き/認証失敗、CI相当build/test/format | Not started |
| AC6 | 移行manifest、使用例、検証結果、全route台帳とこの記録が最終実装と一致 | T1,T2,T5,T6 | 文書validator、台帳照合、Lead最終監査 | Not started |

## Documentation updates

- `docs/28-api-client-design.md`: 新設。クライアント設計、命名、Factory/DI、移行ツール、未導入境界の正規文書。提案状態を明記。
- `docs/00-system-architecture.md`: Contracts/ApiClientの古い説明を実態へ訂正し、新設計へのリンクを追加。
- README、収集基盤/予測設計も影響調査対象。稼働経路・運用・永続化は変わらないため、それらの設計を先行して書き換えない。

## Task plan

全プロダクションコード編集タスクを直列化し、同じ参照先やformatterで競合しない。各workerは一人がwrite ownerとなり、他の作業を取り消さない。public contract判断はLeadに戻す。Leadは設計と最終受入のみを保持（不可分なpublic contract/統合判断）。各workerが最小testとhandoff前regressionを実行し、Leadが独立証拠で採否を決める。

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State | Routing | Audit | Result metrics |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | 全API棚卸し（AC1/AC6） | inventory explorer | requested gpt-6-luna/high | - | route-inventory.md; http-contract-inventory.md/.json | endpoint宣言と登録/route検索照合、handler input/result対照 | 136 routes (135 business); request 116/19; response data 94/41; corrected DI/delegate cases cross-checked; Lead accepted inventory as T3 baseline (counts do not prove wrappers) | Verified | 境界の明確な調査 | read-only research | unavailable; retries 0; corrections 1; reviews 1 |
| T2 | 既存ツールで共有型を改名し、規約例外を個別修正（AC2/4/5/6） | rename_tool_inventory / contract-migration worker | requested gpt-6-luna/high | 設計承認 | src; tools/HorseRacingPrediction.CollectionInitializer; tests; docs/28-api-client-design.md; docs/changes/20260930_refit-api-clients/README.md; docs/changes/20260930_refit-api-clients/contract-migration.json; docs/changes/20260930_refit-api-clients/agent-audits/T2-A2.json; docs/changes/20260930_refit-api-clients/agent-audits/T2-A3.json; docs/changes/20260930_refit-api-clients/agent-audits/T2-A4.json; docs/changes/20260930_refit-api-clients/agent-audits/history/T2-A1.json | CLI inventory/apply、Contracts tests、solution build、format | Roslynator semantic rename 53件を適用。142宣言をresource namespace/folderへ配置し、142個の型名一致ファイルを維持。馬・騎手・調教師DTOと共通alias DTOを自然統合し、profile契約用JSON往復試験とAPI既存利用者試験を追加。独立reviewで全profileフィールド、nullable SourceName、list order、internal model不変を確認。Release build、Contracts 46/46、API 39/39、format verifyを通過。memo/weather統合は継続 | In progress | 意味判断はLead、確定規則下の移行を直列実行 | T2-A2,T2-A3,T2-A4 | unavailable; retries 2; corrections 0; reviews 3 |
| T3 | 入力/戻りの有無に応じた操作別Request/Response、DTO、サーバー/既存利用者mapping（AC2/4/5） | wire-contract worker | gpt-6-luna/high予定 | T2 | Contracts、Api境界mapping、関連test、既存利用者のRequest組立/Response取出し | 新契約HTTP試験、旧→新データ保存性、既存利用者回帰 | 未着手 | Dependent | 内部実行型を維持し独立比較 | dispatch時作成 | unavailable; retries 0; corrections 0; reviews 0 |
| T4 | 全Refitインターフェース、Factory、DI（AC1/3/4/5） | api-client worker | gpt-6-luna/high予定 | T3 | ApiClient、新ApiClient.Tests、slnへのtest追加 | 全route送信、DI/Factory/error/cancel tests | 未着手 | Dependent | 凍結済みHTTP契約を実装 | dispatch時作成 | unavailable; retries 0; corrections 0; reviews 0 |
| T5 | 独立HTTP往復/全回帰と局所修正（AC1-6） | verification worker | gpt-6-luna/high予定 | T4 | tests、直列の限定修正、検証記録 | TestServer、全solution test/build/format | 未着手 | Dependent | Leadが契約逸脱を判定し局所修正はworkerへ戻す | dispatch時作成 | unavailable; retries 0; corrections 0; reviews 0 |
| T6 | 正規文書/台帳の最終反映と受入（AC4/6） | Lead | Lead | T5 | docs | 差分、独立反証、validator、task/AC追跡 | 未着手 | Dependent | 最終受入判断 | none | unavailable; retries 0; corrections 0; reviews 0 |
| T7 | Refit16生成clientのpath/query/body/空応答検証（AC3） | refit-probe worker | requested gpt-6-luna/high | - | docs/changes/20260930_refit-api-clients/probes/refit | isolated probe run | Six groups passed; Lead accepted internal attributed transport; RF015 direct-Body route binding recorded as ruled-out probe pattern, not a production failure | Verified | Worker — independent verification; srcを変更しないためT2と並列可 | T7-A1 | unavailable; retries 0; corrections 0; reviews 1 |

observed model・token・費用はtoolで証明されない限り未確認。調査T1のrequested値から推測しない。実装workerのauditはdispatch時に作成し`scripts/audit_agent_execution.py`で検証する。永続routing変更は行わない。

## Review gates

- Design and task-split review: Lead。設計・AC対応・直列ownershipを確認。route台帳を独立ソース抽出と登録宣言に照合し136/136、重複0。対象135/対象外1の収支一致。
- Concern and agreement review: Lead。C1-C6を設計で処置。外部CLR/schema ID互換性・未導入境界を承認依頼に含める。2026-09-30の実装指示でC1-C6を含む最新設計を承認。
- Pre-implementation review: Lead / 2026-09-30。T2をRunnable、T3-T6をDependentとして直列実行。T2はContracts/全参照の機械的移行とtools/ContractMigration、manifestのみ。新HTTP契約はT3で扱い、T2ではJSON/永続型不変。移行tool fixture→Contracts tests→solution Release buildをworker実行。反例は同名型/alias/Razor/enum/異形Summary。判断不明はLeadへ戻し、他タスクの書込は開始しない。現在はtool Release buildとself-testが成功し、独立dry-runでVF001が発生したため局所修正中。
- Checkpoint review: DTO移行、wire mapping、client/DI、統合検証の検証済み単位で実施・コミット。
- Final review: 未実施。全AC/タスクVerified、禁止runtime導入0、新契約適合/既存利用者追従、文書同期を確認してImplementedにする。

## Verification record

T1のroute・HTTP shape inventoryを独立確認しLeadが受入。T7は6 probe groupsが成功しLeadがaccept。直接`[Body]` Requestからnested route placeholderを結ぶ形はRF015/`ArgumentException`となったため、計測で不成立と確認し、public default interface methodからinternal attributed transportを呼ぶ形を採用候補として検証した。このRF015はisolated probe patternの結果であり、本番変更の失敗ではない。移行ツール単体のRelease buildは0警告/0エラー、自己テストは成功した。独立したsolution dry-runは`QualifiedNameSyntax`を`SimpleNameSyntax`へcastする`InvalidCastException`で終了（VF001、未解決）。T2は構文rewriterのguardとgeneric `PagedResponse<T>` target表記を修正し、静的差分を確認したが、Leadの指示により再実行を保留中。本番型移行は適用されていない。型manifestはContractsの148 top-level宣言と148件で一致し、重複targetなし。変更記録validator issues=0、git diff --check成功。認証middlewareを独立確認しGETも保護対象と設計へ反映。調査当初はCodeGraph CLI/MCPが使用不能だったが、追加指示で[CodeGraph導入](../20260930_codegraph-setup/README.md)を完了。以後はCLI exploreを優先し、coherent edit後にsyncして確認する。Refit 16.3.0の同梱README/XMLと公式ドキュメントを参照。実装後のCI主要gate:

### Material verification failures

| ID | Gate / evidence | State | Closure evidence |
| --- | --- | --- | --- |
| VF001 | The abandoned `tools/ContractMigration` attempt is retained only in `agent-audits/history/T2-A1.json`. During resumed work, published Roslynator 1.0.0 `rename-symbol --dry-run` repeatedly terminated in `MSBuildWorkspace.TryApplyChanges` with `NullReferenceException` (full solution, filtered/project, VS MSBuild, and isolated fixture); it made no source writes. | Closed with documented limitation | No custom tool was repaired or added. Actual-mode was verified on an isolated fixture, then applied to 37 pre-inventoried Contracts symbols with exact assembly predicate and `--on-error abort`; target collisions were zero. Contracts tests, solution build, format, source review passed. `--dry-run` remains a known Roslynator environment defect. |

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

### 2026-09-30 restart: published rename tool first

利用者は「まず項目の名前変更をツールを使って実行し、最後規約に合わないものを個別に修正する」方式を明示した。Decision 6とT2の実施方式をこの指示で更新する。自作移行ツールの開発は再開せず、公開済みRoslynator CLI `rename-symbol`による型のsemantic renameを先行し、namespace配置、同義型統合、Razor等の未追従箇所を後から個別修正する。Roslynator 1.0.0の`--dry-run`は`MSBuildWorkspace.TryApplyChanges`内でNullReferenceExceptionとなったため、成功扱いしない。実適用前にContracts assemblyの対象宣言数・target衝突を独立棚卸しし、隔離fixtureでactual-modeを検証後、実適用と差分/buildで確認した。37件の意味的改名、Razor 17ファイル/67参照補正、Contracts tests 43/43、Release solution build、format verifyを完了。namespace配置、全Contracts型の1型1ファイル化、同義型統合は継続中。HTTPの新Request/Response構造は引き続きT3で扱う。受け入れ基準と未導入境界は維持する。

再開時点 `51d4f310` はclean。記録にある `tools/ContractMigration`、`contract-migration.json`、Refit probe sourceは現在のcheckoutに存在しない。以前の検証記述は履歴であり、現在の実行可能な成果物の証拠として扱わない。VF001の自作ツールは採用を撤回し、代替CLIでのdry-run/apply/buildを新しいclosure evidenceとして残す。

旧attempt `agent-audits/history/T2-A1.json` は失敗した方式の履歴として内容を変えず保持し、現在のT2の成果へ合算しない。read-onlyの既存tool調査と型分類調査はrequested Luna/high、observed model/usageは未確認。再開時の文書validator `scripts/validate_change_records.py` はこのcheckoutに存在せず、使用可能な `scripts/audit_agent_execution.py` と記録の整合確認を行う。

Pre-implementation review (restart): Leadは契約判断・統合受入を保持し、機械的実行はLuna/highへ直列委譲する。改名段階では業務値、JSON property名、内部/永続モデル、runtime登録を変更しない。同名のDomain型とContracts型は区別する。公開済みCLIは作業用ディレクトリへ導入し、リポジトリへ新規tool projectを追加しない。例外の修正も同じwrite ownerが実施し、dry-runと適用の後に関連test、solution build、CI format、旧名検索で検証する。

第1 checkpointではRoslynator actual-modeで37 Contracts型のsemantic renameとRazor補正を完了。次は資源別namespace化、全型1ファイル化、承認済み同義型統合と回帰検証。T2-T6と全ACは未完了。T7の過去検証結果は記録のみ存在し、T4で再現可能な実HTTP試験を実施する。再開時の意図的な未コミットは本記録の方式更新のみ。

第2 checkpointでは追加16型を含むsemantic rename 53件、148 Contracts宣言のresource namespaceと同名folderへの配置、1型1ファイル化を完了した。独立reviewはbaseline対比で型body ordered-token mismatch 0/148、XML documentation line 47/47、213 using-var declarationのscope/order preservationを確認した。初回のdirective cleanupがusing-varを誤って選択したため全213宣言を復元し、3箇所の逆順を個別修正した。Roslynatorのdry-run制約は既知のまま保持する。`dotnet build HorseRacingPrediction.sln -c Release --no-restore`成功、`dotnet format HorseRacingPrediction.sln --no-restore --verify-no-changes`成功、`dotnet test HorseRacingPrediction.sln --no-build -c Release --filter "TestCategory!=External"`は1511 passed/1 skipped。1回目のtest runではScraping 78件がPlaywright Chromium未導入で失敗したため、既存のPlaywright install scriptでChromiumを導入して再実行し全件成功した。T2は承認済み自然な型統合が残るためIn progress。

第3 checkpointではprofile responseをHorseDto/JockeyDto/TrainerDtoへ統合し、Horse/Jockey/Trainer aliasをnullable `AliasDto.SourceName`へ統合した。旧profile JSON fixtureを各新DTOへdeserialize/serializeし、horse pedigree/color、optional null、alias listとnull SourceNameを検証した。solution Release build、Contracts 46/46、関連API 39/39、format verifyが成功。独立reviewで同じfield/list順とApplication内alias modelの不変を確認。T2はmemo/weatherの承認済み統合が残るためIn progress。

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

### Frozen implementation clarifications (2026-09-30)

- CodeGraph source/impact確認により、HorseProfileResponseとHorseReadDto、JockeyProfileResponseとJockeyDto、TrainerProfileResponseとTrainerDtoは、それぞれ同じ業務データの別表現である。各資源のHorseDto/JockeyDto/TrainerDtoへ自然に統合する。プロパティ順やrecord/class差を理由に重複を残さず、既存値を維持してconstructor/initializerを追従する。
- HorseAliasEntry/JockeyAliasEntry/TrainerAliasEntryは同じ4項目。AliasResponseのSourceNameのみnon-null annotationだが、共通AliasDto.SourceNameはoptionalとして既存値をそのまま保持する。alias4型をCommon.AliasDtoへ統合する。型annotationの緩和はOpenAPI可読性優先・自然な統合という追加承認の範囲内。
- AcquireNextExecutionEndpointのみcamelCase文字列enumとnull省略の応答JSON設定を持つ。通常APIの数値enumと区別し、client readerは両形式へ対応しつつrequestの数値enumを変えない。既存の日時意味も維持する。
- OpenAPIのProduces/型metadataも新Response/Dtoと一致させる。本文なし200/201/202/204に空Response schemaや`{}`を追加しない。
- T7は独立したRefit16.3.0試験。Requestオブジェクトのpath/query分解、path値のquery重複防止、GET bodyなし、ISO日付、typed body/response、本文なしとerror/cancellationを実行してT4のテンプレートを確定する。第三者ドキュメントだけで成功判定しない。

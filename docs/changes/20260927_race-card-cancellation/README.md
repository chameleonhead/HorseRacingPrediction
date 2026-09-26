# 出馬表の取消表示に対応し、馬の識別を維持して収集を復旧する

- Status: Approved
- Change record schema: 2
- Owner: Main
- Created: 2026-09-27
- Updated: 2026-09-27

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | In progress | 2026-09-27利用者「お願いします」で提示済み設計を承認。契約凍結から実装開始 |
| Verification | In progress | 本番GET、公式HTML、実snapshotter→parserで取消例外を再現。修正後回帰は未実施 |
| Deployment/operation | Not started | GitHub経由の配備・対象失敗の復旧・進捗確認を本提案に含む。未承認の操作はしない |

## Context / incident

利用者提示の対象は `Race/JRA/20260927:Nakayama:1/race-detail`、失敗グループ `43C1C29257924632`。
2026-09-27 00:53:37 JSTに `JraValueParseException`（`HorseNumber`, raw=`取消`）で失敗し、同じnotificationを理由にpipelineが自動停止した。
利用者は調査結果を受け「変更記録を作成し、対応をお願いします」と依頼した。本書は具体的な契約・安全条件の承認対象であり、依頼だけを未提示の契約変更全体への承認とは扱わない。

当該raceのCardはPersist前に失敗している。過去馬番の付け替えやCICDのlegacy migrationではなく、現行Card parserが取消文字列を数値馬番として要求する問題である。
証拠と再現は [evidence.md](evidence.md)、read-only再現コードは [probes/Program.cs](probes/Program.cs)。

2026-09-27追加確認: 利用者は「サイトの知識としても記録」「予想の対象外はよいが、出馬表としてはそのまま記録」と明示。取消馬を出馬表から取り除かず、予想対象の選別と分離する方針を確定した。サイトの観測事実は正本 `docs/27-jra-site-collection-contract.md` §2.3にも記録する。この確認をC2/AC2/AC3へ反映する。本番再開等を含む全体承認とは区別し、本書全体はProposedを維持する。

## Goals / non-goals

- 取消行も公式Horse identityで保持し、普通の未確定Cardと区別する。出馬表の保存・読出しから取消馬を除かず、取得できる馬名・馬主・騎手等をそのまま記録する。
- 取消/除外された馬を新規予想の候補にせず、残りの正常馬を処理できるようにする。
- 未知文字列やidentity不整合に対する安全停止を弱めない。
- 修正版を既存GitHub CI/CDで配備し、今回の対象だけを復旧し実際の収集進捗を確認する。
- データ削除、過去馬番推測、他の失敗の一括retry、旧予想/結果の別馬付替え、管理APIの取消結果コードの流用は行わない。
- 新規画面/フォーム設計は含めない。API/read modelの状態に接続し、既存番号表示のnull安全性を確認する。既存予想履歴は書き換えない。

## Hypothesis ledger

| ID | Claim / boundary | Evidence / falsification | Result / disposition |
| --- | --- | --- | --- |
| H1 | 馬番の誤った列抽出か、公式馬番欄自体が取消なのか | 本番errorだけでは判別不可。公式HTMLの対象rowと実snapshotを照合 | `td.num`自身が取消、馬リンクあり。過去成績には別の11番がある。現在馬番の補完元にしてはならない |
| H2 | 現行コードで同じ終端例外になるか | 実URLのHTML bytesをChromiumに供給し、実PlaywrightPageSnapshotterとRaceCardPageParserを実行 | `HorseNumber / 取消`を再現。HTTP GET成功だけを再現証拠にしていない |
| H3 | nullや行削除だけで解決するか | Card保存→read model→予想とCollector待機条件を独立readerが照合 | nullは番号未確定待ち/予想全体拒否、行削除は既存entryを消さない。単純回避を却下 |
| H4 | 結果の取消コードを流用できるか | ResultStatusとRaceAggregate.DeclareEntryResult前提を照合 | 結果宣言後専用。Cardの出走状態として流用しない |

## Decisions / technical impact

承認記録: 取消馬を出馬表に保持し予想だけ除外する追加確認の後、利用者が「お願いします」と明示。AC1–AC5とC1–C5、GitHub配備・安全条件付き対象限定復旧を含む提示済み対応を承認としてExecution Modeへ移行した。先行のProposed記述は設計経緯であり、現在状態は本Approvalを正とする。

Pre-implementation契約凍結: transportの `Contracts.RaceEntryParticipationStatus` とdomainの同名enumは `Active=0, Cancelled=1, Excluded=2`。公開入力はnullableで未指定を表し、保存済みevent/read DTOの欠落はActive互換。Scraping `RaceEntry.ParticipationStatus` は末尾optionalでActive既定。T1の専有writeはRaceCardPageParser.csと取消専用parser test/fixtureのみ。Mainは共通model/DTO/domain/persistenceと接続を所有する。T0設計完了、T2 Runnable、T1はmodel追加後Runnable、T3–T5 Dependent。Mainがテストbuildを調整し、共有出力への並列buildを避ける。

1. 出走のキーは引き続きRaceId＋HorseId。番号・枠・出走状態は属性であり、番号の穴埋め、行順採番、隣接行/過去成績からの採番をしない。
2. Card用の出走状態を通常/取消/除外として明示する。結果確定後の着順・異常結果とは別。parser→収集DTO→API→domain/event→read model→予想contextの全経路で欠落させない。
3. 対象行の専用馬番セルの完全一致 `取消` / `除外` を非出走として認識する。馬名/馬主/過去成績本文の同文字列を状態判定に使わない。その他の非数値文字列は従来どおりエラー。数値セルを持つ取消表現の追加は専用の現在出走状態が確認できる構造に限定し、本文の曖昧検索はしない。
4. 公式Horse identityは取消/除外でも必須。現在の番号が消えていれば新規entryはnullとし、同じRace/Horseの既知番号があれば既存のnull非上書き契約で保持する。数字を創作しない。通常→取消の再取得でEntryIdと既存参照を維持する。
5. 通常馬の番号nullだけを木曜等の未確定待ち判定に使う。取消/除外のnullを理由にCard全体を15分待機へ落とさない。通常馬が未確定なら従来の保存後待機を維持する。
6. 出馬表の全entry保存・読出しは通常/取消/除外の全行を対象とし、取消馬の行や取得済み属性を削除しない。予想用filterをCard取得、domain登録、出馬表read modelへ流用しない。出馬表登録頭数と予想可能頭数を混同しない。新規の予想生成・スコア対象・手動予想対象検証は通常馬だけとする。通常馬0頭でも全取消行を出馬表として保存・読出しでき、予想生成だけを対象なしとする。Race全体の公式中止は推定せず、過去に生成した予想の履歴/参照は保存し続ける。
7. Cardの通常/取消/除外は明示更新する。Card情報を持たない結果/履歴/旧clientのupsertが既知取消を通常へ戻さないよう、入力未指定と通常の明示指定を区別する。旧eventの項目欠落は通常として読める後方互換を保つ。DB破壊的移行や一括書換えはしない。
8. 出走状態変更後の旧予想context/odds receiptを再利用させない。現在の割当fingerprintには出走状態を含め、保存済みodds snapshotを再解釈しない。取消馬を現役候補として新規に割り当てない。未知marketを勝手に数字解析しない既存契約は維持する。
9. race-detail取得revisionを4→5へ進め、旧Currentも通常の再取得経路へ載せる。今回のFailed対象はrevision変更だけで復旧と見なさず、配備後に限定retryの成否を確認する。
10. parser全体のJraValueParseException/未知例外を一括Isolated化しない。正常な取消データを正しく解釈する修正とし、本番全体停止の安全境界は維持する。

## Concern and agreement ledger

| ID | Concern and evidence | Impact | Proposed disposition | AC/task/test | Agent position | User disposition | State |
| --- | --- | --- | --- | --- | --- | --- | --- |
| C1 | 公式馬番欄に数字がなく過去成績欄には11番がある | 誤採番/別馬付替え | 公式Horse identityと明示状態、番号は推測せずnull/同馬既知値保持 | AC1/AC2/T1–T3、過去11番混入反例 | 推奨 | 承認待ち | Resolved in design |
| C2 | nullだけ/行削除だけでは待機や旧entry残留、予想混入 | 収集停止を避けても誤予想/出馬表欠落 | 全行を出馬表として保存・読出し、予想選別だけ別処理。Result取消コード流用を却下 | AC2/AC3/T2–T4 | 推奨 | 取消馬も出馬表として記録し予想だけ対象外とする方針を明示確認 | Resolved in design |
| C3 | DTO/event追加で旧データ/別writerが既知状態を消す可能性 | 取消復活/参照破損 | 旧event読取互換、未指定は保持、明示更新のみ変更、stable ID維持 | AC2/AC4/T2–T4 | 推奨 | 承認待ち | Resolved in design |
| C4 | 未知値を正常扱いに広げると構造不整合を隠す | 誤保存 | 専用セルの既知値のみ、未知/identity欠落/重複拒否を保持 | AC1/AC4/T1–T4 | 推奨 | 承認待ち | Resolved in design |
| C5 | 全体resumeは別失敗にも影響する | 未修正障害再発 | 配備前pause維持、対象限定retry、他の未解明失敗があればresumeしない。再停止時は原因採取し反復resumeしない | AC5/T5 | 条件付き復旧を推奨 | 承認待ち | Resolved in design |

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | 実ページ相当HTMLの取消/除外を認識し、通常馬・公式馬identityを保持。現在番号を過去成績/行順から生成しない | T0,T1,T4 | live再現、固定HTML→snapshot→parser、未知値/誤scope反例 | Not started |
| AC2 | 初回取消/通常→取消でも全行を出馬表として保存・再読出しし、取消馬の取得済み属性・stable ID・同馬既知番号を保持。旧event/旧入力互換も維持 | T2,T2D,T3,T4 | serialization→API→event→projection/read-back。今回相当16頭は取消1頭込み16頭として残り、馬主等も保持 | Connected |
| AC3 | 取消のnullでは未確定待ちにならず、通常馬のnullでは待機を維持。16頭中取消1頭なら予想候補だけ15頭。全非出走でも出馬表は残し予想のみ対象なし、通常レース中止を推定しない | T3,T4 | collector orchestration、API-only/ML/手動検証、出馬表全件と予想候補の独立比較 | Not started |
| AC4 | 通常カード/結果/oddsの既存動作、状態未指定保持、古いreceipt拒否、未知障害の停止を維持 | T2,T2D,T3,T4 | 関連regression、Release build、format、全体非External suite、Linux CI | Connected |
| AC5 | GitHub経由で同版API/collectorを配備しhealth確認。対象限定再取得で取消例外が消えCard保存と継続進捗を確認する | T5 | workflow、GET失敗履歴/対象attempt、pipeline状態、15分観測または正常後続batch完了（先に得た証拠） | Not started |

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State | Routing | Audit | Result metrics |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T0 | 本番証拠・再現・設計 | Main | Lead | - | 本record/probes、canonical docs | 実parser例外再現 | evidence.md、利用者明示承認 | Verified | Lead — data integrity / architecture | none | unavailable; retries 0; corrections 1; reviews 1 |
| T0R | downstream inventory | entry_reference_inventory | Review | - | read-only | AC2/AC3/AC4のsource経路照合 | evidence.mdのsource trace、Mainの実HTML再現との整合 | Verified | 独立read-only、変更権限なし | T0R-A1 | unavailable; retries 0; corrections 0; reviews 1 |
| T1 | 取消の狭いparser認識/fixture | cancellation_parser | Cost efficient | T2 | src/HorseRacingPrediction.Scraping/Jra/Parsing/RaceCardPageParser.cs; tests/HorseRacingPrediction.Scraping.Tests/Parsing/RaceCardCancellationTests.cs | 実HTML相当snapshot、未知値/過去数値反例 | focused39 passed、Main live16/15反証 | Verified | Worker — frozen contract / exclusive parser scope | T1-A1 | unavailable; retries 1; corrections 1; reviews 2 |
| T2 | 状態contract/domain/persistence | Main | Lead | T0 | Contracts/ApiClient/Domain/API/Application/ReadModelsの状態経路と関連tests | 旧event読取・未指定保持・状態遷移の実保存 | Domain126、API3成功、全体回帰中 | In progress | Lead — public contract / persistence | none | unavailable; retries 0; corrections 0; reviews 0 |
| T2D | domain固定契約の反例tests | cancellation_domain_tests | Cost efficient | T2 | tests/HorseRacingPrediction.Domain.Tests/RaceParticipationStatusTests.cs | focused tests→Domain regression | focused8、Domain126 passed | Verified | Worker — frozen invariants / exclusive test file | T2D-A1 | unavailable; retries 0; corrections 0; reviews 1 |
| T3 | collector/予想/odds接続 | Main | Lead | T1,T2 | Scraping workflow/Collector/Agents/ML/odds関連とtests | AC2–4の統合反例とrevision | 実装済み、全体回帰中 | In progress | Lead — integration / overlapping writes | none | unavailable; retries 0; corrections 0; reviews 0 |
| T4 | 統合回帰・最終レビュー | Main | Lead | T1,T2,T3 | 必要な統合tests、docs | 全ACの経路確認、format/build/test/CI | ローカル全体回帰中 | In progress | Lead — final acceptance | none | unavailable; retries 0; corrections 0; reviews 0 |
| T5 | 配備・限定復旧・観測 | Main | Lead | T4 | GitHub PR/CI/CD、承認された対象retry/resume | AC5、本番GET/実行ログ/PRコメント | 未着手 | Dependent | Lead — security / integration | none | unavailable; retries 0; corrections 0; reviews 0 |

## Delivery / recovery / rollback

承認後にT2の入力互換を凍結してT1へ委譲し、T2と排他的ファイルで進める。T1はproduction確認やAPI変更をしない。T2/T3の同一writerはMainが直列で所有する。
GitHub PRの最終headでCI成功後にmergeし、既存pause/drain→collector→API→health→pause保持の配備を使う。
配備後に対象failure/現在pipelineを再読取し、未解明の別failureがない場合のみ今回の失敗対象1件の限定retryを設定し収集を再開する。通知の消去や他対象の一括retryはしない。別failureがある場合は状態を保持し、その原因と次判断を提示する。
新しい状態を書込後に旧binaryへ単純rollbackしない。異常時はpauseと証拠保全、修正版による前進修復を基本とする。データrestore/削除は別承認。
マージ後の同branchへ追加commit/pushをしない。実行結果はPRコメント/ログに記録し、必要なrecord更新は最新mainから別branch/PR。merged local/remote branchは検証後に削除。

## Documentation updates

- JRA site contract impact: Updated

- `docs/27-jra-site-collection-contract.md`: §2.3に取消セルと行に残る属性の実観測を恒常的なサイト知識として記録し、出馬表保存と予想選別の分離・過去馬番流用禁止を明記。観測事実と未実装の対応を区別する取得元の正本。
- `docs/10-domain-design.md`: 出走状態と結果異常コードを分離する提案、未指定/明示更新の互換境界へのリンク。domain正本。
- `docs/26-collection-platform-design.md`: 取消と未確定待ちの区別、対象限定復旧方針への提案リンク。collection正本。
- UIの新画面/フォーム/一覧変更は提案しない。実装中に必要な外部仕様変更が生じたら本書へ戻す。

## Review gates / next action

Design/task-split: Mainが実データと独立inventoryを照合。T1だけはT2の契約凍結後に独立委譲可能。危険な採番/行削除/未知値の一括無視を却下した。
Concern/agreement: C1–C5を技術的に解決した設計として提示し、2026-09-27利用者「お願いします」で明示承認。未解決のOpen decisionなし。
Pre-implementation: 契約を凍結しT1をgpt-5.6-luna指定のcoding workerへ委譲。専有範囲はparserと専用tests。取消/除外、過去馬番混入禁止、属性保持、未知値拒否を反例とし、RaceCardCancellationTests / RaceCardPublicationTests / RaceCardPageParserTestsのRelease実行を必須とした。Mainがbuild slotを直列調整する。
Checkpoint: 状態contract/domain/writer/readモデルと予想・odds・collectorを接続し、revision5へ更新。共有buildは直列で実施。独立Domain testsは契約凍結後に専有新規fileへ分離できたためT2Dとして追加委譲した。Mainがpublic contractとpersistence判断、統合検証を保持。実サイトprobe16/15とAPI保存/予想3件成功。最終レビューと配備は未完了。
次操作: `dotnet test HorseRacingPrediction.sln --configuration Release --no-restore --filter "TestCategory!=External" -m:1`、exact format gate、migration check、DDD validator、差分レビュー→目的別commit→PR/CI→merge/CD→対象限定retry/条件付きresume→進捗観測。未コミットは本変更に属するsource/tests/docsのみ。APIキー・本番レスポンス全文は保存しない。最終受入まで継続する。

付随事項: 既存diagnostics helperのPowerShell変数補間 `$escapedGroupKey?page` が失敗したため、同じGETを暗号化資格情報からメモリ内で実行して調査した。これは今回の収集原因ではなく、helper自体の修正は本変更のAC外（owner Main、必要なら別変更）とする。

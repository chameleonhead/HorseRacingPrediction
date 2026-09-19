# 未完了の収集基盤変更を一括完了する

- Status: Implemented
- Owner: Main
- Created: 2026-09-15
- Updated: 2026-09-15

## Context

収集基盤の change record には、コードの大半が実装済みだが本番検証が残る3件、設計済みで未実装の1件、実装済みだが本番データ修復が残る1件がある。個別に完了させると、Task metadata、Recovery、batch配送、停止インシデント、主体IDの境界を別々に変更・検証し、同じ永続化・配送・運用経路を繰り返し触ることになる。

本記録は次の既存記録を置き換えず、一つの実行・検証・配備計画として束ねる。

- [収集Workerの同一セッション・マイクロバッチ化](../20260912_collection-worker-microbatch/README.md)
- [想定外収集エラーによる全体停止とSNS通知](../20260913_collection-error-rate-circuit-breaker/README.md)
- [URL失敗時の名称フォールバックと障害ジョブ再開](../20260913_subject-url-fallback-recovery/README.md)
- [JRA競走馬の同定競合防止・不具合データ修復・詳細URL保証](../20260913_jra-horse-identity-and-detail-urls/README.md)
- [収集タスクへ同定メタデータを引き継ぐ](../20260914_collection-task-metadata/README.md)

## Review findings

1. `collection-task-metadata` だけが新規のプロダクション実装を必要とする。現行 `AcquireAsync` はTaskではなくResourceの現在の属性をWorker入力へ渡し、プロフィール由来の調教師発見は業務データ登録なしに名称由来IDでTaskを作るため、再現性と参照整合性の欠陥が現存する。
2. `collection-worker-microbatch` はEnvelope、共有session、部分失敗、相関表示まで実装済みだが、記録上は全19基準の状態表、12 RaceCardの比較計測、横断メトリクスの扱い、配備後smokeの完了証拠が不足している。未実装の横断メトリクスを承認済み範囲から暗黙に除外してはならない。
3. `collection-error-rate-circuit-breaker` は主要経路が接続済みで、AC6/AC8のproduction-path再検証とAC9のSNS publish・SMS subscription確認が残る。SNS publishは外部副作用を伴うため、テストメッセージであることを明記して一度だけ行う。
4. `subject-url-fallback-recovery` は主要経路が接続済みで、騎手・調教師の統合証拠、孤立Recoveryの再関連付け、既存Failureのpreview/applyが残る。Recovery一括投入は新Taskを作る外部状態変更であり、preview件数と対象分類を固定してからapplyする。
5. `jra-horse-identity-and-detail-urls` はコード実装済みだが、本番repairが残る。名前一致だけでは統合せず、固定manifest、backup、pause/drain、dry-runレビュー、同一JRA identityの証拠、冪等性確認を必須とする。source Horseは削除せずredirect/tombstoneを保持する。
6. Task metadataのschema/API契約はmicrobatch Envelope、retry generation、Recovery新Task、DLQ/watchdog、ジョブ詳細のすべてへ波及する。このため永続契約を先に直列で確定し、その後にCollector/API/UIを分割する。
7. 本番repair、Failure一括Recovery、SNS smokeは互いに性質が異なる。単一リリースでコードを配備しても、本番操作は `backup → pause/drain → preview → apply/smoke → post-check → resume` の順に個別証拠を残す。

## Current implementation inventory

2026-09-15の現行HEAD、Git履歴、production caller、関連テストを照合した分類は次のとおり。

| Change | Production implementation | Remaining work | Classification |
| --- | --- | --- | --- |
| collection-worker-microbatch | Envelope集約、共有JRA session、Task別Acquire/Complete、部分失敗、generation guard、Attempt相関、batch詳細画面、local queueが実装済み | AC1–AC19の現行HEAD再追跡、12 RaceCard比較計測、配備後smoke、横断メトリクスをscopeに残すかの整合化 | Code implemented; verification/documentation incomplete |
| collection-error-rate-circuit-breaker | terminal failureとatomic pause、alert incident永続化、SNS dispatcher/retry、outbox/acquire guard、DLQ経路が実装済み | AC6/AC8の現行再検証、実SNS test publish、SMS subscription確認 | Code implemented; production verification incomplete |
| subject-url-fallback-recovery | URL失敗後の名称fallback、stale active row修復、新Recovery task、障害一括Recovery API/UIが実装済み | 騎手・調教師と孤立Recoveryの証拠補完、既存Failure preview/apply | Code implemented; verification and production operation incomplete |
| jra-horse-identity-and-detail-urls | JRA identity伝播、canonical Horse ID、詳細URL検証、専用repair dry-run/apply、redirectが実装済み | 本番backup、dry-run/manifest review、apply、冪等確認、対象再取得 | Code implemented; production repair incomplete |
| collection-task-metadata | Resource属性だけが保存され、Acquireは現在のResource属性を読む。Task metadata列、Task単位validation/互換、canonical主体登録・解決、UI表示は存在しない | T1–T4のプロダクション実装と全検証 | Not implemented |

したがって「未完了5件」は「未実装5件」ではない。新規の機能実装は主に `collection-task-metadata` であり、他4件は現行実装の不足監査、必要な局所修正、配備・本番操作、記録更新が残作業となる。

## Status reconciliation retrospective

### Verified facts

- microbatch記録は、設計、実装、テストを含むcommit `e170c4b` で最初から `Approved` として追加され、その後4件のproduction修正が追記されたが、個別ACの状態列と最終Status更新は追加されなかった。
- microbatch適用後の本番invocation数・メモリ・処理時間は `20260913_collection-failure-investigation-ui` に記録されたが、元のmicrobatch AC11へ逆参照・転記されなかった。
- circuit breakerとsubject URL fallbackは、同じcommit `9444077` で実装と記録が追加された。両記録は「コード実装完了だが本番AC未完了なのでApprovedを維持」と明記しており、Status更新漏れではない。
- Horse identity記録はcommit `f2f0988` で `Implemented (production repair pending)` へ変更された。この値はchange-record formatの正規Statusではなく、未完了AC8/AC9と `Implemented` を同時に表現している。
- 現在のzero-open-item、task/AC ledger、final review、正規状態語彙の強い完了ゲートは、対象実装の後である2026-09-14のprocess更新群により追加・強化された。
- 初回棚卸しでは、record全体のStatusとコード実装状態を分離せず、`Approved` を「未実装」と分類した。これはStatusの更新漏れとは別のレビュー分類ミスである。

### Immediate causes

1. microbatchはACが番号付きリストだけで状態列を持たず、実装commit後に「どのACが未完了か」を機械的・目視で閉じられなかった。
2. 本番証拠が後続change recordへ記録され、元recordへ戻す明示的なclosure taskとownerがなかった。
3. コード完了とchange全体完了を一つのStatusで表そうとして、Horse記録で非正規の複合Statusを作った。
4. 複数機能を同一commitで実装・記録したため、個別recordごとのfinal reviewとStatus遷移がcommit gateにならなかった。
5. 棚卸し側も、Status、AC state、production code、external operationの4軸を照合せず、Status文字列だけで未実装と推定した。

### Systemic cause

change recordのStatusは「承認済みscope全体の完了」を表す一方、コード実装、本番配備、データ操作、運用smokeの部分状態を標準的に表す欄がなかった。当時は、後続記録に証拠が生じた際に元recordを再openして閉じるowner/taskと、repository全recordを対象にした非正規Status・未完了AC・矛盾表現の監査も必須ではなかった。その結果、実装コードは存在するのにStatusから実装有無を判定できず、完了証拠もrecord間で分散した。

### Proposed prevention gates

1. change recordの正規Statusは `Proposed`、`Approved`、`Implemented`、`Superseded` だけとし、括弧付き複合値を禁止する。部分状態は `Completion summary` の `Code`、`Verification`、`Deployment/operation` 列へ分離する。
2. すべてのACを状態列付き表にし、`Not started`、`Connected`、`Verified` のいずれかを必須とする。番号付き箇条書きだけのACはpre-implementation reviewを通過できない。
3. production smokeや運用証拠を別recordで得た場合、同じcommitまたは同じ作業turnで元recordのAC、Verification record、Statusを更新するclosure taskを必須にする。
4. source/testを含む各checkpoint commit前に、変更対象の全change recordについて `Status ↔ AC ↔ task ↔ remaining work` を照合する。`Implemented`かつ未完了AC、非正規Status、全AC VerifiedなのにApproved、本文だけに残る未追跡作業をcommit blockerにする。
5. repository棚卸しはStatusだけで分類せず、`Record status`、`Code state`、`Verification state`、`External operation state` の4列で報告する。
6. 複数recordを一つの実装commitで扱う場合、commit ownerとは別に各recordのclosure ownerをtask planへ記録し、final reviewで全recordを列挙する。
7. 上記を `document-driven-development` とchange-record formatへ反映し、正規StatusとAC整合を検査する小さなvalidatorを追加する。適用は本統合change recordの承認後に行い、既存recordへdry-runして今回4種類の不整合を検出できることを検証する。

再発防止のプロセス変更は [Change recordの状態同期漏れを防止する](../20260915_change-record-status-reconciliation/README.md) で独立して実装・検証する。

## Goals

- Task作成時の安全なメタデータを不変スナップショットとして保存し、全実行・再試行・Recovery経路の正本にする。
- Horse/Jockey/Trainer/Ownerの関連収集Taskを登録済みcanonical IDへ接続する。
- 既存4記録の未検証基準をproduction pathと本番運用証拠で閉じる。
- 本番の安全対象だけを修復・再投入し、収集停止、通知、batch処理、同定、Recoveryを一連の運用として検証する。
- 各元記録を、全基準の証拠が揃ったものだけ `Implemented` に更新する。

## Non-goals

- 同定条件の緩和、同名主体の推測統合、汎用的なHorse merge。
- Resource IDまたはTask IDの全面置換。
- HTML、Cookie、認証情報、無制限payloadの保存。
- 自動再開、実行中Workerの強制終了、承認対象外のFailure再投入。
- 本番データの削除。修復元IDはredirect/tombstoneとして保持する。

## Experience and interaction design

- ジョブ詳細に公開可能なTask metadataだけを日本語ラベルで表示し、対象名と発見元Resourceを追跡できる。
- batch、停止インシデント、Recovery、同定失敗の既存画面を維持し、Task metadata追加によって既存の技術情報を隠さない。
- 一括RecoveryとHorse repairはpreview結果を記録してから実行し、件数、作成・再利用Task、修復・除外対象を事後確認できる。

## Documentation updates

- `docs/22-collector-design.md`: 未コミットのTask metadata・canonical主体ID規則を本変更の正本設計として採用し、実装後に実装済み表現へ更新する。
- 各既存change record: 個別の受け入れ基準、検証結果、差分、本番操作証拠、最終Statusを更新する。
- `docs/changes/20260911_unified-collection-platform/README.md`: Task metadataとbatch実行の最終的な責務境界を同期する。
- `docs/changes/20260911_unified-collection-platform/cutover-runbook.md`: SNS smoke、Failure Recovery、Horse repairの実行順、rollback、post-checkを同期する。

## Decisions

- 既存5記録を統合記録でsupersedeしない。個別の設計・ACを実装契約として維持し、本記録は順序、共有ゲート、完了判定を管理する。
- Task metadataはTask列を正本とし、旧Taskで列が空の場合だけResource属性へ互換fallbackする。
- metadataは許可キー、キー数、値長、総payload量を境界で制限し、Task、Attempt表示、配送payloadの出力もallowlist化する。
- 主体producerは共通の登録・解決境界が返すcanonical IDだけを使用する。業務データ登録に失敗した場合は子Taskを作らず、親Taskへ具体的な失敗を返す。
- schema/API/Task生成契約はMainが直列で所有する。契約固定後にCollector producer、UI、テスト監査を非重複scopeで並行化できる。
- 本番変更はローカル・CI検証済みcommitの配備成功後に限る。preview結果が設計条件を満たさない場合はapplyせず、change recordを `Approved` のまま保持する。

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| UAC1 | `collection-task-metadata` AC1–AC9をすべて満たし、通常、bulk、関連発見、手動、Recovery、revision、依存生成からWorker・Attempt・UIまでTask作成時metadataが不変である。 | T1–T4 | migration/store/transport/handler/API/component E2E | Verified |
| UAC2 | Horse/Jockey/Trainer/Ownerの子Taskは登録・解決済みcanonical IDだけを使い、未登録IDの実行不能Taskを作らない。 | T2, T3 | cross-producer/API/Collector E2E | Verified |
| UAC3 | `collection-worker-microbatch` AC1–AC19を状態表へ追跡し、同日12 RaceCardの1 session実行、部分失敗、再配信、相関表示、比較計測をproduction-equivalent経路で確認する。 | T4, T5 | Collector/API tests、benchmark、配備smoke | Verified |
| UAC4 | `collection-error-rate-circuit-breaker` AC1–AC9を満たし、再開後の新規失敗、設定/IAM異常、実SNSへの一度のtest publish、SMS subscription確認を記録する。 | T4, T6 | store/dispatcher tests、deploy gate、AWS evidence | Verified |
| UAC5 | `subject-url-fallback-recovery` AC1–AC12を満たし、騎手・調教師の名称fallback、孤立Recovery再関連付け、対象Failureのpreview後一括Recoveryを記録する。 | T3, T4, T7 | handler/store/API tests、production preview/apply | Verified |
| UAC6 | Horse repairは固定manifestの同一JRA identity対象だけに適用され、backup、pause/drain、dry-run、apply、冪等再実行、redirect、再取得、除外対象を記録する。 | T8 | repair report、DB/API post-check | Verified |
| UAC7 | 配備後もpipeline停止・再開、Realtime公平性、retry generation、DLQ/watchdog、lease expiry、重複配送、partial failure、URL/location検証に回帰がない。 | T4–T9 | solution tests、production smoke、health/queue checks | Verified |
| UAC8 | 全元記録のACとtaskが `Verified` へ追跡され、CodeGraph同期、format、build、非External solution test、CI/CD、本番post-check、文書同期、git checksが成功する。 | T9 | closure ledgerと検証記録 | Verified |

既存5記録の個別ACは省略せず本記録へ継承し、UAC1–UAC6から各記録のAC表へ追跡する。個別ACの変更には当該記録の再提案・再承認を要する。

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | metadata schema、validation、Task生成、lease/retry/recovery互換を実装する（UAC1）。 | Main | High capability | - | CollectionOperations、migration、store tests | focused migration/store tests | schema diff、全生成経路matrix | Verified |
| T2 | 共通主体登録・解決契約とcanonical IDをAPIへ実装する（UAC1, UAC2）。 | Main | High capability | T1 | API contracts/endpoints、persistence、API tests | API concurrency/integrity tests | registered entityとTask ID一致 | Verified |
| T3 | 全主体producer、fallback、診断、metadataをCollectorへ接続する（UAC1, UAC2, UAC5）。 | MainまたはWorker | High capability | T2 | Collector subject handlers/tests | handler/transport E2E | 未登録子Taskゼロ、fallback成功 | Verified |
| T4 | ジョブ詳細のallowlist表示と全既存経路の回帰テストを統合する（UAC1, UAC3–UAC5, UAC7）。 | Worker draft + Main review | Cost efficient + High capability | T1–T3 | API UI/components/tests（共有contract除外） | component/API/solution tests | UI evidence、回帰matrix | Verified |
| T5 | microbatch AC1–AC19を再監査し、不足実装、12 RaceCard比較、production-equivalent smokeを閉じる（UAC3, UAC7）。 | Main | High capability | T4 | batch/dispatcher/worker/config/tests、記録 | batch failure/retry/performance tests | AC19件のclosure ledger | Verified |
| T6 | circuit breakerの未接続テスト、配備gate、SNS/SMS smokeを閉じる（UAC4, UAC7）。 | Main | High capability | T4 | alert/store/deploy tests、AWS read/write smoke、記録 | focused tests、AWS証拠 | AC9件のclosure ledger | Verified |
| T7 | subject URL fallbackの未検証経路を閉じ、対象Failureをpreview/applyする（UAC5, UAC7）。 | Main | High capability | T3, T4, T6 | recovery/store/handler tests、production recovery、記録 | preview/apply report | AC12件のclosure ledger | Verified |
| T8 | JRA Horse repairを安全手順で本番適用し再取得する（UAC6, UAC7）。 | Main | High capability | T4, T6 | deploy、production repair、記録 | backup/manifest/apply/idempotency report | AC8/AC9/AC11 production evidence | Verified |
| T9 | 全体回帰、CodeGraph、CI/CD、本番post-check、文書同期、分離コミット、最終監査を行う（UAC7, UAC8）。 | Main + independent review | High capability | T5–T8 | repository全diff、CI/CD、change records | format/build/test/diff/status/CI/health | 全task・全AC Verified | Verified |

## Execution and cutover order

1. 承認後にpre-implementation reviewを記録し、現在の未コミット文書を設計checkpointとして単独コミットする。
2. T1→T2で共有永続/API契約を固定し、migration互換と全Task生成経路を検証してcheckpoint commitを作る。
3. T3と、契約固定後の非重複なT4テスト/UI sliceを実装・統合する。
4. T5–T7の既存実装を再監査し、ローカル・CI相当の全検証を完了する。
5. CodeGraph同期、format、build、非External solution test、diff/status確認後に目的別commitをpushし、CI/CDとdeployを終端まで確認する。
6. 本番対象DBを識別してbackupし、pipelineをpause、active leaseをdrainする。
7. Horse repairとFailure Recoveryをそれぞれdry-run/previewし、固定対象と除外理由を記録する。条件不一致ならapplyしない。
8. 安全対象だけをrepair applyし、冪等再実行と対象再取得を確認する。次にURL Failure Recoveryをapplyする。
9. SNSへ識別可能なtest incidentを一度publishし、topic、subscription confirmation、通知状態を確認する。
10. queue、DLQ、pipeline、health、ジョブ画面、batch相関をpost-checkしてresumeする。
11. 個別記録と本記録を更新し、全AC/taskがVerifiedの場合だけ `Implemented` とする。

## Review gates

- **Design and task-split review** — Reviewer: Main。Inputs: 既存5記録、未コミット文書、現行CodeGraph、現行git状態。Decision: 設計上の未決定はなく、一括実行は可能。ただし共有schema/API契約と本番操作を直列化し、コード実装・配備・データ操作・SNS smokeを別ゲートで扱う。UAC1–UAC8はT1–T9と個別記録のACへ追跡可能。T1/T2/T5–T9はデータ整合性・外部副作用・統合判断を含むためMainが保持し、契約固定後のT4だけ限定委譲可能。Follow-up: ユーザーが本記録と継承ACを明示承認した後、pre-implementation reviewを行いT1から開始する。
- **Pre-implementation review** — Reviewer: Main。Approved状態と継承ACを確認し、metadata永続契約を先行、既存実装の証拠補完、本番操作を最後に直列実行する方針で開始した。
- **Checkpoint review** — metadata schema/API/producer/UIと全非External回帰が成功した時点でcommit `7d25c7b`、状態漏れ防止をcommit `bf5e507`、本番maintenance gateをcommits `b130be6`–`f63b6a6`として分離した。
- **Final review** — Reviewer: Main。T1–T9、UAC1–UAC8、全継承ACを実装、テスト、production runへ再追跡し、acceptance-blockingな未完了または外部blockerがないことを確認した。

## Verification record

- 2026-09-15: `git status`で未コミットの `docs/22-collector-design.md` と未追跡の `20260914_collection-task-metadata/README.md` を確認し、ユーザー変更として保持した。
- 2026-09-15: CodeGraphで現行 `CollectionPlatformStore.AcquireAsync` がResource属性をlease入力に使用すること、`JraSubjectProfileCollectionHandler.DiscoverHorseReferencesAsync` が名称由来IDで未登録Trainer Taskを作り得ることを確認した。
- 2026-09-15: microbatchの共有session、Envelope、Attempt相関、circuit breakerの停止インシデント、URL fallback/Recoveryの主要実装が既に記録・コードへ接続されていることを確認した。
- 2026-09-15: 実装コミット `e170c4b`、`9444077`、`8ecb524`、`f2f0988` の変更内容が現行HEADに残り、production callerを持つことを確認した。
- 2026-09-15: `dotnet test HorseRacingPrediction.sln -c Release --no-restore --filter "FullyQualifiedName~CollectionLambdaInvocationTests|FullyQualifiedName~JraSessionExecutionScopeTests|FullyQualifiedName~CollectionPlatformOutboxDispatcherTests|FullyQualifiedName~CollectionPipelineAlertDispatchServiceTests|FullyQualifiedName~JraSubjectCollectionHandlerTests|FullyQualifiedName~HorseIdentityRepairEndpointsTests" -v:minimal` を実行し、Collector 33件、API 15件が成功した。他projectは該当テストなし。
- 2026-09-15: 本レビューは文書だけを変更し、プロダクションコード、外部環境、本番データ、SNSには変更を加えていない。
- 2026-09-15: ユーザーが再発防止に加えて積み残し全体の対応を依頼したため、本記録と継承する既存5記録の受け入れ基準をApprovedとしてExecution Modeへ移行した。
- 2026-09-15: immutable task metadata、allowlist validation、全Task生成経路、canonical主体登録、Job詳細表示を実装し、format、Release build、非External solution tests（Contracts 43、Domain 96、Application 56、Infrastructure 13、ML 14、Agents 106、Scraping 224、Collector 195、API 203成功・1 skip）が成功した。
- 2026-09-15: app-ci `34871217058` とapp-deploy `34871217055` が成功し、API health、Lambda infrastructure、DB migrationを含めproductionへ反映した。
- 2026-09-15: production preview `34872379869` はHorse repair 0件、主体同定Recovery 0件を報告した。apply `34872777840` はpipeline pause、Running task drain、残存候補0件、resumeを成功させ、confirmed SMS subscription 1件へ識別可能なSNS test messageを一度publishした。
- 2026-09-15: change-record validatorは対象記録すべてissues 0、`git diff --check`成功、CodeGraphは同期済みであることを確認した。

## Deviations and follow-up

- production preview時点でrepair/recovery候補は0件だったため、Horse統合やRecovery task作成は行わなかった。これは対象がすでに収束していた結果であり、追加変更を避ける安全側の完了として扱う。
- 初回maintenance previewはremote hostの`jq`不在、初回applyはTask状態名の誤り、SNS smoke初回は古いTerraform stateのoutput不在を検出した。いずれも外部データ変更またはSNS publish前に停止し、runner側解析、`Running`状態、AWS APIによるtopic解決へ修正後に再検証した。
- 2026-09-18: UAC2は個別経路では成立していたが、高速化後のbulk RaceEntry保存がHorse source identityを保持せず、保存主体と子Taskのcanonical IDが分岐する本番回帰を確認した。Jockey/Trainerの名称正規化差とOwnerの別ID体系も同じ境界の未検証箇所である。当時の完了履歴は書き換えず、[主体ジョブのcanonical ID整合と過去ジョブフォールバック](../20260918_canonical-subject-job-fallback/README.md)で全入口と過去ジョブ修復を再設計・再検証する。

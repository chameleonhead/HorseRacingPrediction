# Race出走馬の後着データ補完

- Status: Proposed
- Owner: Main
- Created: 2026-09-19
- Updated: 2026-09-19

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Completed | 要求revisionの全terminal経路での後続実体化、request実行条件の永続化、migration preview/apply/progress、設定画面、deploy後migration jobを接続した。 |
| Verification | Completed | Collector 269件、API 247件（既存skip 1件）、solution build、format、change-record validator、CodeGraphを完了した。 |
| Deployment/operation | Not started | push/deployとproduction migration preview/apply、本番データ補正は未実施。既存error jobは変更していない。 |

> **2026-09-19 design correction:** ownerはRaceCardを取得できた場合だけ取得でき、過去RaceResultや
> 現在のHorse profileからは復元できない。全owner欠損Raceへ補正要求を作る現行migrationは、
> Card取得期間外にも実行不能な要求を作るため、push/deploy/applyしてはならない。
> [JRAサイト収集契約とCard限定補正](../20260919_jra-site-collection-contract/README.md)の承認・実装後に再度Approvedとする。

## 調査結果

対象Race `race-429efd4b-9d45-5464-99fb-ffcbe19d8e55`（2026-09-19 阪神11R）は、
画面上で16頭中15頭の馬主が欠落している。一方、`Race / 20260919:Hanshin:11 / race-detail` は
2026-09-19 15:50:41 JSTに成功し、未解決failureは0件である。

`ApplyBulkRaceResult` は同じ`EntryId`が既に存在すると、そのentryを無条件に`continue`する。
結果ページ等からownerなしでentryが先に登録された場合、後から出馬表が`OwnerName`を取得しても保存されない。
Race APIはentryのownerがnullの場合にHorse profileのownerを表示補完するため、プロフィール取得済みの1頭だけが
表示され、残る15頭が`—`になる。したがってparserの全面失敗ではなく、後着データの永続化漏れである。

成功判定はbulk commandの受付とRace結果保存を基準にしており、全entryのowner充足を成功条件に含めない。
このため不完全なRaceでもcollection taskは成功し得る。

## Incident ledger

```text
Incident: 2026-09-19 阪神11Rで16頭中15頭の馬主が欠落
Temporary recovery: なし（画面・GET・コードのread-only調査のみ）
External/input condition: CardとResultが別時刻に公開され、Result由来entryが先に存在し得る
Technical defect: ApplyBulkRaceResultが既存EntryIdを更新せず、後着のOwnerNameを破棄する
Workflow gap: 成功判定にentry enrichmentの保存結果・欠損観測がない
Corrective proposal: 既存entryへの非破壊・冪等マージと、明示的なrepair preview/apply
Permanent fix: Not started
Remaining risk: 2026-09-19の他Raceおよび過去Raceに同型欠損があり得る
```

## 方針

1. `ApplyBulkRaceResult`は既存entryを読み飛ばさず、後着した収集値をマージする。
2. null/空値で既存の非null値を消さない。異なる非null値は最新の検証済みpayloadを採用する。
3. 実効値が変わらない再送はeventを追加しない。
4. 馬主だけを特例にせず、既存のentry update contractで安全に扱える収集項目を同じ規則にする。
5. collection成功とデータ完全性を分離し、owner欠損数をstage outcome/運用表示で観測できるようにする。
6. 既存Raceは自動再投入しない。日付・Raceを指定したread-only previewと、確認後の明示applyを用意する。
7. repairは新しいRace taskを増殖させず、既存Race resourceへ冪等に再収集・マージする。

## 追加対策案（再承認対象）

### 要求revisionを必ず実行する

- 収集要求の正本は個々のtaskではなく、resource/definitionの`RequiredRevision`と未充足requestとする。
- 既存の低いrevisionのtaskへ要求が統合された場合、そのtaskが成功すれば現行どおり高いrevisionの後続taskを作る。
- 既存taskが失敗・中断・dead-letterになった場合も、未充足の高いrevision requestがあれば、terminal化と同じtransactionで後続taskを1件だけ作る。
- 同じresource/definitionにactive taskは常に最大1件とし、同じbatchの再実行でrequest/taskを重複生成しない。
- 後続taskは元taskのpriorityを自動昇格せず、未充足requestに記録されたlane/priorityを用いる。既存failure notificationの解決は後続taskが実際に成功した時だけ行う。

### データ補正をrace-detail migrationへ含める

- 馬主欠損補正を独立した手動repairで終わらせず、既存の`race-detail` migrationのpreview/apply/progressへ統合する。
- previewは構造移行件数に加えて、保存済みRaceごとのentry数、entry-level owner欠損数、正規化後のRace resource ID、実行可否、既存要求・実行状態をread-onlyで表示する。
- applyはpipelineのpauseとactive taskのdrainを前提に、構造移行後、owner欠損Raceへ`race-detail` revision 2の補正要求を作る。値をHorse profileからコピーしたり推測で直接上書きせず、公式Cardを再取得してaggregateの非破壊mergeを通す。
- migration batch IDは版と対象を基に決定的に生成し、途中失敗・API再起動・apply再実行でも同じrequestを再利用する。大量対象は固定サイズでcheckpointし、完了済み対象を飛ばして再開する。
- apply直後は「補正要求済み」であり「補正完了」とは扱わない。progressは`補正済み`、`処理待ち/実行中`、`取得失敗`、`公式情報なし`を分け、欠損が0件になったRaceだけを完了とする。
- 設定画面からmigrationのpreview、明示apply、進捗・失敗対象確認を行えるようにする。deploy workflowはpreviewのerrorが0件であることを確認してapplyし、resume後は非同期補正の進捗を確認可能にする。

### 旧repair画面の扱い

- migration適用前の個別診断用としてpreview APIは残す。
- apply操作はmigrationへ一本化し、同じ補正要求を作る入口を複数残さない。既存の個別apply API/UIはmigrationへの互換呼び出しにするか、production callerが0件であることを確認して廃止する。
- 「Race画面でHorse profileから表示補完されている件数」と「entry-level owner欠損数」は異なるため、migration画面では後者であることを明記する。

## 先行変更から併せて是正する点

- 出馬表のdomain writeが失敗しても、parse済みの公式発走時刻evidenceは失わない。
- Resource Locationへ`Card` / `Result` / `Unknown`のartifact種別を保存し、異なるartifactの候補を
  成功候補として使わない。legacy locationは自動推定せず`Unknown`のまま、page identity確認後だけ昇格する。
- 上記は既存error taskの自動復旧、priority変更、notification解決を行わない。

## Acceptance criteria

| ID | Observable criterion | Verification | State |
| --- | --- | --- | --- |
| AC1 | ownerなしで先に登録済みのentryへ、後着CardのOwnerNameが保存される。 | domain integration test | Verified |
| AC2 | null/空の後着値は既存ownerを消さず、同じpayloadの再送はaggregate event/versionを増やさない。 | replay/non-destructive tests | Verified |
| AC3 | owner以外の対象収集項目も同じmerge規則で補完され、HorseId/EntryIdの同一性は変更しない。 | field matrix tests | Verified |
| AC4 | Card parserが返したowner件数と保存後owner件数を検証し、欠損を成功taskの内部に隠さず構造化outcomeへ残す。 | collector/API integration test | Verified |
| AC5 | 対象Raceの再収集後、16頭すべての馬主がRace API/画面で表示される。 | production read-only verification | Connected |
| AC6 | 2026-09-19の対象範囲をpreviewすると、Race別のentry総数・owner欠損数・repair可否を表示し、データを変更しない。 | before/after invariant test | Verified |
| AC7 | repair applyは明示選択したRaceだけを処理し、task/request/priority/notificationを自動変更せず、再実行も冪等である。 | repair integration test | Verified |
| AC8 | Card write failure時もparse済み公式StartTime evidenceをcompletionへ返す。 | handler failure test | Verified |
| AC9 | Locationはartifact種別を持ち、Card URLをResult、Result URLをCardの成功候補にしない。legacyはUnknownからpage identity確認後のみ昇格する。 | migration/store/navigation tests | Verified |
| AC10 | 既存error jobの自動復旧・自動再投入・priority変更・notification解決を行わない。 | DB invariant test | Verified |
| AC11 | 低revisionのactive taskへ高revision要求が統合された後、元taskが成功・失敗・中断・dead-letterのどれで終わっても、未充足要求から高revisionの後続taskがちょうど1件作られる。 | store integration/concurrency tests | Verified |
| AC12 | race-detail migration previewで、構造移行対象とowner欠損Race、entry総数、欠損数、実行可否、既存処理状態をデータ変更なしで確認できる。 | API integration/before-after invariant tests | Verified |
| AC13 | migration applyはpause・drain済みの場合だけ実行でき、owner欠損Raceへrevision 2の補正要求を冪等に登録する。Horse profile値の直接コピーや既存priorityの変更は行わない。 | migration/store integration tests | Verified |
| AC14 | migrationが途中失敗またはAPI再起動されても、同じmigrationを再実行すると完了済み対象を飛ばして継続し、request/taskを重複生成しない。 | restart/idempotency/partial-failure tests | Verified |
| AC15 | 設定画面でmigrationのpreview、明示apply、補正済み・処理中・失敗・公式情報なしの進捗を確認でき、Race APIのentry-level owner欠損が0件のRaceだけが補正完了になる。 | component/API/E2E and production post-check | Connected |

## Task split

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | entry merge contract | Main | Lead | Approval | Domain + domain tests | AC1-AC3 | targeted tests | Verified |
| T2 | completeness outcome | Main | Lead | T1 | Collector/API + tests | AC4 | targeted tests | Verified |
| T3 | repair preview/apply | Main | Lead | T1 | Operations/API/UI + tests | AC5-AC7, AC10 | integration/component tests | In progress |
| T4 | evidence/location correction | Main | Lead | Approval | Collection platform + tests/migration | AC8-AC10 | migration/store/handler tests | Verified |
| T5 | final review | Main | Lead/Review | T1-T4 | Change record only | traceability audit | audit evidence | In progress |
| T6 | terminal時の未充足revision実体化 | Main | Lead | Re-approval | Collection platform store + tests | AC11 | success/failure/cancel/dead-letter/concurrency tests | Verified |
| T7 | migration preview/apply/progress契約 | Main | Lead | T6 | Operations/API contracts + tests | AC12-AC14 | persistence-boundary integration tests | Verified |
| T8 | migration設定画面と旧repair入口整理 | Main | Lead | T7 | Settings/API client/UI + tests | AC12, AC15 | component/API tests and caller inventory | Verified |
| T9 | deploy migration・本番補正検証 | Main | Lead/Review | T6-T8 | deploy workflow, change record | AC13-AC15 | cutover evidence and production read-only post-check | Dependent |

## Review gates

### Design and task-split review

- Reviewer: Main
- Decision: Approved design is internally consistent; every AC maps to a task and verification.
- Boundary: no automatic recovery, priority mutation, notification resolution, or broad retry.
- Sequencing: schema/location and shared contracts are serialized; no parallel writer is used.

### Pre-implementation review

- Reviewer: Main
- Runnable frontier: T1 and T4. Main owns both and will serialize shared contract/schema edits.
- Inputs: production Race/job evidence, aggregate/event/read-model paths, collector handler/store paths.
- Escalation: material external behavior change, destructive repair, or inability to preserve old collector/API compatibility returns the record to Proposed.
- Expected evidence: production-shaped regression, replay/idempotency, null-preservation, migration/store/handler, repair invariant, UI/component tests.

## Documentation updates

- `docs/changes/20260919_race-detail-phase-recovery/README.md`: links this follow-up for the previously open Location artifact and scheduling-evidence corrections.
- `.github/workflows/app-deploy.yml`: APIと新Collectorの両配備後に、pause、running task drain、preview、冪等apply、resumeを行うproduction migration jobを追加した。
- No additional canonical architecture document currently defines entry enrichment or operator repair semantics; this approved change record is the canonical change-specific source.

## Deployment and recovery gate

API/schemaを先行し、Collector互換を確認してからCollectorを配備する。配備後にread-only previewを実行し、
対象Raceを明示してrepair applyする。対象RaceのAPIと画面でownerが揃うことを確認するまで完了としない。
既存error jobは本repairに混ぜない。

追加変更後は、`race-detail` migrationを次の順で実行する。

1. 対象DBのbackupを取得し、API/Collectorの互換版を配備する。
2. pipelineをpauseし、実行中taskが0件になるまでdrainする。
3. migration previewで構造移行error、owner欠損対象、既存処理状態を確認する。
4. migration applyで構造移行とrevision 2補正要求の登録を行う。
5. pipelineをresumeし、migration progressで補正完了・失敗・公式情報なしを追跡する。
6. 対象Race API/画面とentry-level集計で欠損0件を確認する。欠損または失敗が残る限りmigrationは完了扱いにしない。

## Additional design and task-split review

- Reviewer: Main
- Finding: `RequiredRevision`はactive task成功時には後続taskを生成するが、失敗・中断・dead-letter経路では未充足requestの実体化保証がない。単純なactive taskのrevision上書きでは実行中payloadとの不整合が起きるため採用しない。
- Decision: 全terminal経路で未充足requestを再評価し、transaction内でactive taskを入れ替える共通処理を採用する。
- Migration boundary: 補正値の直接書換えは行わず、公式Card再取得と既存aggregate mergeをデータ補正手段とする。個別error jobの一括復旧は対象外。
- Task split: persistenceとmigration契約が共有状態を変更するためT6-T9は直列実行し、並列writerは使用しない。
- Approval gate: AC11-AC15と旧repair入口の一本化は外部・運用仕様の追加であるため、再承認まではproduction codeを変更しない。

## Additional pre-implementation review

- Reviewer: Main
- Approval: 2026-09-19にユーザーがAC1-AC15とmigration一本化を承認した。
- Runnable frontier: T6。T7はterminal実体化基盤、T8はmigration API契約、T9は全コード・検証に依存する。
- Write ownership: Mainがstore、API contract、Settings UI、deploy workflow、tests、change recordを直列で所有する。共有永続状態と共通contractがあるため並列writerは使用しない。
- Required evidence: terminal種別ごとの後続task一意性、migration preview不変性、apply/restart冪等性、UI状態、workflow contract、全関連回帰、format、CodeGraph、diff/status。
- Escalation: 直接データ書換えが必要、公式Cardから補正不能、既存error jobの自動復旧が必要になった場合は承認範囲外として再設計する。

## Checkpoint review

- Reviewer: Main
- Finding: direct candidateのLocation分類だけでは、自動探索後に新規保存するRequested/Final URLがUnknownのまま残る漏れを検出した。
- Fix: stage outcomeのURLとartifactを照合し、新規・redirect Locationにも分類を保存する。明示URLの仮Location IDはDB更新対象から除外する。
- Regression: schema baseline/concurrency、Location store、Card/Result navigation、write failure evidenceを追加・再実行した。
- Workflow assessment: 完了前のチェックポイントで検出・修正できた単発実装漏れであり、既存DDD/セルフレビュー規約の不足は実証されていないためskill変更は行わない。

## Verification record

- `dotnet test tests/HorseRacingPrediction.Domain.Tests/HorseRacingPrediction.Domain.Tests.csproj --no-restore`: 107 passed.
- `dotnet test tests/HorseRacingPrediction.Collector.Tests/HorseRacingPrediction.Collector.Tests.csproj --no-restore`: 265 passed.
- `dotnet test tests/HorseRacingPrediction.Api.Tests/HorseRacingPrediction.Api.Tests.csproj --no-restore`: 247 passed, 1 existing skip.
- `dotnet format HorseRacingPrediction.sln --no-restore --verify-no-changes`: passed after formatting.
- `git diff --check`: passed.
- `codegraph sync .`: completed; index reported current.
- Production AC5 remains connected but unverified until deployment and explicit selected-Race repair.

### Additional implementation verification

- `dotnet test tests/HorseRacingPrediction.Collector.Tests/HorseRacingPrediction.Collector.Tests.csproj --no-restore`: 269 passed。success/failure/cancel/dead-letterの全terminal経路、schema v17、同時初期化を含む。
- `dotnet test tests/HorseRacingPrediction.Api.Tests/HorseRacingPrediction.Api.Tests.csproj --no-restore`: 247 passed, 1 existing skip。migration preview/apply replay、設定画面を含む。
- `dotnet build HorseRacingPrediction.sln --no-restore`: passed、0 warnings / 0 errors。
- `dotnet format HorseRacingPrediction.sln --no-restore --verify-no-changes`: passed。
- `python .codex/skills/document-driven-development/scripts/validate_change_records.py docs/changes/20260919_race-entry-owner-enrichment/README.md`: issues=0。
- `codegraph sync .`と`codegraph explore "MaterializeUnsatisfiedRevisionAsync callers and race-entry-owners migration endpoint production path"`: production storeからAPI、設定画面、testまでの接続を確認した。
- `git diff --check`: passed。

## Additional checkpoint and final review

- Reviewer: Main
- Checkpoint finding: 単一request APIはactive taskへ統合した高revisionを`RequiredRevision`へ反映していなかった。bulkだけの局所修正では同型不具合が残るため、単一・bulk双方でrequestを永続化し、terminal共通処理から後続taskを生成するよう修正した。
- Persistence finding: 後続taskが元taskのlane/priority/metadataを流用すると新revision要求の条件を失うため、schema v17でrequestへ`Lane`、`Priority`、`MetadataJson`を追加し、後続taskはrequestから復元する。
- Migration review: 固定batch `migration:race-entry-owners:v2`、全Race read-only scan、pause/running-drain gate、公式Card再取得、進捗分類を実経路へ接続した。Horse profileからの直接コピーは存在しない。**ただし後続確認で、全Race scanから全欠損Raceへ要求する設計はCard取得期間外のRaceを補正できないことが判明したため、本項の実装完了判定を撤回し、再設計を要する。**
- Legacy surface: 旧日付preview/apply APIは外部互換用に残し、applyはpause/drainと同じ固定migration batchへ委譲する。旧Web client/UI callerは0件で、設定画面の操作入口はmigrationへ一本化した。
- Final state: T6-T8の基盤部分は検証済みだが、owner補正対象の選定はCard取得元制約に違反している。T9、AC5、AC13-AC15は再設計後に再検証するため、本recordを`Proposed`へ戻す。
- Workflow assessment: テストが承認後の実装中に既存単一request APIの同型欠損を検出し、承認済み範囲内で根本修正・再検証できた。既存DDD/セルフレビュー規約の不足を示す反復失敗ではないためskill変更は行わない。

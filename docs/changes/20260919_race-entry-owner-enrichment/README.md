# Race出走馬の後着データ補完

- Status: Approved
- Owner: Main
- Created: 2026-09-19
- Updated: 2026-09-19

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Completed | 非破壊merge、欠損outcome、repair preview/apply UI、Location artifact、発走時刻evidenceを接続した。 |
| Verification | Completed | Domain 107件、Collector 265件、API 247件（既存skip 1件）、format/buildを実行した。 |
| Deployment/operation | Not started | push/deployと本番対象Raceの明示repairは未実施。既存error jobは変更していない。 |

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

## Task split

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | entry merge contract | Main | Lead | Approval | Domain + domain tests | AC1-AC3 | targeted tests | Verified |
| T2 | completeness outcome | Main | Lead | T1 | Collector/API + tests | AC4 | targeted tests | Verified |
| T3 | repair preview/apply | Main | Lead | T1 | Operations/API/UI + tests | AC5-AC7, AC10 | integration/component tests | In progress |
| T4 | evidence/location correction | Main | Lead | Approval | Collection platform + tests/migration | AC8-AC10 | migration/store/handler tests | Verified |
| T5 | final review | Main | Lead/Review | T1-T4 | Change record only | traceability audit | audit evidence | In progress |

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
- No additional canonical architecture document currently defines entry enrichment or operator repair semantics; this approved change record is the canonical change-specific source.

## Deployment and recovery gate

API/schemaを先行し、Collector互換を確認してからCollectorを配備する。配備後にread-only previewを実行し、
対象Raceを明示してrepair applyする。対象RaceのAPIと画面でownerが揃うことを確認するまで完了としない。
既存error jobは本repairに混ぜない。

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

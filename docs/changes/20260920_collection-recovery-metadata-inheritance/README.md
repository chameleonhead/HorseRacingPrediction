# 再取得タスクへ主体メタデータを継承する

- Status: Implemented
- Owner: Main
- Created: 2026-09-20
- Updated: 2026-09-20

## Context

production の `Trainer/JRA/trainer-ae6f6e74-ec5a-5954-8d2b-4e324b6cb0d1/trainer-profile` では、元の失敗Taskに `name=村山 明（栗東）` と発見元metadataが保存されていた。一方、ジョブ詳細から作成された `ManualRefresh` Taskはmetadataが空で、CollectorがJRAへ遷移する前に `SubjectIdentification:MissingName` で終了した。

原因は、属性を省略した再取得要求を `CollectionPlatformStore.RequestCoreAsync` が空のTask metadata `{}` として固定することにある。Resource属性は残っているが、lease取得は新Taskの空metadataを正本として扱うためResourceへフォールバックしない。これは [収集タスクへ同定メタデータを引き継ぐ](../20260914_collection-task-metadata/README.md) の、手動再取得・Recoveryで元Taskの不変metadataを継承する承認済み契約に対する局所的回帰である。

## Goals

- `ManualRefresh` と `Recovery` が新Taskを作るとき、直前Taskの不変metadataを既定値として継承する。
- 明示された補正属性だけを継承値へ上書きする。
- 過去Taskのmetadataを変更せず、新Taskへ独立したスナップショットを保存する。

## Non-goals

- productionへのdeploy、retry、recovery、pipeline操作は行わない。
- 主体同定条件、名前正規化、JRA navigationを変更しない。
- 空名や曖昧な主体を自動的に同定しない。
- `docs/changes/20260919_collection-monitor-root-cause-triage/README.md` は変更しない。

## Documentation updates

- 本change recordだけを追加する。既存の正本 `20260914_collection-task-metadata` の契約は変更せず、今回の反例と閉鎖証拠をここに記録するため、他の正本文書更新は不要である。

## Decisions

- 継承はUIではなくStoreのTask生成境界で保証し、API、ジョブ詳細、一括障害Recoveryのcaller差をなくす。
- 継承元は、同じResource/Definitionの直前Taskにある最新の非空metadata、次にResource属性とする。現在の失敗Taskが空metadataでも、それ以前の有効スナップショットを利用できるようにする。
- callerが属性を指定した場合は継承値へキー単位で上書きする。指定がないキーを消去しない。
- Initial、Discovery、Backfill等の通常登録は従来どおりcallerが渡したmetadataを新Taskのスナップショットとし、今回の継承規則を適用しない。

## Acceptance criteria

| ID | Observable criterion | Task | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | metadataを省略した`ManualRefresh`は、完了済み元Taskの`name`と発見元を新Taskへ保持し、leaseで同じ値を取得できる。 | T1 | Store regression test | Verified |
| AC2 | metadataを省略した`Recovery`は、直近Taskが空でも、それ以前の非空Task metadataを新Taskへ保持する。 | T1 | Store regression test | Verified |
| AC3 | 補正metadataを指定した再取得は指定キーだけを上書きし、指定されていない元metadataを保持する。 | T1 | Store regression test | Verified |
| AC4 | 通常の新規登録、既存Taskの不変性、metadata検証・秘密情報制限を維持する。 | T1 | focused Store suite and diff review | Verified |
| AC5 | production操作を行わず、基点change recordと既存の無関係な未コミット変更を変更セットへ含めない。 | T1 | git diff/status review | Verified |

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | 再取得metadataの継承・上書き規則と反例テストを実装し、回帰確認する。 | Main | Lead tier — 永続化境界と既存未コミット変更が重なる単一の短いtask | Approval | Store、Store tests、本record | focused tests、format、diff/status | AC1-AC5 | Verified |

## Concern and agreement review

- **既存の空Taskが最新である場合:** 最新Taskだけを継承すると今回のproduction対象を再度直せない。最新の非空Taskまで遡り、最後にResource属性へフォールバックする。`Resolved in design`、AC2。
- **明示補正による属性消失:** callerの辞書全置換では発見元を失う。キー単位上書きとする。`Resolved in design`、AC3。
- **空metadataが意図的な主体再取得:** 主体profileは名前を必須とするため空へ消去する操作は有効な補正ではない。通常登録の空metadata契約は変更しない。`Resolved in design`、AC3/AC4。
- **production復旧:** code修正だけでは対象を成功させないが、deploy/recoveryは明示的に除外されており、local実装のACを阻害しない。Operations ownerの別途許可事項として `Excluded follow-up`、AC5。

## Review gates

- **Design and task-split review — 2026-09-20, Main:** production API、CloudWatch、`JobDetail.razor`、`RequestCoreAsync`、`AcquireAsync`、既存metadata change recordを照合した。外部サイト条件ではなくTask生成時のmetadata空固定が直接原因である。Store境界の単一修正と永続化反例テストへ限定し、共有永続化ファイルとtestを同時に扱う短いtaskのためLeadが直列実行する。
- **Pre-implementation review — 2026-09-20, Main:** T1は`Runnable`。入力はAC1-AC5と既存Task不変性、成果はStore実装・3反例・focused suite。通常登録のmetadata規則、主体同定条件、production状態を変更しない。既存契約を変える必要が出た場合は`Proposed`へ戻す。
- **Checkpoint review — 2026-09-20, Main:** AC1-AC4をStoreの実経路で確認した。`ManualRefresh`/`Recovery`だけが同一Resource/Definitionの最新非空Task metadataを継承し、明示属性をキー単位で上書きする。通常登録と既存Taskスナップショットは変更しない。追加3反例を含むStore 95件とCollector 282件が成功したため採用する。
- **Final review — 2026-09-20, Main:** AC1-AC5と統合diffを再照合した。solution build、非ExternalのScraping/Collector/API回帰、format、diff/statusを通過し、production mutationと基点record変更がないことを確認した。T1と全ACを`Verified`、local scopeを`Implemented`とする。deployと対象Recoveryは承認範囲外の非blocking follow-upである。

## Verification record

- 2026-09-20 production read-only evidence: 元Task `f0bca3fd-41d6-4c61-ba24-9acdfa66aa63` は対象名と発見元metadataを保持し、replacement Task `60c50f67-a579-4312-bf9f-49eeaa43e1ad` はmetadataが空だった。replacementは `SubjectIdentification:MissingName` で外部遷移前に終了した。
- 2026-09-20 AWS read-only evidence: Lambda request `6dc5286d-2dfb-522c-9114-4b4457abe293` では当該Taskのhandler処理が約0.5msで `ResourceNotFound` となり、JRAアクセス起因ではないことを確認した。
- 2026-09-20: `ManualRefresh_WithoutMetadata_InheritsCompletedTaskSnapshot`、`Recovery_WithoutMetadata_SkipsLatestEmptySnapshot`、`Recovery_MetadataOverridesOnlySpecifiedKeys` を追加した。
- 2026-09-20: `dotnet test tests/HorseRacingPrediction.Collector.Tests/HorseRacingPrediction.Collector.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~CollectionPlatformStoreTests" -v:minimal` は95件成功。
- 2026-09-20: Collector非External 282件、Scraping非External 275件、API非External 267件成功・既存skip 1件。
- 2026-09-20: `dotnet build HorseRacingPrediction.sln --no-restore --configuration Release` は警告0・エラー0。`dotnet format HorseRacingPrediction.sln --no-restore --verify-no-changes` は成功。
- 2026-09-20: `scripts/validate_change_records.py` はrepositoryに存在しないため実行不能。単一Lead taskでagent audit対象ではなく、Status・AC・Task・remaining workの整合は最終diffで手動確認した。

## Remaining work

- localの承認済み実装範囲に残作業はない。
- production deployと対象resourceのRecoveryは本changeの外部フォローアップであり、明示的許可後にのみ行う。

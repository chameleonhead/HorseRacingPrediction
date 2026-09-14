# 収集ジョブ一覧を最新状態に集約する

- Status: Implemented
- Owner: Main
- Created: 2026-09-15
- Updated: 2026-09-15

## Context

本番の `/jobs?view=all` では、同じ収集対象・収集定義への再依頼ごとに新しいTaskが作成され、Task履歴がそのまま別行で表示されている。2026-09-15の確認では `Race / JRA / 20250216:Tokyo:11 / race-detail` が「失敗」「実行待ち」「失敗」の3行として同時に見え、各行は同じ対象・収集定義の詳細URLへ遷移した。

`SearchTasksAsync` は `Tasks` テーブルをTask単位で検索・件数集計し、`Jobs.razor` の `直近の処理` も返されたTaskをそのまま描画する。再依頼・手動更新・再収集は履歴保全のため別Taskを作るので、同じ対象が複数行になること自体は永続化上の意図した動作だが、現在状態を確認する一覧としては過去の失敗と新しい待機・成功が混在して分かりにくい。

## Goals

- 収集ジョブ一覧を、収集対象と収集定義の組ごとに最新Task 1件へ集約する。
- 状態タブ、検索結果、件数表示の意味を同じ最新スナップショットへ統一する。
- 過去Taskと試行履歴は詳細画面から引き続き確認できる。

## Non-goals

- 過去Task、失敗通知、試行履歴の削除・統合・移行は行わない。
- Taskの作成、重複抑止、再試行、配送優先順位は変更しない。
- `収集対象` タブが表示する永続的な収集状態モデルは変更しない。
- 障害の原因別集約と復旧フローは変更しない。

## Experience and interaction design

`直近の処理` は `最新の処理` に改称する。利用者が一覧を開くと、同じ対象・収集定義は1行だけ表示され、その行のバッジ、処理区分、優先度、試行回数は最後に作成されたTaskの値になる。失敗後に再依頼が作られていれば、最新Taskが実行待ちなら「待機中」、成功なら「最近完了」にだけ現れ、古い失敗Taskは一覧へ残らない。

フィルターは「まず各対象・収集定義の最新Taskを決め、その最新Taskへ条件を適用する」。したがって状態を「失敗」に絞っても、最新Taskが待機中または成功の対象は表示しない。エラー検索も最新Taskに属する試行だけを対象にする。ページ件数と各タブ件数も同じ集約後集合から算出し、表示行との食い違いを作らない。

Loading、更新中、空、通信エラーの既存表現は維持する。集約による新しい操作は増やさず、既存の検索・タブ・ページング・詳細導線・キーボード操作・狭幅reflowを維持する。

## Navigation and relationships

- 一覧行は従来どおり対象・収集定義の詳細へ遷移する。
- 詳細画面は同じ対象・収集定義の依頼履歴、Task履歴、試行履歴を保持し、一覧から除かれた過去Taskを確認する正規の導線とする。
- URL query の `view=all` は互換性のため維持し、表示ラベルだけを `最新の処理` に変える。

## Mocks

構造や操作を変えず、同じ一覧のデータ意味とラベルだけを変更するため、新しいワイヤーフレームは作成しない。

## Documentation updates

- `docs/20-admin-ui-design.md`: 収集ジョブ一覧を対象・収集定義ごとの最新Taskへ集約すること、集約後に既存の運用優先順位を適用すること、保存ビュー名を `最新の処理` とすることを正規の画面仕様へ追記した。
- `docs/22-collector-design.md`: Task生成・履歴保持の設計は変更しないため、この変更による更新は不要と判断した。作業ツリーにある既存の未コミット変更には触れない。

## Technical impact

- `CollectionTaskQuery` に最新Taskだけを対象とする検索指定を追加し、管理APIの `tasks/search` へ伝播する。
- `CollectionPlatformStore.SearchTasksAsync` は、Resource PKとDefinition IDごとに `CreatedAt` 降順、同時刻はTask ID降順で最新Taskを一意に選んだ後、状態・対象種別・提供元・処理区分・全文・エラー条件を適用し、集約後の総件数でページングする。
- `GetTaskViewCountsAsync` も同じ最新Task集合から `attention`、`running`、`waiting`、`recent`、`all` を算出する。`attention` のopenなfailure notification条件は維持する。
- 管理UIはタブ名・見出しを `最新の処理` に変更し、検索APIへ最新集約指定を渡す。
- 詳細取得と履歴API、Taskテーブルのスキーマは変更しない。

## Decisions

- 集約キーは `Resource(Type, Provider, ResourceId) + DefinitionId` とする。同じ対象でも収集内容が違うTaskは別行として残す。
- 「最新」は実行予定時刻の `AvailableAt` ではなくTask生成順を表す `CreatedAt`、同値時はTask IDで決める。再試行で変動する予定時刻を世代判定に使わない。
- 集約はUI上のページ内 `GroupBy` ではなく検索・件数を所有するStoreで行う。ページ境界をまたぐ重複と件数不一致を防ぐためである。
- 履歴を失わせる削除やDB移行は行わない。過去の経緯は詳細画面に集約する。
- `収集対象` はTaskの最新世代ではなくデータの取得状態を表すため、今回の置き換え先にはしない。

### Alternatives considered

- UIが取得した1ページだけを `GroupBy` する案は、別ページの重複を除けず、総件数とページ数も誤るため採用しない。
- 過去Taskを削除する案は、監査・失敗調査・試行履歴を失い、今回の表示改善に不要な破壊的変更になるため採用しない。
- `収集対象` タブだけを標準入口にする案は、Taskの待機・実行・優先度を確認する用途を満たさないため採用しない。

## Acceptance criteria

- **AC1 — 最新1行:** 同じResourceとDefinitionに複数Taskがある場合、`最新の処理` には `CreatedAt` が最新のTaskだけが1行表示される。同時刻の場合もTask IDにより結果が一意である。（T1, T2, T3）
- **AC2 — 最新状態で分類:** 過去Taskが失敗し、最新Taskが待機中・実行中・成功のいずれかである対象は、最新Taskに対応する保存ビューだけに含まれ、失敗状態フィルターには含まれない。（T1, T2, T3）
- **AC3 — 件数とページング:** タブ件数、検索結果件数、ページ数は最新Taskへ集約した後の件数と一致し、同じ対象・収集定義がページをまたいで重複しない。（T1, T2, T3）
- **AC4 — 検索:** 対象、提供元、Definition、処理区分、状態、エラーの条件は最新Taskだけに適用され、過去Taskだけが一致する対象を返さない。（T1, T2）
- **AC5 — 履歴保持:** 過去Taskと試行履歴は変更・削除されず、一覧行から従来の対象・収集定義詳細へ遷移して確認できる。（T1, T2, T3）
- **AC6 — 画面品質:** `view=all` の互換URL、既存のloading・更新中・空・通信エラー、キーボード操作、狭幅での情報保持を維持し、タブと見出しは `最新の処理` と表示する。（T2, T3）

## Delivery plan

1. Store検索契約と集約クエリ、集約後のタブ件数を実装し、複数世代・状態・検索・ページ境界をテストする。
2. 管理API clientとBlazor一覧を最新検索へ接続し、表示ラベルとcomponent testを更新する。
3. 関連テスト、solution build、format、実データのデスクトップ・狭幅ブラウザー確認を行い、結果を本記録へ追記する。

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | 最新Task検索と件数集約 | Main | High capability | Approval | Collection models/store, endpoint, store/endpoint tests | focused store and endpoint tests | AC1–AC5を覆うテスト結果 | Verified |
| T2 | 管理UIを最新検索へ接続して改称 | Main | High capability | T1 | AdminApiClient, Jobs.razor, component tests | focused bUnit tests | AC1–AC6の画面出力とAPI request | Verified |
| T3 | 統合・回帰・実ブラウザー検証 | Main | High capability | T1, T2 | change record verification only | format, build, relevant tests, browser desktop/narrow | コマンド結果と観測記録 | Verified |

各Taskの完了条件は上記AC参照とし、T1/T2は共有契約を順に変更するため並列化しない。設計と外部契約の判断を含む小規模な直列変更であり、委譲による調整コストが上回るためMainが実施する。

## Review gates

- **Design and task-split review — 2026-09-15 / Main:** 実画面、`Jobs.razor`、task search API、Store検索・件数、既存UI設計を確認した。AC1–AC6はT1–T3とテスト・ブラウザー検証に対応し、集約キー、最新判定、filter適用順、履歴保持、互換URLに未決定事項はない。共有契約を直列化しMainが担当する案を適切と判断した。
- **Pre-implementation review — 2026-09-15 / Main:** ユーザー承認を受け、T1を`Runnable`、T2/T3を`Dependent`とした。T1はmodels/store/endpointとfocused tests、T2はclient/UI/component tests、T3は検証記録だけを順に所有し、同時書込はない。エスカレーション条件は、最新TaskをDB検索で一意に選べない制約、承認済みfilter意味の変更、履歴削除・schema migrationの必要が判明した場合とする。
- **Checkpoint review — 2026-09-15 / Main:** T1の初回ビルドで分岐の挿入位置と匿名型の型安全性に問題を検出した。旧取得APIを変更しない正しい位置へ移し、`TaskSearchRow`で型を固定した後、Release build、Store 63件、API/UI 22件が成功した。T1を`Verified`、T2を`Runnable`として統合を継続した。
- **Final review — 2026-09-15 / Main:** T1–T3の差分、CodeGraph接続、focused/full tests、実ブラウザー結果をAC1–AC6と照合した。全Taskは`Verified`で、承認範囲の未完了・棄却・外部blockerはない。既存のユーザー変更 `docs/22-collector-design.md` と `docs/changes/20260914_collection-task-metadata/` は変更・混在させていない。委譲なしの直列実装で再試行1回を要したが、focused gateで検出・修正され、routing変更を要する反復的失敗ではない。

## Verification record

- 2026-09-15 設計前調査: 本番 `/jobs?view=all` で同一 `Race / JRA / 20250216:Tokyo:11 / race-detail` の3行（失敗、実行待ち、失敗）と同一詳細URLを確認した。
- 2026-09-15 コード調査: `SearchTasksAsync` と `GetTaskViewCountsAsync` がTask全件を単位に検索・集計し、`Jobs.razor` が `view=all` でそのページをそのまま描画することを確認した。
- 2026-09-15 focused Store test: `dotnet test tests/HorseRacingPrediction.Collector.Tests/HorseRacingPrediction.Collector.Tests.csproj --no-build --configuration Release --filter "FullyQualifiedName~CollectionPlatformStoreTests"` — 63件成功。古い失敗後の最新待機Task、最新状態filter、古いerror除外、履歴保持、集約件数を含む。
- 2026-09-15 focused API/UI test: `dotnet test tests/HorseRacingPrediction.Api.Tests/HorseRacingPrediction.Api.Tests.csproj --configuration Release --filter "FullyQualifiedName~CollectionOperationsEndpointTests|FullyQualifiedName~CollectionAdministrationComponentTests"` — 22件成功。`latestOnly=true`の接続、`最新の処理`ラベル、既存画面状態と導線を含む。
- 2026-09-15 format: `dotnet format HorseRacingPrediction.sln --no-restore --verify-no-changes` — 成功、変更なし。
- 2026-09-15 build: `dotnet build HorseRacingPrediction.sln --no-restore --configuration Release` — 成功、警告0、エラー0。
- 2026-09-15 full non-External test: `dotnet test HorseRacingPrediction.sln --no-build --configuration Release --filter "TestCategory!=External"` — 948件成功、既存1件skip、失敗0。
- 2026-09-15 CodeGraph: `codegraph sync .` 成功後、`CollectionTaskQuery.LatestOnly` がAdminApiClient、Jobs、Storeへ接続され、`GetTaskViewCountsAsync` が管理endpointから呼ばれることを再確認した。
- 2026-09-15 browser desktop: ローカル `/jobs?view=all` で `最新の処理 10`、見出し `最新の処理`、一覧10行、同一詳細hrefの重複0件を確認した。
- 2026-09-15 browser narrow 390x844: メニューへのreflow後も選択tabpanel、検索、全filter、件数、10行の対象・状態・meta・詳細導線がアクセシビリティツリーに保持されることを確認した。一覧行から詳細へ遷移し、戻り先が `/jobs?view=all`、詳細内に依頼履歴1件・Task履歴1件が表示されることを確認した。検証後にviewport overrideを解除した。

## Deviations and follow-up

- 承認済み設計との差分はない。
- 初回ビルドで最新検索分岐を同形の旧取得メソッドへ誤挿入し、匿名型をdynamicで扱う型エラーが発生した。正しい`SearchTasksAsync`へ移し専用型へ変更して解消し、その後のfocused/full gateはすべて成功した。
- 本番環境へのデプロイはこのchange setに含めない。

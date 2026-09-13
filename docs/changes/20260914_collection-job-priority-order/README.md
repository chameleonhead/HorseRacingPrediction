# 収集ジョブ一覧を運用優先順位で並べる

- Status: Implemented
- Owner: Codex
- Created: 2026-09-14
- Updated: 2026-09-14

## Context

収集ジョブ一覧は現在、数値優先度の降順、Task ID の昇順でページングされる。実行基盤はそれより強い処理区分の順位を持ち、`Realtime`、`Normal`、`Background` の順で候補を選ぶ。同じ処理区分では数値優先度を高い順に評価する。一方、成功して完了したジョブは実行候補ではないため、未完了ジョブより前に表示する運用上の理由はない。

利用者が一覧から優先ジョブを読み取れるよう、一覧もこの安定した運用優先順位に合わせる。ただし、実配送には待機時間による aging と、リアルタイム処理が連続した後にバックグラウンド処理を1件選ぶ飢餓防止がある。これらは時刻と直前の配送履歴により変動するため、一覧の静的な並び順には含めない。

また、要対応ビューの「障害のまとまり」は、現在カードを複数列へ自動配置し、原因、ジョブ種類、件数、最新メッセージを同じカード内へ詰めている。PCでは列ごとの位置が揃わず比較しづらく、携帯では狭いカード内で長い原因名と件数が競合し、最新メッセージが1行省略されるため内容を把握しづらい。

## Goals

- 収集ジョブ一覧を、完了状態、処理区分、数値優先度、ジョブ種類の順で一貫して並べる。
- フィルターや複数ページにまたがる場合も、全結果に対する正しい順序を保つ。
- 通常検索経路と作成日時フィルター経路で同じ順序を使う。
- 障害グループの原因、ジョブ種類、件数、最新内容、詳細導線を、PCと携帯の双方で走査しやすくする。

## Non-goals

- dispatcher の候補選択、aging、飢餓防止規則を変更すること。
- 利用者が任意の並び順を選ぶソート UI を追加すること。
- 収集ジョブの処理区分または数値優先度を変更すること。
- データ取得状況ビュー、障害グループ、ジョブ詳細内の履歴の並び順を変更すること。
- 障害グループの集約条件、最大表示件数、詳細ページ、復旧処理を変更すること。

## Experience and interaction design

`/jobs` の収集ジョブ各ビュー（要対応、処理中、待機中、完了、すべて）では、フィルター適用後のジョブを次の順で表示する。

1. 完了状態: 未完了、完了済み（`Succeeded`）
2. 同じ完了状態内の処理区分: リアルタイム、通常、バックグラウンド
3. 同じ完了状態かつ処理区分内の数値優先度: 高い順
4. 同順位内のジョブ種類: Definition ID の昇順
5. 上記が同じ場合: Task ID の昇順（安定したページングのための非表示タイブレーク）

ジョブ種類は収集定義を一意に表す Definition ID（例: `race-detail`、`race-odds`）を指す。画面に表示する日本語名は表示専用とし、並び順には使用しない。処理区分、数値優先度、日本語表示名の表示内容は変更しない。

「障害のまとまり」は[レスポンシブ・ワイヤーフレーム](mocks/failure-groups-responsive-wireframe.md)に従い、画面幅で情報を消すのではなく再配置する。

- PC: 複数列カードをやめて1列の比較可能な行とし、原因、ジョブ種類、件数、最新発生、最新内容、詳細導線を同じ列位置へ揃える。最新内容だけを1行省略する。
- 携帯: 1グループを縦積みし、先頭行に原因と件数、次にジョブ種類と最新発生、最大3行の最新内容、最後に「詳細を見る」を配置する。
- 行／カード全体をリンクとして維持し、キーボードfocusと既存の状態色を保つ。色だけに頼らず、件数と文言で障害を識別できるようにする。
- 長いエラーコードや空白のないメッセージでもコンテナを越えず、横スクロールを発生させない。
- 現行どおり先頭6グループを件数順で表示し、7件以上ある場合は省略を明示する「すべての障害（N原因）を見る」導線を既存の障害一覧へ表示する。

## Documentation updates

- `docs/20-admin-ui-design.md`: `/jobs` の正本仕様へ、完了状態、処理区分、数値優先度、ジョブ種類、安定タイブレークからなる一覧順序を追加する。
- `docs/26-collection-platform-design.md`: 実配送の動的な公平配分と、管理一覧の安定した運用順位の境界を追記する。
- `docs/20-admin-ui-design.md`: 障害グループ一覧のPC／携帯それぞれの情報階層とレスポンシブ規則を追加する。
- `docs/changes/20260914_collection-job-priority-order/mocks/failure-groups-responsive-wireframe.md`: PCと携帯の提案レイアウトおよび各表示状態を定義する。

## Technical impact

- 一覧の順序は API のページング前に `CollectionPlatformStore.SearchTasksAsync` で確定する。Blazor 側だけの並べ替えはページをまたぐ順序を保証できないため採用しない。
- `Succeeded` だけを完了済みとして未完了ジョブより後ろに置く。`Failed`、`DeadLetter`、`Cancelled` は成功完了ではないため、完了済みグループへ含めない。
- 処理区分の順位は enum の宣言順への暗黙依存ではなく、実行基盤と同じ明示的な順位（Realtime=0、Normal=1、Background=2）を用いる。
- 数値優先度は enum の列挙順ではなく保存済み整数を降順にする。現行ポリシーは 100、90、80、70、50、30、10 を使用し得る。
- ジョブ種類は永続化済みの Definition ID を序数比較で昇順にし、日本語表示名の変更やローカライズから独立させる。
- SQLite で翻訳できない並べ替えが必要な経路は、SQL でフィルターした候補へ同一の並べ替えを適用してから `Skip` / `Take` する。件数取得とページ境界を回帰テストする。
- 障害グループのDTOとAPIは変更せず、`Jobs.razor` の意味構造とscoped CSSをレスポンシブに組み替える。Fluentの色・focus表現を維持し、独自CSSはgrid、spacing、折返し、省略などレイアウト用途に限定する。

## Decisions

- 「優先度の高い順」は、実行基盤の安定した優先属性に合わせて、処理区分を数値優先度より先に評価する。
- 成功済みジョブは再実行候補ではないため、処理区分と数値優先度を評価する前に未完了／完了済みへ分け、完了済みを後ろに置く。これにより「直近の処理」など状態が混在するビューで作業対象を先に確認できる。
- 一覧は aging とリアルタイム連続数による一時的なバックグラウンド強制選択を再現しない。管理画面を更新するたびに順序が変動し、次回配送順とも一致を保証できないためである。
- 同順位の「種類順」は、画面の日本語名ではなく Definition ID の昇順とする。
- 大文字小文字や文化設定による環境差を避け、Definition ID は序数比較で昇順にする。
- 障害グループはPCでもカードを横に並べず、1列の比較行を採用する。携帯では同じ情報を縦積みにreflowし、重要情報を非表示にしない。既存DTOの `LastFailedAt` を「最新発生」として表示する。
- 最新メッセージの全文調査と復旧は既存の障害グループ詳細ページに委ね、一覧はPCで1行、携帯で最大3行のプレビューにする。
- 先頭6件という要約密度は維持し、7件以上のときだけ既存 `/jobs/operations?tab=failures` への全件導線を表示する。

## Acceptance criteria

- **AC-1 (T2, T3):** `/jobs` のジョブ一覧で、`Succeeded` の完了済みジョブは処理区分と数値優先度にかかわらず未完了ジョブより後ろに表示される。失敗、デッドレター、中断は完了済みとして扱わない。
- **AC-2 (T2, T3):** 同じ完了状態内では、リアルタイム、通常、バックグラウンドの順に表示される。バックグラウンドの数値優先度がリアルタイムより高くても、この処理区分順位を優先する。
- **AC-3 (T2, T3):** 同じ完了状態かつ同じ処理区分では保存済み数値優先度の降順となり、100、90、80 のような enum 外の中間値も数値どおりに並ぶ。
- **AC-4 (T2, T3):** 同じ完了状態、処理区分、数値優先度では、ジョブ種類を表す Definition ID の昇順となり、日本語表示名は並び順に影響しない。
- **AC-5 (T2, T3):** 全キーが同じジョブは Task ID の昇順となり、ページを再読込しても順序とページ境界が安定する。
- **AC-6 (T3):** 通常検索と作成日時フィルターの両経路、および2ページ以上の結果で AC-1〜AC-5 が成立する。
- **AC-7 (T3):** dispatcher の lane、aging、飢餓防止に関する既存テストが通り、実行順位の挙動が変わらない。
- **AC-8 (T4, T5):** PC幅では障害グループが1列の比較行として表示され、原因、ジョブ種類、件数、最新発生、最新内容、詳細導線がグループ間で揃って走査できる。
- **AC-9 (T4, T5):** 幅390px相当では、原因と件数、ジョブ種類、最新発生、最大3行の最新内容、「詳細を見る」が縦積みになり、横スクロールやコンテナ外へのはみ出しがない。
- **AC-10 (T4, T5):** 長いエラーコード、長い空白なしメッセージ、原因未分類、2桁以上の件数でも主情報と詳細リンクを識別できる。
- **AC-11 (T4, T5):** 障害グループの行／カード全体をマウス、タッチ、キーボードで開け、focusが視認でき、色だけで状態を表さない。
- **AC-12 (T5):** 障害なし、Loading、API Errorの既存表示が維持され、障害の集約・最大6件・詳細遷移・復旧動作が変わらない。7原因以上では総原因数を含む全件導線が表示され、6原因以下では表示されない。

## Delivery plan

1. 承認後、タスクと受け入れ基準を再確認し、実装対象を `Runnable` にする。
2. サーバー側で完了状態、処理区分、数値優先度、Definition ID、Task ID の順をページング前に適用する。
3. store、API/UI、dispatcher の関連テストを実行し、CodeGraph を同期して実経路を再確認する。
4. 障害グループのレスポンシブ構造とscoped CSSを実装し、component testとPC／携帯ブラウザーで反復確認する。
5. 検証結果と差分を本記録へ追記し、最終レビュー後に `Implemented` とする。

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | 設計、正本文書、受け入れ基準を確定する (AC-1〜AC-12) | Main | Lead tier | - | 本 change record、`docs/20-admin-ui-design.md`、`docs/26-collection-platform-design.md` | 文書差分と設計レビュー | 2026-09-14 ユーザー承認 | Verified |
| T2 | サーバー側の複合順序を実装する (AC-1〜AC-5) | Main | Lead tier | T1 | CollectionOperations の関連ファイル | focused build、CodeGraph 再照会 | `SearchTasksAsync`両経路と順序テスト | Verified |
| T3 | 通常・日時フィルター・ページ境界・dispatcher 非回帰を検証する (AC-1〜AC-7) | Main | Lead tier | T2 | 関連テスト、検証記録 | focused tests、format、diff/status | focused 62件、全非Externalテスト合格 | Verified |
| T4 | 障害グループ一覧をPC／携帯向けにreflowする (AC-8〜AC-11) | Main | Lead tier | T1 | `Jobs.razor`、`Jobs.razor.css` | component test、PC／携帯browser verification | responsive DOM/CSSと長文fixture | Verified |
| T5 | 長い値、各状態、操作性、既存動作を検証する (AC-8〜AC-12) | Main | Lead tier | T4 | 関連UIテスト、検証記録 | focused tests、browser verification、format | component 10件、390px/1280px観測 | Verified |

## Review gates

- **Design and task-split review (2026-09-14, Main):** `CollectionLaneAllocator`、`CollectionPlatformOutboxDispatcher`、`CollectionPlatformStore.SearchTasksAsync`、`Jobs.razor`、関連CSS・テスト・正本文書を確認した。成功完了は `Succeeded` とし、状態が混在する一覧では未完了を先にする。未完了／完了済みの各グループで処理区分順位を Realtime、Normal、Background とし、数値優先度には 90 と 80 の中間値が存在する。同順位はユーザー確認により日本語表示名ではなく Definition ID の種類順とした。障害グループはPCの複数列カードと携帯の1行省略が走査性を損ねているため、PCは1列の比較行、携帯は情報を保持した縦積みとした。AC-1〜AC-12 は T2〜T5 と検証へ双方向に対応する。調査だけをread-only workerへ委譲し、書込競合はなかった。UI判断と実装は Main が直列で担当する。Decision: approval requestへ進める。Follow-up: 承認後にpre-implementation reviewを記録する。
- **Pre-implementation review (2026-09-14, Main):** ユーザーがAC-1〜AC-12を含む変更記録を明示的に承認した。T1は`Verified`、T2とT4は`Runnable`、T3はT2へ、T5はT4へ依存する`Dependent`とした。T2の入力は`SearchTasksAsync`の通常／日時フィルター両経路、T4の入力は`Jobs.razor`とscoped CSSおよび承認済みwireframeとする。期待証拠はfocused build/test、PC／携帯browser観測、CodeGraph再照会。承認済み外へのAPI・dispatcher・集約条件変更が必要になった場合は停止して再設計する。Decision: production implementationを開始する。
- **Checkpoint review (2026-09-14, Main):** `SearchTasksAsync`の両分岐が完了状態、明示lane順位、整数priority、Definition ID、Task IDの順でページング前に並ぶことをdiffと新規store testで確認した。障害グループは承認済みwireframeどおり1列比較行／携帯縦積みとなり、既存DTO以外のAPI変更はない。focused store 62件、component 10件が合格。Decision: 全体回帰とブラウザー検証へ進む。
- **Final review (2026-09-14, Main):** T1〜T5はすべて`Verified`。AC-1〜AC-7は通常／日時フィルター／複数ページを横断するstore testと全テストで確認し、dispatcher実装は未変更。AC-8〜AC-12は長い値・7原因fixtureのcomponent test、既存のempty/error test、390x844および1280pxのlocalhost観測で確認した。両viewportでdocument horizontal overflowはfalse。実DBには障害グループがなかったため、実ブラウザーの障害行そのものはfixtureによるcomponent検証を証拠とし、ページ全体のresponsive reflowとoverflowを実ブラウザーで確認した。CodeGraph同期・再照会、Release build、format、diff/statusを確認し、未完了タスクや受け入れ阻害blockerはない。Decision: `Implemented`へ進める。

## Implementation result

- 収集ジョブ検索結果を、未完了、lane、priority降順、Definition ID、Task IDの順にサーバー側で並べ、通常／作成日時フィルターの双方でページング前に適用した。
- 障害グループをPCでは1列の比較行、携帯では縦積みにし、最新発生、明示的な詳細導線、7原因以上の全件導線を追加した。
- 集約条件、最大6件の要約、詳細・復旧API、dispatcherの動的優先制御は変更していない。

## Acceptance-criterion matrix

| Criteria | State | Evidence |
| --- | --- | --- |
| AC-1〜AC-6 | Verified | `SearchTasks_OrdersIncompleteBeforeSucceeded_ThenLanePriorityAndDefinitionAcrossPages`（通常、日時、2ページ） |
| AC-7 | Verified | 全非Externalテスト合格、dispatcher production diffなし |
| AC-8〜AC-11 | Verified | failure group component test、responsive DOM/CSS、390px/1280px browser観測 |
| AC-12 | Verified | 既存empty/error component tests、7原因fixtureの全件導線条件 |

## Verification record

- 設計調査: `codegraph explore` で `CollectionLaneAllocator`、`CollectionPlatformOutboxDispatcher`、`SearchTasksAsync`、`Jobs.razor` の経路を確認。
- リポジトリ検索: `CollectionPriority`、`LaneLabel`、`DefinitionLabel`、関連テストと正本文書を確認。
- 調査時点の `git status --short`: 変更なし。
- `dotnet build src/HorseRacingPrediction.Api/HorseRacingPrediction.Api.csproj --no-restore`: 成功、警告0、エラー0。
- `dotnet test tests/HorseRacingPrediction.Collector.Tests/HorseRacingPrediction.Collector.Tests.csproj --no-restore --filter "FullyQualifiedName~CollectionPlatformStoreTests"`: 62/62合格。
- `dotnet test tests/HorseRacingPrediction.Api.Tests/HorseRacingPrediction.Api.Tests.csproj --no-restore --filter "FullyQualifiedName~CollectionAdministrationComponentTests"`: 10/10合格。
- `dotnet format HorseRacingPrediction.sln --no-restore --verify-no-changes`: 成功。
- `dotnet build HorseRacingPrediction.sln --no-restore --configuration Release`: 成功、警告0、エラー0。
- `dotnet test HorseRacingPrediction.sln --no-build --configuration Release --filter "TestCategory!=External"`: 全プロジェクト成功（Api 201合格/1スキップ、Scraping 222合格、Collector 184合格、その他288合格）。
- localhost `/jobs` browser verification: 390x844と1280pxで表示し、両方で`documentElement.scrollWidth > innerWidth`がfalse。390pxではtoolbarが1列へreflowし、1280pxではPC配置を確認。
- `codegraph sync .`: 完了（indexは既に最新）。`codegraph explore "SearchTasksAsync completed lane priority definition ordering Jobs failure-group responsive"`で検索APIからstore、およびJobs failure group経路を再確認。
- `git diff --check`: 成功。

## Deviations and follow-up

- 実DBに障害グループがなかったため、長い障害行と7原因以上の実ブラウザー観測は行わず、同じBlazor componentを7原因fixtureでrenderするcomponent testでDOMと内容を検証した。実ブラウザーではPC／携帯のページ全体のreflowとoverflowを検証した。

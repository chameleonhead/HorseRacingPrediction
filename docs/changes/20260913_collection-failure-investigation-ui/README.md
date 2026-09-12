# 収集障害の調査・一括復旧UI

- Status: Implemented
- Owner: HorseRacingPrediction maintainers
- Created: 2026-09-13
- Updated: 2026-09-13

## Context

本番の収集管理では、出馬表などの障害が多数発生した際に次の問題がある。

- URLを明示して依頼しても、Resource詳細の依頼履歴では「あり」としか表示されず、そのURLを確認できない。
- 試行履歴の横長な表へ例外名、本文、URLが詰め込まれ、原因と次の行動を判断しにくい。
- 「障害のまとまり」は代表の1 Resourceへ遷移するため、同じ原因に属する他の対象を確認できない。
- 原因単位の全件再取得は収集運用画面にあるが、対象を確認・選択してから再取得できず、Jobs画面からの導線も代表詳細へのリンクに見える。
- 個別Resourceを順番に開いて再取得する運用は、多数障害時に操作量が大きい。

2026-09-13の本番確認では、`race-card / JraCollectionException` 27件、`race-result / PlaywrightException` 53件などが同時に発生しており、代表リンクだけでは影響範囲を調査できなかった。

## Goals

- URL指定値、実際の要求URL、最終到達URL、保存済み候補を用途別に確認できる。
- 最新障害について「何が起きたか」「想定原因」「推奨する対応」を先に読め、例外本文や呼出履歴は必要時だけ展開できる。
- 障害グループから専用詳細ページへ移動し、グループ内の全Resourceをページングして確認できる。
- グループ全件または選択したResourceを、確認後に通常のRecovery経路へ一括投入できる。
- 再取得後は既存のFailure lifecycleに従い、対象が要対応から対応中へ移る。

## Non-goals

- 例外本文や試行履歴を削除・書き換えない。
- UIから任意SQLや任意条件式を実行させない。
- 再取得専用Workerや別キューを作らない。
- エラー分類そのものの網羅的な再設計は行わない。未知のエラーも技術詳細付きで表示可能にする。

## Experience and interaction design

### Resource詳細

Resourceが要対応の場合、ページ上部の概要とタブの間に「最新の障害」セクションを表示する。

1. 日本語の短い要約
2. 発生日時、試行回数、HTTP状態、ページ判定
3. 推奨する対応
4. 実際に使用したURL（要求URLと最終URLが異なる場合は両方）
5. 「技術的な詳細を表示」で例外名、原文、呼出履歴、相関IDを展開

URLは省略表示してもリンク先とコピー対象は完全な値を保持し、外部リンクとして新しいタブで開ける。依頼履歴のExplicit URLも「あり」ではなく、完全なURLを確認できるリンクとして表示する。値がない項目は「記録なし」とし、URLを推測しない。

試行履歴の主表は「回、結果、開始、処理時間、HTTP、操作」に絞り、「詳細を見る」で同じページ内の試行詳細を表示する。狭幅では横長グリッドを強制せず、主要情報と詳細操作を保持する。

### 障害グループ

Jobs画面と収集運用画面のグループ行は代表Resourceではなく、`/jobs/failures/{groupKey}` の独立した詳細ページへ遷移する。

グループ詳細は次の順で表示する。

- パンくず、収集内容、エラー分類、未解決件数
- 日本語の原因要約、推奨対応、共通する技術詳細
- Primary Action「対象をまとめて再取得」
- 検索・対象種別・開催日等の絞り込み
- チェックボックス付き対象一覧（Resource、発生日時、試行回数、最新URL、個別詳細）
- ページング

「対象をまとめて再取得」は現在のグループに属するOpen障害全件を対象とする。行を選択した場合は「選択した対象を再取得」を使用できる。実行前Dialogで対象件数、収集内容、処理区分、優先度、重複抑止を説明し、確定後は作成・既存Task件数を表示する。

対象はページ表示中の行だけでなく、サーバー側の同一グループ条件を正として選択する。一括操作中に解決済みへ変化した通知は安全に除外し、既にActiveなTaskは既存Taskへまとめる。

### エラー説明

アプリケーション側にエラー説明カタログを置き、既知のErrorCodeを次のような運用文へ変換する。

- `JraCollectionException`: 期待する情報をページから取得できなかった。URL、ページ判定、対象ID一致を確認する。
- `UnexpectedPage`: HTTP成功でも別ページを検出した。最終URLとページ判定を確認し、URL再探索を伴う再取得を行う。
- `TargetClosedException`: ブラウザーまたはページが処理中に終了した。実行バッチ内の他障害とLambdaの資源状況を確認する。
- `PlaywrightException` の資源不足: 同一バッチ、Lambdaメモリ、ブラウザー起動数を確認する。

未知のErrorCodeは「分類されていない障害」と表示し、原文とURLを失わない。

## Navigation and relationships

```text
収集管理 / 要対応
  ├─ Resource行 ──────────────> Resource詳細
  └─ 障害のまとまり ─────────> 障害グループ詳細
                                  ├─ Resource詳細
                                  ├─ 実行バッチ詳細
                                  ├─ 選択対象を再取得
                                  └─ グループ全件を再取得
```

## Mocks

- [デスクトップ・狭幅ワイヤーフレーム](mocks/failure-investigation.md)

## Documentation updates

- `docs/changes/20260911_unified-collection-platform/admin-ui-implementation-note.md`: Resource詳細のURL・障害要約と、障害グループ専用詳細ページ、一括Recoveryの標準導線を追記する。この変更後も同文書を統一収集基盤UIの実装規約とする。
- `docs/20-admin-ui-design.md`: 現行と旧ジョブ基盤の記述が混在する大規模文書のため、本変更では全面改訂せず、本change recordと上記implementation noteを新収集基盤の正本として参照する。

## Technical impact

- Failure group詳細取得APIを追加し、group metadataとOpen通知のページング済み対象を返す。
- group keyから、Definition、Task status、ErrorCodeという既存のグループ条件をサーバー側で再解決する。存在しない・全件解決済みの場合は説明付きnot-found/emptyを返す。
- 一括Recovery APIは既存のnotification ID指定を維持しつつ、group全件をサーバー側で選択できる契約を追加する。大量件数をクライアントへ全件ロードしてから再送しない。
- Resource詳細ReadModelは既に保持するExplicitUrl、RequestedUrl、FinalUrl、ResourceLocationをそのまま利用し、欠損値を捏造しない。
- `RaceOpsErrorDetail`を、要約、推奨対応、技術詳細を一貫表示する共有パターンへ拡張する。
- Recoveryは既存CollectionRequest、SQS、Lambda、Active重複防止、Failure resolution遷移を使用する。
- グループ詳細には実行バッチへの導線を含め、同じLambda実行で発生した障害か、個別実行の累積障害かを区別できるようにする。

## Decisions

- グループ調査はDialogではなく再訪可能な独立ページにする。対象数、検索、ページング、複数の関連リンクを扱うためである。
- Jobsの障害グループ自体を即時実行ボタンにはしない。まず影響範囲を確認し、詳細ページで全件または選択対象を再取得する。
- エラー原因は例外原文だけで断定せず、「確認できた事実」「想定原因」「推奨対応」を分ける。
- URLはResource identityではなく取得証跡として表示する。URL表示を追加してもResourceKey中心の設計は変更しない。
- 全件Recoveryはページングに依存しないサーバー側選択とする。
- チェック選択はページを移動しても保持し、解決済みになった通知は確定時に除外する。「すべて選択」は表示中ページだけを選択するものと明記し、グループ全件操作と混同させない。
- group keyの衝突や解決済みによる消滅を暗黙に別グループへ結び付けない。サーバーが一意に解決できない場合はConflict、Open対象がなくなった場合は説明付きemptyとして扱う。
- URLのコピーは共有の小さな操作として実装し、Clipboard APIが拒否された場合はURL本文を選択できる状態とエラーフィードバックを残す。

## Pre-implementation self-review

- Primary Action: グループ詳細の「対象をまとめて再取得」に限定し、選択対象の再取得は選択後だけ有効なSecondary Actionとする。
- 操作数: Jobsのグループから1遷移で対象・原因・URLを確認でき、全件Recoveryは詳細→確認→確定の3操作以内とする。
- 情報密度: Resource詳細の横長試行表から例外全文とURLを外し、最新障害を先に読む構造へ変更する。技術情報は削除せず展開領域へ移す。
- Dialog/Page: 調査は検索・ページング・再訪を伴うため独立Page、確定だけを短いDialogとする判断に問題はない。
- 大量対象: クライアントが全notification IDを取得する案は1000件超で破綻するため退け、group全件はサーバー側選択にする。選択操作はIDだけを保持する。
- 競合: 画面表示後に解決した対象、既存Active Task、二重送信を受け入れ基準9で扱う。確認Dialogの確定中は操作を無効化する。
- レスポンシブ: desktopはFluentDataGrid、狭幅は同じ情報順の一覧へreflowする。横スクロールだけに依存しない。
- アクセシビリティ: 行全体クリックに依存せず、Resource詳細リンク、ラベル付きFluentCheckbox、標準Dialog、展開summaryを使用する。
- 既存整合: Resource詳細、CollectionOperations、Failure lifecycle、既存Recovery APIを再利用し、新しいWorkerやキューは作らない。
- 結論: 実装を妨げる未解決の仕様問題はない。ユーザーの2026-09-13承認に基づきExecution Modeへ移行する。

## Acceptance-criterion matrix

| AC | 状態 | 実装・検証先 |
|---|---|---|
| 1–3 | Verified | Resource詳細にExplicit/要求/最終URL、最新障害要約、技術詳細を実装。API buildとcomponent testで検証 |
| 4–8 | Verified | group API/page、全件・選択Recovery、Jobs/収集運用の導線を実装。endpoint/component testsで検証 |
| 9–10 | Verified | Open再解決、Active Task再利用、0件/衝突/最大10,000件、検索・ページングをendpoint testsで検証 |
| 11–12 | Verified | Fluent標準操作、label付き選択、Dialog、details、狭幅reflowを実装し、component renderとローカル認証済み画面で導線を確認 |
| 13–14 | Verified | 既知エラー・URLのcomponent test、相関情報をgroup/Resource詳細と実行バッチリンクへ表示 |

## Acceptance criteria

1. Explicit URLで依頼したResource詳細に完全な指定URLが表示され、開く・コピーできる。
2. 試行済みResourceではRequested URLとFinal URLが区別され、値がない場合は「記録なし」と表示される。
3. 要対応Resourceの上部に最新障害の日本語要約、発生情報、推奨対応、URLが表示され、技術原文は展開して確認できる。
4. Jobsと収集運用の障害グループから、代表Resourceではなく同じグループ詳細ページへ遷移する。
5. グループ詳細で未解決対象をページング・検索でき、各Resource詳細へ移動できる。
6. グループ全件を1回の確認操作でRecoveryへ依頼でき、表示ページ外の対象も含まれる。
7. 選択した複数対象だけをRecoveryへ依頼できる。
8. Recovery受付後、対象は要対応から対応中へ移り、作成Task数と既存Task再利用数が表示される。
9. 実行直前に解決済みとなった通知、重複配信、既存Active Taskがあっても二重実行や不正な状態遷移が起きない。
10. 0件、通信失敗、長いURL、長い例外、未知ErrorCode、1000件超のグループに説明付き状態と安全な操作がある。
11. デスクトップで主要情報を走査でき、320 CSS pxでもページ全体の横overflowなしに原因、対象、主要操作へ到達できる。
12. キーボードだけでグループ遷移、対象選択、確認Dialog、再取得確定、技術詳細展開ができ、focusが視認できる。
13. component testでURL表示、既知・未知エラー、全件・選択Recovery、empty/error状態を検証し、browser testでナビゲーション、Dialog、狭幅を検証する。
14. Playwright資源不足では実行バッチ、Lambda request ID、バッチ内件数を表示し、同一実行内の関連障害へ移動できる。

## Delivery plan

1. group詳細・ページング・group server-side recoveryのReadModel/APIを追加する。
2. エラー説明カタログとResource詳細の最新障害・URL表示を追加する。
3. 障害グループ詳細ページとJobs/収集運用からの導線を実装する。
4. 全件・選択一括Recovery、確認・完了・競合時フィードバックを実装する。
5. Store/API/component/browserの正常・異常・大量データテストを追加する。
6. デスクトップと320px相当で実データを操作し、最大の使い勝手上の問題を修正して再検証する。
7. 本番デプロイ後、出馬表障害グループでURL・原因・複数対象・一括Recoveryを確認する。

## Verification record

- 2026-09-13: CodeGraphでJobs、CollectionOperations、JobDetail、Failure group API、Resource詳細ReadModelの接続を調査した。既存データにはExplicitUrl、RequestedUrl、FinalUrl、Locationが保存されているが、依頼履歴はURLを「あり」とだけ表示し、試行履歴はURLと例外全文を横長列に表示している。
- 2026-09-13: 本番で障害グループが代表Resourceへのリンクであること、出馬表27件等の他対象を同画面から確認できないこと、原因説明・推奨対応・URLが一つの調査単位として表示されないことを確認した。
- 2026-09-13: CloudWatch Logsとデプロイ時刻を照合した。`ERR_INSUFFICIENT_RESOURCES` 53件が発生した15:20 UTCは、microbatch Lambdaの反映完了15:25:51 UTCより前だった。導入前15:15–15:22:30は108 invocation、2,048MB中最大1,939.8MB、平均報告最大1,939.8MB、平均2.24秒だった。導入後15:26:30以降は8 invocation、最大1,149.2MB、平均報告最大1,005.4MB、平均118.74秒で、複数Taskを同一browser sessionへ集約した結果と整合する。Lambdaはreserved concurrency 1、SQS batch size 1（messageは複数Taskを含むenvelope）、memory 2,048MB、timeout 900秒である。
- 2026-09-13: 資源不足の直接要因は、旧方式で短いLambda invocationごとにChromiumを繰り返し起動し、warm execution environment内の報告最大メモリが上限の94.7%へ達したことと判断した。microbatch後の最大使用率は56.1%で、同エラーは新Lambda反映後のログには確認されていない。現時点ではメモリ増強を即時実施せず、同じ障害グループをRecoveryして新方式で再観測する。最大使用率80%超、`ERR_INSUFFICIENT_RESOURCES`再発、またはバッチ途中の`TargetClosedException`が継続した場合に3,072MB以上への増強とバッチ上限の再評価を行う。
- 2026-09-13: 障害グループ詳細ReadModel/API、サーバー側全件Recovery、ページング・検索、0件404・group key衝突409・10,000件上限を実装した。Jobsと収集運用から代表Resourceではなく専用ページへ遷移する。
- 2026-09-13: Resource詳細に最新障害の日本語説明、推奨対応、Explicit/要求/最終URL、コピー・外部リンク、展開式技術情報を追加した。試行履歴は主要列と展開詳細に整理し、グループ各行からResource・実行バッチへ遷移できる。
- 2026-09-13: グループ全件とページをまたいで保持する選択対象のRecoveryを分離し、確認Dialog、作成Task数・既存Task再利用数の結果表示を追加した。
- 2026-09-13: `dotnet build src/HorseRacingPrediction.Api/HorseRacingPrediction.Api.csproj --no-restore -p:BaseOutputPath=.build/verify/` は警告0・エラー0。関連endpoint/component tests 13件成功。`dotnet test HorseRacingPrediction.sln --no-restore` は全プロジェクト成功（合計854件成功、2件スキップ）。ローカルAPIを新ビルドで再起動し、認証済みJobs画面の既存ナビゲーションとAPI応答を確認した。ローカルDBにOpen障害がない状態は空表示となり、障害詳細のデータ表示はcomponent testで確認した。

## Implementation notes

- DB migrationは不要だった。既存のRequest/Attempt/Failure情報をReadModelへ投影している。
- 全件Recoveryはクライアントへ全IDを配布せず、確定時にサーバーでOpen対象を再解決する。選択Recoveryは既存notification ID APIを再利用する。
- Lambdaメモリはユーザー方針に従い2,048MBのまま変更していない。microbatch適用後の再発有無を上記閾値で監視する。
- 設計との差分として、対象種別・開催日の専用フィルターは現時点のgroupがDefinition単位であり、全文検索でResource種別・ID・URL・エラーを扱えるため追加しなかった。

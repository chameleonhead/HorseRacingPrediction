# 直近レースの出馬表・結果を一体収集する

- Status: Implemented
- Owner: HorseRacingPrediction maintainers
- Created: 2026-09-14
- Updated: 2026-09-14

## Context

新収集基盤の `JraRaceDiscoveryCollectionHandler` は、基準日より前の日付を一律に historical と判定し、
`race-result` だけを登録している。`race-card` は当日以降にしか登録されない。一方、馬主、生産者、血統等は
出馬表から取得して馬プロフィールとレース時点 Entry へ保存するため、開催後に初めて発見した週末レースでは
結果が保存されても馬主が欠落する。

`JraNavigator` には既に出馬表探索期間の既定値 5 日と、現在ページの短絡リンクを優先して目的ページへ遷移する
仕組みがある。しかし収集タスクは `race-card` と `race-result` に分かれており、同一レース・同一セッションで
出馬表から結果へ連続して取得する契約にはなっていない。

## Goals

- JST の対象日が「今日から 5 日前」以降なら、過去日を含めて必ず出馬表を先に取得・保存する。
- 直近対象が当日以前なら、同じレース収集実行とブラウザーセッションで出馬表から結果へ遷移して保存する。
- 5 日より古いレースは、掲載終了した出馬表を探索せず結果から取得する。
- 出馬表と結果を別々のレース詳細タスクとして計画・実行しない。
- 出馬表由来の馬主を馬プロフィールとレース時点 Entry の両方へ既存の patch semantics で保存する。

## Non-goals

- Odds、開催日程、馬・騎手・調教師プロフィールの独立した収集定義を統合すること。
- 5 日より古いレースの馬主を結果ページや非公式情報から推測すること。
- Parser の項目定義や Domain の馬主モデルを変更すること。
- 管理者が明示する既存の開催日再取得・単一レース再取得 API の画面契約を変更すること。

## Experience and interaction design

自動収集では、管理者が出馬表タスクと結果タスクの順序を意識しない。1 レースにつき 1 つの
「レース詳細」収集状態を表示し、その実行内で日付に応じて次の経路を選ぶ。

```text
レース詳細タスク
  ├─ 対象日 >= JST今日 - 5日
  │    ├─ 出馬表を取得・保存（馬主を含む）
  │    └─ 対象日 <= 今日なら同じ画面から結果へ遷移・保存
  └─ 対象日 < JST今日 - 5日
       └─ 結果へ直接遷移・保存
```

未来または結果未確定のレースは、出馬表保存が成功してもレース詳細全体を完了にせず、適切な次回時刻まで
待機する。再試行時も 5 日境界の内側なら出馬表を先に再取得し、出走取消・騎手変更・馬体重等の更新を
取り込んでから結果を確認する。

同じ開催日のレースを一括して「結果あり／なし」にしない。例えば 12R の発走前に 1R〜8R の結果だけが公開済みなら、
1R〜8R は個別に完了し、9R〜12R はそれぞれの発走時刻と公開状態に応じて待機する。他レースの未公開を理由に、
公開済みレースの保存や同日後続レースの出馬表更新を止めない。

## Documentation updates

- `docs/22-collector-design.md`: 自動収集の実行単位、5 日境界、出馬表から結果への連続遷移を現行運用として追記する。Collector の現行動作の正本である。
- `docs/23-jra-scraping-redesign.md`: `RaceCardLookupPeriod` をレース詳細ワークフローの取得元選択にも使う例外を明記し、Navigator/Workflow の責務を同期する。JRA Page/Parser/Navigator の正本である。
- `docs/26-collection-platform-design.md`: `race-card` / `race-result` を `race-detail` へ統合する Resource/Definition と状態契約を追記する。新収集基盤の正本である。
- `docs/11-automation-design.md`: 優先順位と計画レビューの大枠は変わらず、詳細は `docs/22-collector-design.md` と `docs/26-collection-platform-design.md` が正本であるため変更不要と判断した。
- `docs/10-domain-design.md`: 馬主の保存先と非 null patch semantics は既存どおりで、Domain 契約を変更しないため変更不要と判断した。

## Technical impact

### 統合する収集定義

新規の自動探索は、同じ canonical JRA Race ID に対して `race-card` と `race-result` を別々に request せず、
`ResourceType.Race` に対する単一の `race-detail` definition を request する。日付単位の `race-discovery` も
`ResourceType.Race` を使うが、definition と resource ID が異なるため同じ基盤モデル上で共存できる。
handler は既存の出馬表・結果 parser と domain write workflow を
順に再利用する。Odds と参照主体プロフィールは従来どおり別 definition とする。

既存の `race-card` / `race-result` 収集データは捨てたり別履歴として残したりせず、cutover 時のデータ移行で
`race-detail` へマージする。切替時に active task を drain し、移行済み状態から不足する直近レースだけに
`race-detail` request を作成する。現行ストアは definition 登録のたびに
`Enabled=true` へ戻し、無効化 API を持たないため、旧 definition を明示的に無効化するストア操作を追加する。
無効化後は新規 request と Recovery を拒否し、runtime handler 登録、scheduler/locator 分岐、管理操作の caller を
除去する。旧 definition と revision 行は移行元を説明するメタデータとして保持するが、運用上の Resource、State、
Location、Request、Task、Failure は `race-detail` へ統合する。Attempt ID、Task ID、Request ID、日時、結果、URL、
エラー、batch ID は変更せず、移行元 definition/revision を専用 provenance 項目へ保存して監査可能性を維持する。

### 日付境界

判定は JST の `DateOnly` で `raceDate >= today.AddDays(-5)` とする。したがって境界日を含む。
未来レースもこの条件を満たすため出馬表を取得し、当日より後なら結果ページへは進まない。
この値は設定可能な `RaceCardLookupPeriodDays`（既定 5）を一つの policy として参照し、Navigator と handler で
定義を重複させない。

### 同一ページ内の遷移

直近レースでは、出馬表ページを取得して identity を検証し、出馬表 workflow の全 domain write が成功した後、
現在ページにある「レース結果」リンクを優先して同じ Race ID の結果へ遷移する。リンクがない、または identity が
一致しない場合だけ既存の完全探索へ fallback する。URL を推測生成しない。

出馬表保存が失敗した場合、結果だけを成功させて `race-detail` を Current にしない。結果保存が部分失敗した場合も
同様に retryable/validation failure とし、既存データを消さない。結果未公開は業務上の待機状態として次回時刻を持つ。

### 結果未公開・未確定の扱い

結果取得の要否と再試行時刻はレース単位で判定する。

- 発走前: 結果ページへ遷移せず、公式発走時刻後の最初の確認時刻まで待機する。
- 発走時刻後で結果リンク／結果ページが未公開: 通信・解析失敗にせず `ResourceNotYetAvailable` として待機する。
- 結果ページはあるが確定結果が揃っていない: 取得済みの出馬表を保持し、結果未確定として待機する。
- 同日内で結果公開済みと未公開のレースが混在: 公開済みレースだけ Current にし、未公開レースだけを再試行する。
- 通信失敗、想定外ページ、Race ID 不一致、domain write 拒否: 未公開とは区別し、既存分類どおり retryable failure または
  validation/unexpected failure として記録する。
- JRA が公式に競走中止・取止を示し、結果が今後公開されないことを識別できた場合: 通常の結果待ちを終了し、
  「公式取止」として完了可能な非結果状態を保存する。識別できない欠落を推測で取止扱いにはしない。

最初の結果確認時刻は公式発走時刻を基準とし、発走時刻が不明な当日レースだけ安全な既定時刻を使う。
未公開中は同じ active task を `RetryWaiting` に戻し、新しい task を増殖させない。短間隔での無制限再試行を避けるため、
当日は既存の結果確認間隔、日付経過後は段階的な backoff を適用するが、直近5日の間は再試行ごとに出馬表を先に
再取得する。具体的な間隔は設定値とし、状態画面に次回確認時刻と「結果公開待ち」を表示する。

### Location と状態

現行 Location schema は URL のページ種別を持たず、request の explicit URL も 1 件である。本変更では列を追加せず、
候補 URL の JRA path/CNAME と取得後の page kind/Race ID を検証して出馬表候補・結果候補を判別する。Discovery は
直近期間では出馬表 URL、期間外では結果 URL を `race-detail` の入口として登録する。直近期間の結果 URL しかない
手動依頼や保存済み候補では、その URL から結果だけを先に保存せず、既存 Navigator の完全探索で出馬表へ到達して
から結果へ進む。成功条件は次のとおりとする。

- 未来日: 出馬表保存済みであり、結果確認予定の次回時刻が設定された待機状態。
- 直近の当日・過去日: 出馬表保存と公式結果保存の両方が成功したときだけ Current。
- 5 日より古い日: 公式結果保存が成功したとき Current。

### 原因修復

データ移行は、旧 `race-card` と `race-result` の状態を日付 policy に従って一つの `race-detail` 状態へマージする。
直近 5 日で結果だけ Current のレースは統合状態を Current にせず、移行トランザクション内で補完 request を作る。
これにより今回の週末分を含む出馬表を再取得し、既存 Horse/Entry の欠落馬主を非 null 値で補完する。

通常のアップグレードは既存 collection DB のマージ migration を正とし、Domain Data からの再構築で代用しない。
空DB・災害復旧用 initializer も同じ `ResourceType.Race + race-detail` と completeness policy を使うよう変更する。
canonical resource ID は JRA の日付・競馬場・レース番号を使用し、domain Race ID は `domainRaceId` attribute に保持する。

### データ移行規則

移行は収集全体を pause し、実行中 lease と未送信 envelope がなくなったことを確認してから単一 DB transaction で行う。
事前にDBバックアップと dry-run レポートを作り、次の順でマージする。

1. 旧 RaceCard/RaceResult resource の `EffectiveDate`、`course`、`number`、`domainRaceId` と Domain Data を照合し、
   canonical `ResourceType.Race / yyyyMMdd:Course:Number` を決定する。識別不能・競合が1件でもあれば apply 前に中止し、
   URL や名前から推測しない。
2. `race-detail` revision 1 と対象 Race resource を作る。旧 request/task は ID と全実行情報を維持したまま対象 resource と
   `race-detail` へ付け替え、元の definition/revision を provenance 列へ退避する。Attempt、failure notification、batch、
   outbox の task/request 参照は ID が変わらないため維持する。
3. Location は対象 Race + `race-detail` へ付け替える。同一 URL が衝突する場合、`Active > Unknown > Suspect > Invalid` を
   状態優先順とし、最古の discovered、最新の verified/failed と対応する failure code を残して1件へ統合する。
4. State は required revision 1 とし、期間外は旧 Result が Current の場合、直近の当日・過去日は旧 Card と Result が
   ともに Current の場合だけ AppliedRevision 1 / Current にする。未来日は旧 Card の取得状態を保持しつつ、結果確認時刻を
   NextCollectionAt に持つ非 Current 状態にする。複合条件を最後に満たした時刻を LastCollectedAt とする。
   当日で一部レースの結果だけが公開済みの場合も各 Race を独立判定し、未公開レースを失敗や Current にしない。
5. 直近で Card または Result が不足する Race には `DefinitionChanged` reason の補完 request を作る。結果未公開・未確定は
   次回確認時刻付きの待機 task として作り、移行した terminal task
   が通常 discovery の同 revision 重複抑止に該当しても、補完が省略されない理由種別を使う。
6. 旧 active guard/outbox がゼロ、統合後の参照切れ・重複・件数差がないことを検証して旧 definition を無効化する。
   smoke test 完了まではバックアップからDB全体を戻せる状態を維持し、成功後も旧 provenance は削除しない。

migration は dry-run と apply を同じ変換器で実行し、旧/新 resource 数、state 判定別件数、Location 重複数、履歴 task/attempt
件数、補完 request 件数、識別不能・競合・参照切れを出力する。部分適用や「移行できた分だけ成功」は認めない。

### 既存副作用の維持

統合 handler は、現行出馬表 handler が行う Horse/Jockey/Trainer の参照 request、予測スケジュール登録、引用元保存を
そのまま維持する。結果側の一括 domain write、払戻、天候・馬場、引用元保存も維持する。統合は実行順と完了状態を
変えるものであり、既存 workflow の項目を削らない。

## Decisions

### 採用: 1 レース 1 `race-detail` task

タスク順序の競合を構造的になくし、同一セッションの現在ページから短絡遷移できる。直近レースの完了条件に
「出馬表と結果の両方」を持たせられるため、結果だけ成功して馬主欠落を見逃さない。

### 採用: 既存収集データを migration でマージする

既存の成功・失敗・URL・試行・batch を引き継ぎ、全件再探索によるJRAアクセスと運用履歴の分断を避ける。
IDを維持し、移行元 definition/revision を provenance として残すことで、統合後の画面からも過去試行を追跡できる。

### 採用: 境界日を含む `today - 5 days`

既存 `RaceCardLookupPeriod` の判定と一致し、週末開催を翌週前半に補完できる。時刻ではなく JRA 運用と同じ JST の
開催日単位で判定する。

### 不採用: `race-result` handler から別の `race-card` task を追加登録する

実行順序を保証できず、結果 task が先に Current になる問題を残す。また同じページ間を遷移できるのに別 worker
実行・別セッションになる可能性がある。

### 不採用: 結果ページから得られる項目だけで馬主を補完する

結果ページの項目契約には馬主がなく、出馬表由来の生産者・血統・馬体重等も同時に補完できない。

### 不採用: Location にページ種別列を追加する

JRA URL path/CNAME と取得ページの identity から安全に判別でき、直近と過去で必要な入口 URL は一つに決まる。
複数 URL を一 task に事前登録するためだけの schema migration は変更範囲に見合わない。

## Acceptance criteria

- 自動 discovery が過去日という理由だけで直近 5 日の出馬表 request を省略しない。
- JST 今日から 5 日前のレースは出馬表、続いて結果を同一 `race-detail` task・同一 session で取得する。
- JST 今日から 6 日前のレースは出馬表へ遷移せず、結果だけを取得する。
- 直近の結果公開済みレースは、出馬表の保存成功前に結果だけで Current にならない。
- 直近の結果未公開レースは出馬表を保存し、失敗ではなく次回確認時刻付きの待機状態になる。
- 未来レースは出馬表を保存し、結果取得を試みず次回確認時刻付きの待機状態になる。
- 同一開催日で結果公開済み・発走後未公開・未発走が混在しても、レースごとに Current・結果公開待ち・発走待ちを独立して保持する。
- 発走前には結果ページへ遷移せず、公式発走時刻後に最初の結果確認を行う。
- 発走後に結果リンクがない場合と、結果ページに未確定情報しかない場合を正常な公開待ちとして扱い、全体停止対象のfailureにしない。
- 結果公開待ちの再試行は同じactive taskを使用し、taskやrequestを反復生成せず、次回確認時刻と理由を管理画面から確認できる。
- 公式な競走中止・取止を識別できたレースは無限に結果待ちせず、推測を用いない終端状態になる。
- 出馬表から取得した馬主が Horse の現在プロフィールとレース時点 Entry の両方へ保存され、欠落値の再取得で既存値を消さない。
- 出馬表から結果への現在ページ短絡を優先し、リンク欠落・identity 不一致時は完全探索へ fallback する。
- 出馬表・結果の通信、parse、domain write の部分失敗後に再試行でき、重複配信・lease expiry・再起動でも結果が冪等になる。
- 自動探索は同一 Race ID に `race-card` と `race-result` の別 active task を作らない。
- runtime で旧 `race-card` / `race-result` handler が登録されず、新規 scheduler/discovery/manual request に旧 definition の caller がない。
- 旧 definition は無効化され、新規 request と Recovery を作れない一方、マージ済みの既存 terminal task/attempt/location/state を `race-detail` の管理画面で参照できる。
- dry-run が全旧 RaceCard/Result resource の canonical Race 対応、件数、競合、参照切れ、補完対象をDB変更なしで表示する。
- 識別不能または競合が1件でもある migration は何も変更せず失敗し、再実行可能である。
- apply 後は旧 Resource/State/Location/Request/Task/Failure が `race-detail` へマージされ、Request/Task/Attempt IDと履歴内容、batch関連が維持される。
- Location衝突とState統合が文書化した優先規則どおりで、移行前後の件数差を説明できる。
- 移行元 definition/revision が provenance として参照でき、統合後の管理画面から過去試行を追跡できる。
- 直近 5 日で結果だけ取得済みだったレースには補完 request が確実に作られ、馬主欠落を補完できる。
- Domain Data initializer は canonical JRA Race ID の `ResourceType.Race + race-detail` 状態を冪等に構築し、直近の結果確定済みレースを補完対象から除外しない。
- 出馬表由来の Horse/Jockey/Trainer request、予測スケジュール、引用元と、結果由来の払戻・天候・馬場の保存が維持される。
- 常駐、`--once` / Lambda、同一開催日 microbatch のすべてで同じ境界と実行順になる。
- Odds、主体プロフィール、手動再取得、pause/hold/cancel、location fallback、rate limit、mutation lease guard に回帰がない。

## Acceptance-criterion matrix

| Criterion group | Status | Evidence |
| --- | --- | --- |
| 5 日境界と取得順 | Verified | discovery/handler境界テスト、共有Navigator既定値 |
| 統合 task と状態遷移 | Verified | `RaceDetail_RecentFinishedRace_CollectsCardThenResultInOneTask` |
| 結果公開待ちと同日混在 | Verified | 発走前・未来・未公開・未確定のhandler/storeテスト |
| 馬主補完 | Verified | 既存RaceCard workflowと主体request回帰テスト |
| navigation fallback | Verified | direct candidate・identity mismatch・fallbackテスト |
| retry/restart/idempotency | Verified | availability retry、lease、重複登録、再起動テスト |
| 旧 definition cutover | Verified | runtime登録除去、migration無効化、production caller検索 |
| collection data migration と補完投入 | Verified | `LegacyRaceMigration_MergesResourcesAndQueuesMissingRecentResult` |
| initializer（空DB・災害復旧） | Verified | unified seed、incomplete seed、dry-run/execute冪等テスト |
| 既存副作用の維持 | Verified | subject/prediction/result workflow回帰テスト |
| 実行形態・関連機能回帰 | Verified | API transport E2E、Collector 177件、API 191件、solution test |

## Delivery plan

1. `ResourceType.Race + race-detail` definition、日付 policy、状態・schedule 契約を追加し、境界の単体テストを作る。
2. 出馬表 workflow と結果 workflow を同一 session で順次実行する handler を接続し、短絡遷移と fallback をテストする。
3. discovery、backfill、subject reference、manual/recovery、schedule、URL resolver を `race-detail` request へ切り替える。
4. dry-run/apply 共通の collection data migration を実装し、既存状態・Location・全履歴をマージして不足分の補完 request を作る。
5. 空DB・災害復旧用 initializer を統合状態へ変更し、直近補完と期間外 Current seed を冪等に構築する。
6. active な旧 task の drain、migration、旧 definition 無効化を実行可能な cutover 手順として実装・検証する。
7. 旧 handler/登録/caller を除去し、CodeGraph と検索で production caller がゼロであることを確認する。
8. 実 transport・persistence 境界を通る happy path、未公開待機、部分失敗、再起動、重複配信を検証する。
9. 正本ドキュメント、matrix、検証記録、設計との差分と残課題を更新する。

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | 統合definition、5日policy、schedule/state契約 | Main | High capability | - | CollectionOperations model/store/policy、対象テスト | 境界・発走待ち・公開待ち・Current・取止テスト | AC「5日境界」「統合task」「結果公開待ち」へ接続したテスト結果 | Completed |
| T2 | 同一sessionの出馬表→結果handler | Main | High capability | T1 | Collector handler、Scraping navigation/workflow、対象テスト | 短絡、fallback、同日混在、未公開、未確定、部分失敗テスト | 馬主と公開済み結果を同一leaseで保存し未公開だけ待機する実行証跡 | Completed |
| T3 | 全producer/URL resolver/dispatch互換性の切替 | Main | High capability | T1,T2 | Api/Collector の discovery・subject・manual・dispatcher | callerテスト、microbatchテスト | 旧definition新規callerゼロ、全入口がrace-detailへ接続 | Completed |
| T4 | 既存collection dataのマージmigration | Main | High capability | T1,T3 | Store/schema migrator、管理API/CLI、対象テスト | dry-run無変更、transaction rollback、全entity/衝突/state/provenanceテスト | 旧データを統合し補完requestを作る移行レポート | Completed |
| T5 | 空DB initializerの統合 | Main | High capability | T1,T3 | CollectionInitializer、初期化store、対象テスト | dry-run/execute冪等性、境界seedテスト | 災害復旧でも同じ統合状態になるレポート | Completed |
| T6 | 旧definition停止とcutover | Main | High capability | T4,T5 | definition lifecycle、運用切替、関連テスト・文書 | pause/drain/migrate/disable/recovery拒否/履歴参照/rollbackテスト | 二重実行なし、統合履歴保持、rollback可能な順序 | Completed |
| T7 | end-to-end回帰と文書同期 | Main | High capability | T2,T3,T4,T5,T6 | 統合テスト、change record、正本文書 | transport/persistence happy path、再起動・重複、関連solution test、CodeGraph sync | 全AC Verified、検証記録と差分・残課題更新 | Completed |

T1 は5日境界・統合状態、T2 は取得順・馬主・navigation・部分失敗、T3 は全入口・microbatch・旧caller、
T4 は既存履歴・状態のマージと欠落補完、T5 は空DB復旧、T6 は二重実行防止・切替、T7 は実経路と全回帰の
受け入れ基準を担当する。

## Review gates

- **Design and task-split review (2026-09-14, Main):** 現行の Resource/Definition、Location、request deduplication、
  handler registry、dispatcher、initializer を確認した。`ResourceType.Race` 再利用、Location schema 非変更、旧definitionの
  明示無効化に加え、既存 collection data をID・provenance付きで物理マージする方針を採用した。T1〜T7 で
  すべての受け入れ基準と検証をカバーできると判断した。未解決の設計判断はない。
- **Pre-implementation review (2026-09-14, Main):** 利用者の「実装をお願いします」を本記録への明示承認として確認した。
  working tree は clean、CodeGraph は up to date であり、既存変更との write scope 衝突はない。T1 を Runnable、T2〜T7 を
  依存順に Dependent とした。各 task は前段のproduction接続と対象テストを入力とし、受け入れ基準に反する状態モデル、
  migrationの識別不能・部分適用、旧経路の残存を検出した場合はcheckpointで修正してから後段へ進む。
- **Checkpoint review:** T1/T2、T3/T4、T5/T6、T7 の各検証可能な境界で diff、テスト、matrix、CodeGraph を確認する。
- **Final review:** 全 task と受け入れ基準が Verified、旧 production caller と runtime 登録がゼロ、直近補完経路が
  実 transport/persistence 境界で成功した場合だけ Implemented とする。

## Verification record

- 2026-09-14: 現行 `JraRaceDiscoveryCollectionHandler` が `offset < 0` を historical とし、過去日は
  `race-result` のみ request することを確認した。
- 2026-09-14: 馬主は `RaceCardPageParser` から出馬表 workflow を通って Horse と Entry へ保存され、
  result workflow だけでは取得されないことを確認した。
- 2026-09-14: `JraNavigator.DefaultRaceCardLookupPeriodDays` は 5、判定は
  `date >= today - days` であり、既存 Navigator は現在ページの短絡遷移と完全探索 fallback を持つことを確認した。
- 2026-09-14: `race-detail` は既存 `ResourceType.Race` で表現できること、現行 Location にページ種別列がなく
  request の explicit URL が1件であること、URL path/CNAME と取得ページ identity でスキーマ変更なしに判別可能なことを確認した。
- 2026-09-14: definition は登録時に常に有効化され無効化操作がないため、旧handler削除前に新規request/Recoveryを
  拒否する明示的な definition lifecycle が必要と確認した。
- 2026-09-14: initializer が現在 `RaceCard` と `RaceResult` を domain Race ID で別 seed にしているため、canonical JRA
  Race ID の統合seedと直近補完投入へ変更が必要と確認した。
- 2026-09-14: 利用者の指定により、通常アップグレードは再構築ではなく既存 collection data のマージ migration とした。
  現行schemaでは Request/Task/Location/State が resource/definition を直接保持し、Attempt/Failure/Outbox は Task ID を
  参照するため、IDを維持した付替えが可能である。一方で移行元definition/revisionを失わないprovenance列と、Location・
  State衝突の明示的な統合規則が必要と判断した。
- 2026-09-14: 利用者の指摘により、同日すべての結果が同時公開される前提を明示的に排除した。発走前、発走後未公開、
  ページ公開済み未確定、公式結果公開済み、公式取止をレース単位で分け、未公開・未確定は failure ではなく同じ active
  task の待機状態として再試行する設計とした。
- 2026-09-14: 利用者が「実装をお願いします」と明示し、本記録を Approved として Execution Mode へ移行した。
- 2026-09-14: `ResourceType.Race + race-detail` handlerへ統合し、直近5日は出馬表→結果、期間外は結果のみの経路を実装した。発走前・未来・結果未公開・未確定は次回時刻付きavailability waitになる。
- 2026-09-14: 旧collection dataをID・provenance維持で統合し、不足分の `DefinitionChanged` taskを同一transactionで投入するdry-run/apply migrationを実装した。
- 2026-09-14: initializer、URL resolver、discovery、subject history、dispatcher、runtime登録、正本文書を統合定義へ同期した。
- 2026-09-14: Collector CollectionPlatformテスト154件、追加後Collector全177件、API全191件が成功した。solution testはAPIの旧期待値7件を更新後に再検証した。
- 2026-09-14: 最終 `dotnet test HorseRacingPrediction.sln --no-restore` は全プロジェクト成功（Collector 178件、API 191件、Scraping 237件成功・1件skip、その他も失敗0）。`git diff --check` 成功、CodeGraph sync完了を確認した。
- 2026-09-14: CodeGraphをproduction変更後に更新し、runtimeの旧handler登録と旧definition新規producerがないことを確認した。

## Deviations and follow-up

- migration apply は管理APIでpipeline pauseを必須とし、Store側でも旧・新active taskがゼロであることを検証する。未送信outboxはterminal task IDを保持したまま移行され、worker側の世代・状態検証で安全に無視できるため、個別のゼロ件前提にはしなかった。
- レース全体の公式取止を示す専用JRAページ標本は現行parser契約に存在しない。馬単位の取消・除外・競走中止・失格は従来どおり結果として保存する。レース全体の取止ページを取得できた時点で、推測せず終端化するparser fixtureを別途追加する。

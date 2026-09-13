# システム全体の時刻基準をJSTへ統一する

- Status: Implemented
- Owner: HorseRacingPrediction team
- Created: 2026-09-13
- Updated: 2026-09-13

## Context

管理画面の日時表示には `DateTimeOffset.ToLocalTime()` が使われており、表示結果が Api ホストのローカルタイムゾーンに依存する。一方、収集計画や JRA の日付判定の一部は明示的に JST を使うが、ジョブ、監査、メモ、結果確定、予想などの内部時刻生成には `UtcNow` と JST が混在している。

競馬開催日、発走時刻、運用担当者の判断はいずれも日本時間を基準とする。この変更の目的は、利用者への表示と、日付境界・実行可否・期限などの業務判断を JST（UTC+09:00）で一貫させることである。データベースはタイムゾーンなしのJST壁時計値を保存し、Entity Framework Coreの標準的な変換機構でJSTとして実体化する。

## Goals

- 管理画面に表示するすべての日時を、実行ホストの設定に依存しない JST とする。
- 表示、開催日境界、発走時刻到達、実行可能時刻、期限、再試行などの判断をJSTに統一する。
- ドメインとアプリケーションでは絶対時刻を `DateTimeOffset` で安全に扱い、データベースにはJSTの壁時計値をタイムゾーンなしで保存する。
- Entity Framework Coreの標準的なモデル構成で保存・実体化変換を一元化する。
- 日付境界、開催日、発走時刻、定期計画、リース、再試行、監査履歴を同じ JST 基準で扱う。
- 既存の UTC データが表す瞬間を変えず、安全に JST 表現へ移行する。
- Windows、Linux、ローカル、コンテナ、Lambda で同じ結果にする。

## Non-goals

- 過去に記録した瞬間そのものを9時間ずらすことはしない。
- JRA が提供する時刻や日付の意味を変更しない。
- 経過時間、レース走破タイム、ラップタイムをタイムゾーン変換しない。
- 外部プロトコルが要求する Unix epoch や UTC 値の仕様を変更しない。境界で変換する。
- 利用者が任意のタイムゾーンを選択する設定は追加しない。

## Experience and interaction design

- 日時を含む画面は `yyyy/MM/dd HH:mm` または既存画面に適した同等の形式で表示し、画面または共通表示部品で `JST` を明示する。
- 秒が運用判断に必要な更新時刻は、既存どおり秒まで表示する。
- 日付のみ、時刻のみを表示する場合も JST から切り出す。
- 空値は既存どおり `—` 等で表し、ローディング、空状態、エラー状態の挙動は変更しない。
- ブラウザー、Api、OS のタイムゾーン設定を変更しても表示値は変わらない。

## Documentation updates

- `docs/00-system-architecture.md`: システム横断のJST時刻契約、外部境界、既存データ移行方針を追加する。サービス共通規約の正本である。
- `docs/20-admin-ui-design.md`: 管理画面の日時表示を明示的なJST変換へ統一し、JST表記を付ける規約を追加する。管理UI表示規約の正本である。
- `docs/10-domain-design.md`: ドメイン型は既に絶対時刻を `DateTimeOffset`、競馬日付を `DateOnly`、発走時刻を `TimeOnly` で表現しており、型の責務は変わらないため変更不要と判断した。
- `docs/22-collector-design.md`: 開催日判定は既にJSTと明記されており、本変更の共通時刻契約へ従うため変更不要と判断した。
- `docs/25-predictor-design.md`: 予想スケジュール固有の外部仕様は変わらず、共通時刻契約へ従うため変更不要と判断した。
- `docs/26-collection-platform-design.md`: 収集基盤の状態モデルや順序保証は変わらず、共通時刻契約へ従うため変更不要と判断した。

## Technical impact

### 共通時刻契約

- 絶対的な瞬間は `DateTimeOffset` で表し、アプリケーション境界の内側では常に JST の `+09:00` へ正規化する。
- 現在時刻は注入可能な `TimeProvider` の UTC clock を唯一の時計として取得し、共通の JST 変換処理を通す。直接の `DateTimeOffset.Now`、`DateTimeOffset.UtcNow`、`DateTime.Now`、`DateTime.Today` は、外部仕様への適合箇所を除きプロダクションコードから除去する。
- 競馬開催日は JST の現在時刻から `DateOnly` を導出する。公式発走時刻は対象開催日の JST と組み合わせて絶対時刻にする。
- API の日時入力は offset 付き ISO 8601 を受け入れ、意味する瞬間を維持してJST基準の判断に使用する。日時出力はJSTの `+09:00` を付けた ISO 8601 とする。offset のないAPI入力は曖昧なため拒否する。
- JSON API、実体化後のイベント・ReadModel・ジョブ・リース・監査・メモ・観測・予想の日時も同じJST契約に従う。
- Unix epoch、AWS Lambda deadline、外部AI応答など外部仕様が UTC または epoch を要求する値は、その境界表現を維持し、取り込み直後または送信直前に変換する。

### 永続化と移行

- 新規・更新される日時は、SQLiteを含むデータベースへ `yyyy-MM-ddTHH:mm:ss.fffffff` 相当のJST壁時計値として保存し、`Z`、`+09:00`、タイムゾーンIDを含めない。
- EF Coreでは標準の `ValueConverter` をモデル構成へ登録し、書き込み時に `DateTimeOffset` を同じ瞬間のJSTへ変換して `DateTimeKind.Unspecified` の `DateTime` として保存し、読み出し時にその壁時計値へ `+09:00` を付けて `DateTimeOffset` として実体化する。nullable型にも同じ規約を適用する。
- converterは各Repositoryや画面で個別適用せず、各 `DbContext` のモデル構成または共有規約として一元登録する。プロパティ型は原則 `DateTimeOffset` を維持し、DB都合でドメイン型を `DateTime` へ変更しない。
- 手書きSQL、イベントpayload、JSON列、ローカルキューなどEF Coreのproperty converterを通らない永続化経路は、共通の永続化変換処理を使用して同じ契約を守る。
- JSON APIはDB表現と分離し、引き続きoffset付きの日時を入出力する。DB内JSONに日時が含まれる場合だけ、永続化専用serializerでタイムゾーンなしのJST値を扱う。
- 既存の UTC または他offsetの値は、表す瞬間を維持したままJST壁時計値へ変換し、その後offsetを除く一回限りの移行を行う。nullable列、イベントpayload、JSON列、ローカルキューを含む実在する全日時保存箇所を実装時に棚卸しする。
- 文字列順序で期限や優先順を比較する列は、固定長・桁揃え済みのJST表現へ統一する。移行中にoffset付き値とタイムゾーンなし値を混在させず、同一トランザクションで全対象を変換してインデックスと期限判定を再検証する。
- Unix millisecondsなど絶対時刻を数値で保持する列は値を変更しない。
- 移行前に既存のDBバックアップ機構を使用し、失敗時にはトランザクションをロールバックする。

### 画面表示

- `ToLocalTime()` を廃止し、共通のJSTフォーマッターを全管理画面で使用する。
- ジョブ、試行、障害、収集バッチ、バックフィル、レース結果、メモ、名寄せ監査、主体情報、一覧の最終更新など、日時を表示する全画面を対象とする。
- DOM上の可視文字列またはアクセシブルな補足でJSTであることを判別できるようにする。

## Decisions

### 採用: 内部は `DateTimeOffset +09:00`、DBはタイムゾーンなしJSTに統一する

利用者の「内部もJSTベース」という要望を明示的な契約として実現しつつ、データベースではoffsetを保持しない。DB読み書き境界で必ずJSTを付与・除去するため、アプリケーション内部では絶対時刻の比較可能性を維持する。

### 不採用: DBにも `+09:00` を保存する

タイムゾーン情報を保存しないという要件に反する。JST以外の値をDBへ書き込ませないことで、タイムゾーンなしでも値の意味を一意にする。

### 採用: 共通時計・変換・表示部品を設ける

各サービスやRazor componentが個別にタイムゾーンID、`UtcNow`、書式を持つ状態を解消し、Windows/Linux差と変換漏れを防ぐ。テストでは固定clockを注入する。

### 採用: EF Core標準の `ValueConverter` でDB境界を構成する

EF Coreのモデルメタデータに保存・実体化規則を置き、通常のquery/update経路へ自動適用する。Repositoryごとの手動変換やmaterialization interceptorは採用しない。変換を通らない手書きSQL等だけを明示的な境界として扱う。

### 不採用: 保存はUTCのまま表示だけJSTにする

一般的な分散システムでは有力だが、内部もJSTに統一する今回の要求を満たさず、UTC/JST混在を残す。

### 不採用: `DateTime` の `Local` に統一する

ホスト設定への依存とoffset欠落を招き、コンテナやLambdaで再現性を保証できない。

## Acceptance criteria

- Apiホスト、ブラウザー、OSのタイムゾーンに関係なく、管理画面の全日時が同じJST値で表示される。
- 管理画面の日時表示から `ToLocalTime()` がなくなり、JSTであることを利用者が判別できる。
- JST 2026-09-14 00:30 と UTC 2026-09-13 15:30 が同じ瞬間として扱われ、画面は `2026-09-14 00:30 JST`、APIは `2026-09-14T00:30:00+09:00`、DBはoffsetなしの `2026-09-14T00:30:00` 相当になる。
- 現在時刻を生成するプロダクション経路が共通clockを通り、JST `+09:00` の `DateTimeOffset` を返す。
- APIがoffset付き入力を同じ瞬間のJSTへ正規化し、offsetなし入力を受理しない。
- ジョブの実行可能判定、リース期限、再試行、outbox、ローカルキュー、予想スケジュールがJST化の前後で同じ瞬間に発火する。
- DBへ新規保存される日時に `Z`、offset、タイムゾーンIDが含まれず、読み出すと同じ壁時計値のJST `DateTimeOffset(+09:00)` になる。
- EF Coreで管理される非nullable・nullableの日時プロパティが、モデル構成の `ValueConverter` を通じて保存・実体化される。
- 既存UTCデータは瞬間を維持してタイムゾーンなしJST表現へ移行され、日付跨ぎも正しい。
- UTC・他offset・JSTが混在していた既存データの並び順、期限検索、監査履歴順が、タイムゾーンなしJSTへの移行後も時系列順になる。
- イベントpayload、JSON列、nullable日時、ローカルキューを含む永続日時の棚卸し結果が検証記録に残る。
- JRAの開催日・発走時刻境界はJSTで判定され、23:59から00:00の日付跨ぎを固定clockテストで確認できる。
- 外部仕様上Unix epochまたはUTCを要求される境界は互換性を維持する。
- すべての関連テスト、ビルド、`git diff --check` が成功する。

## Delivery plan

1. 全プロジェクトで時刻生成、変換、表示、API契約、永続化列を棚卸しし、共通JST clock/変換/書式とEF Core `ValueConverter` を追加する。
2. Api、Collector、Predictor、Agents、Domainの直接的な現在時刻取得を共通clockへ置き換える。
3. 管理画面の日時表示を共通JSTフォーマッターへ置き換え、表示とアクセシビリティのcomponent testを追加する。
4. APIの入力検証・出力正規化を実装し、offsetあり・なし・日付跨ぎをcontract testで検証する。
5. SQLite、イベント、DB内JSON、キューの既存日時をタイムゾーンなしJST表現へ変換する移行を実装し、読み出し時のJST実体化、バックアップ、ロールバック、混在形式、順序、期限判定を検証する。
6. scheduling、lease、retry、outbox、collection、predictionの実経路で同じ瞬間に動作する統合テストを行う。
7. 検証結果、実装差分、残課題を本記録へ追記し、正本文書を同期する。

各実装チェックポイント前に関連ビルド・テスト、`git diff --check`、`git status` を確認し、目的単位でコミットする。

## Acceptance-criterion matrix

| 対象 | 状態 | 実経路 |
|---|---|---|
| 管理画面の全日時表示 | Verified | API response → Razor component → `JstTime.Format` → DOM |
| 共通JST clockと内部生成 | Verified | `TimeProvider` / system clock → `JstTime.Now` → service/domain timestamp |
| API offset契約 | Verified | HTTP JSON converter → JST normalization → `+09:00` response |
| DBのタイムゾーンなし保存と既存データ移行 | Verified | EF `ValueConverter` / persistence helper → timezone-less SQLite/JSON → JST materialization |
| scheduling/lease/retry | Verified | scheduler → store query → dispatch/worker completion |
| JRA日付・発走境界 | Verified | clock → collection policy/workflow → side effect |
| 外部時刻プロトコル互換 | Verified | external epoch/UTC ↔ application JST |

## Verification record

- 2026-09-13: 利用者が、表示と判断に使用する日時をJST基準とし、DBはタイムゾーンなし、EF Core標準の方法でJSTとして実体化する設計を明示的に承認した。
- 2026-09-13: CodeGraphとリポジトリ検索により、明示的JST変換と `UtcNow`、`Now`、`Today`、`ToLocalTime()` が混在していることを確認した。
- 2026-09-13: 管理画面ではジョブ、試行、障害、収集バッチ、バックフィル、レース結果、メモ、名寄せ履歴、主体情報等に日時表示が存在することを確認した。
- 2026-09-13: SQLiteの一部期限判定がoffset付きISO文字列の比較を使うため、既存値と新規値を混在させず移行する必要があることを確認した。
- 2026-09-13: 共通の `JstTime`、API用およびDB内JSON用converterを追加し、UTCからJSTへの日付跨ぎ、offsetなしDB表現、JST実体化、offsetなしAPI入力拒否をテストした。
- 2026-09-13: `EventStoreDbContext`、`CollectionPlatformDbContext`、`PredictionScheduleDbContext` の `DateTimeOffset` / nullable `DateTimeOffset` にEF Core標準の `ValueConverter` をモデル規約として登録した。ドメインプロパティ型は変更していない。
- 2026-09-13: EventStore EF migration、collection platform schema version 7、prediction schedule、local queueに既存offset付き値を同じ瞬間のタイムゾーンなしJSTへ変換する処理を追加した。migration前のバックアップとtransactionは既存経路を維持した。
- 2026-09-13: 管理画面のジョブ、試行、障害、実行バッチ、バックフィル、レース結果、メモ、名寄せ、JRA主体取得日時を共通JST表示へ置き換え、`ToLocalTime()` をプロダクションUIから除去した。画面構造・操作・loading/empty/error状態は変更していない。
- 2026-09-13: 収集計画の3時間bucketとeffective date、UIの日付初期値、MLの日付fallback、Collector/Predictor/Agentsの現在日時をJST基準へ統一した。
- 2026-09-13: `dotnet build HorseRacingPrediction.sln --no-restore` は警告0・エラー0で成功した。
- 2026-09-13: 全9テストプロジェクトを逐次実行し、合格889件、スキップ2件、失敗0件を確認した。solution一括の並列実行ではテストホストが一度終了したため、リソース競合を避けた逐次実行で全件を再検証した。

## Deviations and follow-up

- 画面構造と操作導線を変更していないため、UI検証はbUnit component testで行い、新たなbrowser visual regressionは追加していない。
- EventFlowの `EventEntity.Data` / `Metadata` と `SnapshotEntity.Data` / `Metadata` はライブラリ所有のopaque envelopeであり、EFの日時propertyではないため `ValueConverter` の対象外である。アプリケーション所有のReadModel JSON、ローカルキューJSON、typed日時列にはJST保存契約を適用した。

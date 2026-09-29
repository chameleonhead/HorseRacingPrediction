# APIクライアント設計

本書はApiClientと共有HTTP DTOの正規設計。2026-09-30に承認済み。実装状況と承認は[変更記録](changes/20260930_refit-api-clients/README.md)を参照する。

## 現状と対象

ApiClientはnet10.0でRefit / Refit.HttpClientFactory 16.3.0を参照済み。現在のIRaceQueryServiceとIPredictionWriteServiceは既存利用者向け抽象であり、新Refitインターフェースとは分離して残す。Contractsには読み取りDTOだけでなく要求・応答・共通型がある。

全業務JSON APIを対象とする。Races、Horses等の資源単位で`I{Resource}Api`をApiClientの対応する名前空間に配置する。Collectionの収集運用・修復も含む。管理画面のlogin/logout、Razor UI、Swaggerは対象外。healthは業務外のため新クライアントの対象外。

## 契約の配置と命名

- `HorseRacingPrediction.Contracts.{Resource}`を所有API単位に使う。複数APIが参照しても所有者が明確なら所有APIに置く。
- 共通・所有者を定められない型は`Contracts.Common`、日時補助は`Contracts.Common.Time`。CommonとSharedを併設しない。
- 入力と戻りデータの両方がある操作は、同じ語幹の`{Operation}{Resource}Request`と`{Operation}{Resource}Response`を対で用意する。入力パラメーターがなければRequestを、戻りデータがなければResponseを作らない。空型・空オブジェクト・形式上の対を作らない。enum、例外、値オブジェクト、補助クラスには機械的にDtoを付けない。
- 業務データの塊は必ず`{Meaning}Dto`にする。Request/Responseは操作の入出力を表す入れ物であり、業務エンティティの項目を直接展開しない。詳細は`RaceDto Race`、一覧は`IReadOnlyList<RaceSummaryDto> Races`のように意味のあるプロパティ名を使う。無意味な共通`Data`による一律ラップはしない。
- Response直下に置く項目は都度判断する。ページング情報、処理件数、受付IDなど操作・応答そのものの単一値は直下へ置ける。複数項目が一つの概念を表すなら`PaginationDto`や`CollectionProgressDto`等へまとめる。RaceId・RaceName・RaceDateなどエンティティの項目をResponseへ並べることは禁止する。Requestも業務データの塊を持つ場合は入力専用Dtoに分離し、path IDや単純な検索条件は直下に置ける。
- `Dto`/`ReadModel`/`Snapshot`/`Entry`という従来名だけで型の役割を決めず、実際のHTTP境界・参照を確認する。同じ意味・データ・nullability・制約を持ち自然に共有できる型は統合する（利用者の追加指示）。形だけの一致で統合せず、意味や必要項目が異なる型は区別する。統合候補の差分と全利用者を確認し、manifestへ複数旧型から統合先への対応と理由を記録する。
- 例：`RacePredictionContextDto`はデータとして維持し`GetRacePredictionContextResponse.Context`へ格納。`HorseReadDto`は`HorseDto`、`PredictionTicketSummaryReadModel`は`PredictionTicketWithMarksDto`へ整理し、それぞれ操作別Responseが持つ。既存Summary形状とは異なるため名前だけで統合しない。
- サーバー内部モデルがHTTPへ露出している箇所は共有のwire DTOと明示的マッピングを追加する。EF entity、Domain event、CollectionOperationsの実行モデル・永続モデル自体は移動しない。Contracts/ApiClientからApi、Infrastructure、CollectionOperationsを参照しない。
- CLR名・OpenAPI schema IDに加えて、DTOを包むJSON構造も変更対象とする（利用者の2026-09-30指示）。業務データの意味、値、null、日時/enum表現、route、認証条件は維持する。旧形状との互換ラッパー/並行APIは作らない。既存利用者は同じHTTP実装のまま新Request/Responseの組立・取り出しへ追従させる。

### 操作契約の例と境界

```csharp
public sealed record GetRaceRequest(string RaceId);
public sealed record GetRaceResponse(RaceDto Race);

public sealed record SearchRacesRequest(DateOnly? RaceDate, int? Page);
public sealed record SearchRacesResponse(
    IReadOnlyList<RaceSummaryDto> Races, PaginationDto Pagination);
```

これは構造の説明であり、既存の検索条件を削減する指定ではない。JSON例は`{ "race": { "raceId": "...", "raceName": "..." } }`。各操作のmanifestに入力/戻りデータの有無、必要なRequest/Response、DTO、直下プロパティ、配置理由、旧→新JSONパスを記録する。

GET/DELETEのRequestを用意することとJSON bodyの追加は別である。path値はpathへ、検索条件はqueryへ割り当て、IDをqueryへ重複送信しない。必要ならRefitインターフェースの公開メソッドから属性付きtransportメソッドへ分解して渡す。入力のない操作はRequest引数を持たず、CancellationTokenのみを受け取る。CancellationTokenや認証ヘッダーは業務入力の有無の判定に含めない。path/queryだけの操作は入力ありとして扱う。

戻りデータがない操作にはResponseを作らず、本文なしの応答を維持する。204を200へ変更したり、`{}`を返したりしない。既存の200/201/202/204/207とLocationは意味を維持する。エラーは成功Responseへ偽装せず既存status/error契約を維持する。

## FactoryとDI

`IApiClientFactory.Create<TApi>() where TApi : class`を共通入口とし、対象は登録済みの業務APIインターフェースだけとする。未知の型は明確な例外とし、任意のサービス解決には使わない。

`services.AddHorseRacingApiClient(options => ...)`を実装する。Optionsは絶対HTTP(S)のBaseAddress、任意ApiKey、ApiKeyHeaderName（既定X-Api-Key）、Timeoutを持つ。秘密情報を例外・ログ・サンプルに含めない。既存業務APIはGETを含めAPIキー保護対象。ApiKeyの省略は呼出元のhandlerで認証ヘッダーを付与する場合に対応するためであり、匿名アクセスを保証しない。認証エラーをそのまま返す。

FactoryはIHttpClientFactory管理の共通named HttpClientとRefit生成実装を利用し、BaseAddress・serializer・handler設定を共通化する。明示的なAPI登録表を使い未登録型を拒否する。HTTP設定用のIHttpClientBuilderを拡張メソッドから返し、呼び出し元がhandlerを追加可能にする。Factoryは長寿命HttpClientを独自キャッシュしない。並行呼び出しでヘッダーを書き換えない。自動retryは追加しない（書込APIの二重実行を避ける）。

公開操作は入力がある場合だけ専用Requestを受け取り、CancellationTokenは常に受け取る。戻りデータがある場合は`Task<ApiResponse<TResponse>>`、ない場合は`Task<IApiResponse>`とする。後者はHTTP status/header/errorを扱うためのRefit型であり、空の業務ResponseやJSONを作るものではない。業務DTOや配列はResponse内に保持する。404/409/422等とエラー本文を保持する。transport failureとcancellationは例外として伝播する。応答のDispose責任を使用例に示す。URL/query名、日付、配列形式をendpointへ明示的に合わせ、null queryは省略する。

System.Text.JsonはAPIと同じWeb既定・JST converterを使う。Refit 16.3.0の同梱仕様に従い生成クライアントと必要なJSON metadataを用意する。reflection fallback依存を暗黙追加しない。パッケージの更新は目的外。

```csharp
// 将来の利用例。この変更ではアプリの起動処理へ追加しない。
services.AddHorseRacingApiClient(options =>
{
    options.BaseAddress = new Uri("https://api.example.test/");
});

var races = factory.Create<IRacesApi>();
using var response = await races.GetRaceAsync(new GetRaceRequest(raceId), cancellationToken);
// response.StatusCode / response.Error / response.Content?.Raceを確認する。
```

## ツールによる移行

推奨はRoslynで型を認識した一括変更。最初に旧完全修飾名→新完全修飾名・配置先のmanifestを出し、重複・未分類を検出する。

1. MSBuildWorkspaceでsolutionを読み、manifestの旧完全修飾名をContracts compilationのsymbolへ対応付ける。
2. 実装した移行ツールはRoslynの意味モデルで型参照symbolを識別し、構文rewriterで宣言名、namespace配置、完全修飾名、alias参照を更新する。API別分割は単一namespaceのRenameでは実現しないため、各型を個別に新配置へ移す。曖昧な名前の文字列置換は行わない。
3. dry-runで差分を確認してから適用する。ツールは専用の`tools/ContractMigration`へ置き、manifestを変更記録へ残す。再実行時は既に移行済みの項目を検出し、衝突時に停止する。
4. Razor、文字列内の型名、コード生成入力、ドキュメントはRoslynだけでは網羅を保証できない。rgで旧名・旧namespaceを照合し、Razor buildと全solution buildで確認する。歴史文書は書き換えない。
5. Request/Responseの新設とJSONの包み直しは意味のある契約変更として別工程で行う。単純Renameへ混ぜない。format、新契約HTTPテスト、業務データ保存性、既存利用者の回帰テスト、差分確認を行う。

IDEで作業する場合はVisual Studio/Riderの型Renameとnamespace調整リファクタリングも候補。ただし今回の大量の分類移動ではmanifest付きRoslynツールの方が変更対象を再現・レビューしやすい。正規表現だけの全置換はalias、同名型、文字列を区別できないため主方式にしない。

参考：[Roslyn Rename API](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.rename.renamer.renamesymbolasync)、[Refit公式](https://github.com/reactiveui/refit)。実装時は参照済み16.3.0の同梱APIを基準にする。

## 導入境界

既存AdminApiClient、Collector/PredictorのHTTP処理、IRaceQueryService/IPredictionWriteService実装をRefitへ差し替えない。既存利用者には型参照、Requestの組立、ResponseからのDTO取り出し、必要な応答データの扱いを反映する。「未導入」はRefitへの切替と新DI呼出を行わない意味であり、既存利用者を旧JSONのまま残す意味ではない。サーバーと利用者の契約変更は同じ変更セットで検証する。稼働ホストで新DI拡張を呼ばない。デプロイ・DB移行は行わない。

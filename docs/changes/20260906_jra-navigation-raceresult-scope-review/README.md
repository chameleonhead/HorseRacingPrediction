# JRAスクレイピング層 Navigation / RaceResult取得仕様 変更

- Status: In progress (Phase 1-3 実装済み、Phase 4 未着手)
- Owner: HorseRacingPrediction maintainers
- Created: 2026-09-06
- Updated: 2026-09-06

## Context

既存のJRAスクレイピング実装（`docs/23-jra-scraping-redesign.md` で設計した `JraSession` / `JraNavigator` / `JraPageReader` / 各 `IJraPageParser` のアーキテクチャ）は、`Calendar -> RaceList -> RaceCard -> RaceResult` という単一の縦方向Navigationと、RaceResultParserの緩い（未知値をUnknownへ丸める・失敗時にサイレントへプレースホルダー値で継続する）解析に依存していた。

実運用で次の問題が判明している。

- RaceCard（出馬表）が取得できないことが、成績収集ジョブ全体の失敗として扱われるケースがある。
- 出馬取消・除外・中止・失格などJRA上正常な特殊状態が、着順欄のパース失敗と区別されずサイレントに欠落（馬番0・馬名空のプレースホルダー行）としてAPIへ送信されていた（`ed1d5eb` で一部対処済み）。
- 天候・馬場状態・性齢などの未知値が `Unknown` へ丸められ、JRA側の表記揺れやParserの取り違えを区別できない。
- レース結果ページの「開催選択」到達判定（Current/Recent/Historical）が、RaceCard探索用の期間しきい値と混在しており、責務が分離されていない。

本変更セットは、ユーザーから提示された「JRAスクレイピング層 Navigation / RaceResult取得仕様 変更依頼書」（本ドキュメント末尾に全文を保持）を実装へ落とし込む。依頼書は34節にわたる詳細仕様であり、規模が大きいため複数フェーズに分けて実装する。本ドキュメントはその進捗管理と設計決定の記録を兼ねる。

## Goals

依頼書34節「完了条件」を満たすこと。要約すると次の通り。

1. RaceCard（出馬表）とRaceResult（レース結果）のNavigationを独立させ、`Calendar -> RaceList -> RaceCard -> RaceResult` を必須経路として扱わない。
2. 直近レースは出馬表からRaceCardを優先的に探索し、出馬表に存在しなければ探索を諦める（Collector全体を失敗させない）。
3. 明確な過去レースでは出馬表を探しに行かず、レース結果ページから出走馬相当情報も復元する。
4. RaceResultの取得失敗・解析異常は原則Collectorを失敗させる。RaceIdの自己検証、天候・馬場状態・結果状態・性別等の未知値検出を含む。
5. 平地・障害のページ差異（推定上り/平均1F、ハロンタイムの有無）、同着・降着、馬体重・人気・賞金・券種の「値なし」と「値ありだが解析不能」の区別を扱えること。
6. RaceResult全体のValidation完了後にのみAPI書き込みを行い、レース宣言→エントリー登録の順序を維持すること。
7. 上記特殊ケース・異常ケースを自動テストでカバーすること。

## Non-goals

- RaceCard（出馬表）Parser自体の厳格化（本変更はRaceResult側を主眼とする。RaceCardは依頼書3節の通り「取得できれば使う」情報のため、既存の失敗許容実装を維持する程度に留める）。
- JRAサイトの新しいNavigation導線（重賞以外の特殊レース種別、地方競馬・海外馬券発売レース等）への対応拡張。
- 既存の一括登録API（`IDataCollectionWriteService.DeclareRaceResultBulkAsync`）自体のインターフェース変更。API側がレース宣言→エントリー登録の順序をアトミックに保証している前提を維持し、Scraper側で非一括の新APIを設計し直すことはしない。

## Technical impact

依頼書の節番号に対応させて整理する。

### Phase 1（実装済み・コミット `70777b7`）

- **依頼書8節（Race Identity Validation）**: `JraRaceResultCollectionWorkflow` に要求 `RaceId`（Date/Course/Number）とページ解析済み `RaceId` の照合を追加。不一致時は `JraRaceIdentityMismatchException` を送出する。
- **依頼書10・11節（天候・馬場状態）**: `RaceResultPageParser` で「ラベル自体が存在しない（optional要素なし）→ null」と「ラベルは存在するが値がJRA既知集合外→エラー」を明確に分離。従来は両方とも `null` へ丸めていた。
- **依頼書15節（性齢）**: `HorseSex` enum（牡/牝/せん）を新設し、性齢列を `Sex`/`Age` へ分解。列自体が存在しない場合は null、値が既知集合外ならエラー。
- **依頼書16〜19節（ResultStatus / FinishPosition / 降着 / Time）**: `ResultStatus` enum（Finished/Cancelled/Excluded/DidNotFinish/Disqualified）を新設。着順欄の「取消」「除外」「中止」「失格」を、着順パース失敗と区別される正常な特殊状態としてモデル化。`FinishPosition` を状態依存のnullableに変更。
- **依頼書31節（Parser例外の分類）**: `JraPageParseException` を非sealedにし、`JraPageStructureException` / `JraUnexpectedValueException` / `JraValueParseException` / `JraRaceIdentityMismatchException` / `JraResultConsistencyException` を追加。`FieldName` / `RawValue` を保持し、ログ・テストから参照可能にした。
- 上記に対応する単体テスト（特殊状態、性齢分解、天候/馬場/着順欄の未知値エラー、RaceId不一致）を `RaceResultPageParserTests` / `JraRaceResultCollectionWorkflowTests` に追加。

既存実装の確認事項（変更不要と判断）:

- 依頼書30節「部分保存を避ける」「宣言→エントリー登録の順序維持」は、既存の `DeclareRaceResultBulkAsync`（1回のAPI呼び出しでレース宣言→各馬着順登録をAPI側がアトミックに順序保証）で既に満たされている。
- 依頼書3.3節「RaceCard取得失敗の許容」は、既存の `JraRaceCardCollectionWorkflow` がレース単位の try/catch でRaceCard失敗を握りつぶし、Collector全体を落とさない設計に既になっている。

### Phase 2（実装済み・本コミット）

- **依頼書3.1・3.2・5節（RaceCardLookupPeriod）**:
  - `IJraNavigator` に `IsWithinRaceCardLookupPeriod(DateOnly date)` を追加し、`JraNavigator` 側で `対象日 >= 今日 - RaceCardLookupPeriodDays`（既定5日）を判定する実装 `IsWithinRaceCardLookupPeriod` を追加した。値は `private const` ではなく、内部コンストラクタ引数 `raceCardLookupPeriodDays`（既定値は `internal const int DefaultRaceCardLookupPeriodDays = 5`）として持たせ、テスト・将来の設定変更で差し替え可能にした（依頼書3.1節の「`CurrentRacePeriod`のような意味の強い名称を避け、期間は後から容易に変更できるようにする」という明示的要求に対応）。
  - `JraNavigator.ToRaceCardAsync` と `JraRaceCardCollectionWorkflow.CollectAsync` の両方でこの判定を使い、対象日がRaceCardLookupPeriodより古い場合は出馬表探索（レース一覧取得含む）自体を早期にスキップする。スキップは既存の「RaceCard取得失敗の許容」（依頼書3.3節、`NotYetPublished`と同様の空結果 `RaceCardCollectionResult(date, course, [], [], [])`）と同じ扱いにし、Collector全体を失敗させない。
  - 「出馬表の有無を最終判定とする」（依頼書3.2節）は元々 `ToRaceListAsync` が返すレース一覧に対象レースが存在するかどうかで最終判定しており（`JraNavigator.ToRaceCardAsync` 内の `raceList.Races.SingleOrDefault(x => x.Id == race)`）、曜日・開催カレンダーからの別判定は追加していない。今回追加したRaceCardLookupPeriodはあくまで「探索を試みるかどうか」の事前フィルタであり、最終判定を代替するものではないことをコード上のコメントで明示した。
  - 依頼書5節の「RaceCard探索の5日をRaceResult Navigationの分岐条件として流用しない」制約は、`IsCurrentRacePeriod`（±3日）/ `IsRecentRacePeriod`（±92日）と `IsWithinRaceCardLookupPeriod`（既定5日）が完全に別のフィールド・別のメソッドであることに加え、`IJraNavigator` インターフェースのXMLコメントおよび `JraNavigator` 内の各フィールド定義コメントで明示的に相互不流用を注記した。
- **依頼書4節（過去レースでのRaceEntry相当情報復元）**: 調査の結果、`RaceResultPageParser` は既にHorseNumber/HorseName/JockeyName/Time/ResultStatus/FinishPosition/Sex/Age（依頼書14・15節の一部）を解析できていたが、`RaceResultEntry` モデル自体が持つ `FrameNumber`/`AssignedWeight`/`TrainerName`/`Popularity`/`BodyWeight`/`BodyWeightChange` は**Parserが実際には値を設定しておらず常にnull**、かつ `JraRaceResultCollectionWorkflow` から `IDataCollectionWriteService` への送信経路（`RaceResultBulkEntry`）自体にHorseName/JockeyName/性齢/斤量等を渡すフィールドが存在しなかった（`HorseNumber`と着順・タイム・着差・異常区分・賞金のみ）。このため、RaceCardを経由しない過去レースでは出走馬の氏名・所属といった識別情報がAPI側へ渡らず、依頼書4節が求める「RaceEntry相当情報の復元」は**未達**と判断した。対応として:
  - `RaceResultBulkEntry`（`HorseRacingPrediction.ApiClient`）に、既存の `UpsertRaceEntryAsync` と同じ命名・null許容パターンで `HorseName`/`JockeyName`/`TrainerName`/`GateNumber`/`AssignedWeight`/`SexCode`/`Age`/`Popularity`/`BodyWeight`/`BodyWeightChange` を追加（すべて末尾の省略可能パラメータとして追加したため、既存呼び出し側の互換性は維持）。新しい非一括APIは設計せず、既存の一括登録エンドポイントの入力を拡張する方向とした（Non-goalsの制約に整合）。
  - `HorseSex` に `HorseSexText.ToSexCode` を追加し、`JraRaceResultCollectionWorkflow` で `RaceResultEntry` の解析済み属性（現状Parserが実際に埋めるのは HorseName/JockeyName/Sex/Age のみ）を `RaceResultBulkEntry` へ渡すよう変更した。
  - **既知の残課題（意図的に未実装、推測で実装しなかった箇所）**: `FrameNumber`（枠番）・`AssignedWeight`（斤量）・`TrainerName`（調教師名）・`Popularity`（人気）・`BodyWeight`（馬体重）は、`RaceResultPageParser`側でまだ列検出・値解析を実装していない（`RaceResultEntry` のコンストラクタ引数としては受け皿があるが常にnullのまま）。依頼書33節の「実装時に未考慮パターンを発見した場合は想像で補完しない」方針に基づき、これらの列がレース結果ページ上でどのヘッダー文字列・セル書式（例:「馬体重」列が実際に何と表記されるか、増減の括弧表記の有無）で出現するかを実サイトで確認できていない本セッションでは、推測でのregex追加を避けた。RaceCard（出馬表）が別途取得できるケースでは、これらの属性は既存の `UpsertRaceEntryAsync`（`JraRaceCardCollectionWorkflow`）経由で登録されるため実運用上の欠落は限定的（出馬表を経由しない明確な過去レースでのみ欠落）。実サイトの列構造を確認できた時点でParser側の列検出・テストを追加するフォローアップとする。

### Phase 3（実装済み・本コミット）

- **依頼書12節（Course構造）**: `RaceCourseSpec`（`DistanceMeters` / `RaceType` / `Surfaces`（`IReadOnlyList<CourseSurface>`）/ `Direction` / `Layout` / `RawLayout`）を新設。単純なTurf/Dirt/Jump enumへ押し込む実装をやめた。`RaceResultPageParser` は、依頼書に例示された4つの実表記
  - `1,600メートル（芝・左）` → `Surfaces=[Turf]`, `Direction=Left`
  - `1,400メートル（ダート・左）` → `Surfaces=[Dirt]`, `Direction=Left`
  - `2,890メートル（芝 外内）` → `Surfaces=[Turf]`, `Direction=null`, `Layout="外内"`
  - `3,000メートル（芝→ダート）` → `Surfaces=[Turf, Dirt]`（障害。RaceTypeはRaceNameに「障害」を含むかどうかで判定）
  のみをサポートする。ページ上に「数字＋メートル（…）」の形式自体が見つからない場合は「コース構造欄なし」の正常系（`CourseSpec=null`）として扱うが、見つかったのに芝/ダート以外の馬場種別や左/右以外の方向表記が出現した場合は`JraUnexpectedValueException`（FieldName=`Course.Surface`/`Course.Direction`）とし、黙って無視しない。`RawLayout`は括弧内の生文字列としてデバッグ用に保持する。
- **依頼書18・20節（降着・Margin・同着）**: 着順欄に「10(1位降着)」のように確定順位と元の入線順位が併記される表記を検出し、`FinishPosition`=確定後着順・`OriginalFinishPosition`=元の入線順位として分離する（`RaceResultEntry.OriginalFinishPosition`は既存フィールドを利用）。降着表現（「降着」を含む着順テキスト）を検出したのに元順位を解析できない場合は`JraResultConsistencyException`（FieldName=`OriginalFinishPosition`）とする。着差列（見出し「着差」）を新設し、`MarginRaw`に生文字列のまま保持（ハナ/アタマ/大差/同着等を数値正規化しない）。着差欄の値が「同着」の場合は新設した`RaceResultEntry.IsDeadHeat`をtrueにする。1着・取消・除外・中止・失格では着差なしを正常とするが、着差列自体は存在するのに通常完走の2着以下で値が空の場合は`JraResultConsistencyException`（FieldName=`MarginRaw`）とする（着差列自体がページ構造として検出できない場合は、この不整合チェックの対象外とし、正常系として扱う＝列検出の未対応と値の欠損を区別するため）。
- **依頼書21・22節（推定上り・平均1F・ハロンタイム）**: 列見出し「推定上り」「平均1F」をそれぞれ`RaceResultEntry.EstimatedLast3F`/`Average1F`として分離。障害レースに`EstimatedLast3F`を要求しない（列が存在しなければ単にnull）。ハロンタイム（分割タイム）自体のテーブル構造は実サイトで確認できていないため、本フェーズでは着手していない（Deviations参照）。
- **依頼書23節（コーナー通過順位）**: `CornerPassage`（`CornerNumber`/`OrderRaw`）を新設し、見出しに「コーナー」を含み「N コーナー」のようにコーナー番号を含む列をレース単位の可変長リスト（`JraRaceResultPage.CornerPassages`）として抽出する。コーナー数は固定せず、「必ず1〜4コーナーが存在する」といったValidationは行わない。列自体が見つからない場合はnull（正常系）。
- **依頼書24・25・26節（馬体重・人気・斤量・枠番・調教師）**: 列見出し「馬体重」「人気」「斤量」「枠番」「調教師」を新設し、`RaceResultPageParser`が実際に値を解析するようにした（Phase 2時点では受け皿はあったが常にnullだった）。
  - 馬体重: `482(0)`/`494(+2)`/`400`の3パターンを許容。値ありで正規表現に一致しない場合は`JraValueParseException`（FieldName=`BodyWeight`）。
  - 人気: 値がある場合`>= 1`を要求。0や非数値は`JraValueParseException`（FieldName=`Popularity`）。
  - 斤量: decimalとしてParse、正数を要求。値ありで解析不能なら`JraValueParseException`（FieldName=`AssignedWeight`）。
  - 枠番: 先頭の数字を抽出。値ありで解析不能なら`JraValueParseException`（FieldName=`FrameNumber`）。
  - いずれも列自体が存在しない場合はnull（正常な欠損）。
- **依頼書27・28節（賞金・払戻）**: 既存の賞金section解析（該当なし。Course spec同様、実サイトの賞金セクション構造は未確認のため本フェーズでは着手していない。Deviations参照）。払戻については、式別セルに値があるのに`WinPayouts`/`PlacePayouts`/`QuinellaPayouts`/`ExactaPayouts`/`TrifectaPayouts`のいずれにも属さない場合（未知券種）を`JraUnexpectedValueException`（FieldName=`PayoutType`）として検知するようにした（従来は黙ってスキップしていた）。また、払戻金額セルに値があるのに数値として解析できない場合も`JraValueParseException`（FieldName=`Payout.Amount`）とした（従来は黙ってスキップしていた）。
- **API送信経路（`RaceResultBulkEntry`/`JraRaceResultCollectionWorkflow`）**: 上記で新たに解析できるようになった`OriginalFinishPosition`/`IsDeadHeat`を`RaceResultBulkEntry`に追加して送信するようにした。また、`RaceCourseSpec`が取得できた場合は`RaceResultBulkRequest.SurfaceCode`/`DistanceMeters`/`DirectionCode`にも反映するようにした（Phase 2時点ではこれらは常にnullだった）。
- 上記に対応する単体テストを`RaceResultPageParserTests`に追加（コース表記4パターン＋未知表記エラー、平地/障害でのEstimatedLast3F/Average1F分離、同着、降着（正常系・元順位解析不能のエラー系）、着差欠損の異常系、馬体重・人気・斤量・枠番・調教師の解析、未知券種・払戻値解析不能のエラー系、同着・降着・取消・除外・中止・失格が同一レースに混在するケース）。

### Phase 4（未着手・テスト網羅）

- 依頼書32節に列挙された全Fixtureケース（同着・降着の複合、古い年代の券種差、DOM位置変更を模したFixture等）の網羅。Phase 1では特殊状態・性齢・未知値・RaceId不一致の基本ケースのみカバー済み。

## Decisions

- Phase分割は「サイレントな異常握りつぶしの解消」（依頼書6〜8・31節）を最優先とする。理由: 現在最も実害が大きいのは、パース失敗が正常データとして記録される事象（プレースホルダー行のAPI送信）であり、依頼書の他の節（Course構造分解、Margin正規化等）は情報の粒度を上げるものであってデータ破損には直結しないため。
- 既存の一括登録API（`DeclareRaceResultBulkAsync`）とその順序保証は変更せず維持する。依頼書30節が言及する「`DeclareRaceResultAsync` → `DeclareRaceEntryResultAsync`」という2段階の記述は、実装当時の別API形態を指している可能性があるが、現行の一括APIが同じ安全性（レース未宣言のままエントリー登録が先行しない）を満たしていることをコード確認済みのため、新たに非一括APIを起こす作業は行わない。
- Sex/ResultStatusなど「JRA既知集合外はエラー」とする列挙体は、`Unknown` メンバーを設けない（依頼書16節の明示的要求）。将来JRAが新しい表記を追加した場合はモデル・Parser・テストを拡張して対応する運用とする。

## Acceptance criteria

依頼書34節の1〜16項目をそのまま受け入れ基準とする。Phase 1完了時点のステータス:

| # | 基準 | 状態 |
|---|---|---|
| 1 | RaceCardとRaceResultのNavigationが独立している | **満たす（Phase 2で`RaceCardLookupPeriod`導入、RaceResult側の期間しきい値と分離をコメントで明示）** |
| 2 | 直近レースでは出馬表を優先的に探索できる | **満たす（Phase 2で`RaceCardLookupPeriod`により名称・境界を明確化。最終判定は出馬表への実在有無のまま）** |
| 3 | RaceCardが存在しなくても正常にRaceResultへ進める | 満たす（既存実装で確認済み） |
| 4 | 古いレースでは出馬表を探索しない | **満たす（Phase 2で`RaceCardLookupPeriod`より古い場合は探索自体を早期スキップ）** |
| 5 | 過去RaceResultからRaceEntry相当情報を復元できる | **一部満たす（Phase 2でHorseName/JockeyName/Sex/Ageの送信経路を追加。FrameNumber/AssignedWeight/TrainerName/Popularity/BodyWeightはParser側の列検出が未実装・実サイト未確認のためフォローアップ）** |
| 6 | RaceResult Navigationが結果系導線だけで完結する | 満たす（既存実装で確認済み） |
| 7 | RaceResultがRaceIdを自己検証する | **満たす（Phase 1で実装）** |
| 8 | 天候・馬場・結果状態等の未知値をエラーにできる | **満たす（Phase 1で実装、Course構造等は未対応）** |
| 9 | 取消・除外・中止・失格・同着・降着を正常に扱える | **満たす**（Phase 3で同着=`IsDeadHeat`、降着=`FinishPosition`/`OriginalFinishPosition`分離を実装。複数特殊状態混在のテストも追加） |
| 10 | 平地・障害のページ差異を扱える | **一部満たす**（Phase 3で`EstimatedLast3F`/`Average1F`分離、Course構造でのRaceType判定を実装。ハロンタイム自体の分割タイム抽出は実サイト未確認のため未着手） |
| 11 | 正常な欠損とParser異常を区別できる | **満たす**（天候/馬場/性齢/結果状態に加え、Phase 3で馬体重・人気・斤量・枠番・調教師・着差・払戻券種/金額でも「列/値なし＝null」と「値ありで解析不能＝エラー」を区別） |
| 12 | 払戻をRaceResultとして取得できる | **満たす**（既存の5券種抽出に加え、Phase 3で未知券種・金額解析不能の検知を追加） |
| 13 | 古いレースの券種差を正常に扱える | 一部満たす（券種section自体が存在しない場合は既存実装で正常扱い。古い年代の実際の券種差はFixtureで確認できておらず未検証。Phase 4で対応） |
| 14 | RaceResult全体のValidation完了前に部分保存しない | 満たす（既存実装で確認済み） |
| 15 | RaceResult宣言後にEntryResultを保存する既存順序を維持する | 満たす（既存実装で確認済み） |
| 16 | 特殊ケース・異常ケースを自動テストでカバーする | Phase 1範囲のみ。Phase 4で拡充 |

## Delivery plan

1. **Phase 1（完了）**: ResultStatus/HorseSex enum、天候・馬場状態・性齢の未知値エラー化、RaceId自己検証、例外分類、対応テスト。
2. **Phase 2（完了）**: `RaceCardLookupPeriod` の導入とNavigation層のドキュメント・命名整理、過去レースでのRaceEntry相当情報復元（HorseName/JockeyName/Sex/Ageの送信経路。FrameNumber等の列解析は実サイト未確認のためフォローアップ）。
3. **Phase 3（完了）**: `RaceCourseSpec`、Margin/OriginalFinishPosition/IsDeadHeat/コーナー通過順位/EstimatedLast3F・Average1F、馬体重・人気・斤量・枠番・調教師・払戻の厳格Parse（依頼書4節フォローアップのFrameNumber/AssignedWeight/TrainerName/Popularity/BodyWeight列解析を含む）。賞金section・ハロンタイム分割タイムは実サイト未確認のため未着手（Deviations参照）。
4. **Phase 4**: 依頼書32節のFixtureテスト網羅と例外種別・Fieldの検証強化。

各フェーズは独立してビルド・テスト可能な単位でコミットする。

## Verification record

- `dotnet --version`: `10.0.100`（本セッションでインストール。`/home/user/.dotnet` に配置、`dotnet-install.sh --version 10.0.100`）
- `dotnet build tests/HorseRacingPrediction.Scraping.Tests/HorseRacingPrediction.Scraping.Tests.csproj`: 成功、0 Warning / 0 Error
- `dotnet test tests/HorseRacingPrediction.Scraping.Tests/HorseRacingPrediction.Scraping.Tests.csproj --filter "FullyQualifiedName!~E2ETests"`: 成功、95件（実サイトE2Eテストは除外）
- `dotnet build`（ソリューション全体）: 成功、0 Warning / 0 Error

### Phase 2 検証（本コミット）

- `dotnet build`（ソリューション全体）: 成功、0 Warning / 0 Error
- `dotnet test tests/HorseRacingPrediction.Scraping.Tests/HorseRacingPrediction.Scraping.Tests.csproj --filter "FullyQualifiedName!~E2ETests"`: 成功、100件（Phase 1の95件 + Phase 2で追加した`RaceCardLookupPeriod`関連テスト5件。実サイトE2Eテストは除外）
- `HorseRacingPrediction.Collector.Tests` 側の `FakeJraNavigator`（`IJraNavigator`実装）にも `IsWithinRaceCardLookupPeriod` を追加したため、`HorseRacingPrediction.Collector.Tests` を含むソリューション全体のビルドが通ることを確認済み（`dotnet build`）。

### Phase 3 検証（本コミット）

- `dotnet build`（ソリューション全体）: 成功、0 Warning / 0 Error
- `dotnet test tests/HorseRacingPrediction.Scraping.Tests/HorseRacingPrediction.Scraping.Tests.csproj --filter "FullyQualifiedName!~E2ETests"`: 成功、118件（Phase 2の100件 + Phase 3で追加したCourse構造・同着・降着・着差・馬体重/人気/斤量/枠番/調教師・未知券種/払戻金額解析不能・複数特殊状態混在の18件。実サイトE2Eテストは除外）
- `RaceResultBulkEntry`/`RaceResultBulkRequest`（`HorseRacingPrediction.ApiClient`）へのフィールド追加（`OriginalFinishPosition`/`IsDeadHeat`、`SurfaceCode`/`DistanceMeters`/`DirectionCode`の実値反映）を含むため、ソリューション全体のビルドで依存プロジェクト（`HorseRacingPrediction.Collector` / `HorseRacingPrediction.Collector.Tests`等）に影響がないことを確認済み。

## Deviations and follow-up

- Phase 3〜4は本ドキュメント作成時点で未着手。着手時は本ドキュメントの「Technical impact」「Acceptance criteria」表を更新すること。
- 依頼書33節が求める「実装中に未考慮パターンを発見した場合は想像で補完しない」方針に基づき、Phase 3以降でFixtureにない表記（新しい天候表記、新しい券種等）を発見した場合は、本ドキュメントに調査結果を追記してからモデル・Parser・テストを拡張する。
- この環境（開発コンテナ）に `dotnet` SDKが未インストールだったため、`dotnet-install.sh` で `/home/user/.dotnet` へローカルインストールした。CI環境のSDKインストール手順とは独立しており、本コンテナ固有の対応。
- **Phase 2で確認した既知の残課題（Phase 3で対応済み）**: `RaceResultPageParser` は `FrameNumber`（枠番）・`AssignedWeight`（斤量）・`TrainerName`（調教師名）・`Popularity`（人気）・`BodyWeight`/`BodyWeightChange`（馬体重）の列を解析していなかった。Phase 3で、依頼書14・24・25・26節に文字通り現れるJRA既知用語（枠番/斤量/調教師/人気/馬体重）を列見出しとして検出する実装を追加した。「着差」（20節の概念）「推定上り」「平均1F」（21節に文字通り記載）も同様に、依頼書本文に明記された用語を列見出しとして採用した。これらは実サイトの生HTMLを直接確認したものではなく、依頼書（ユーザー自身が提示した仕様書）に記載された用語を確定した仕様として扱った点に留意されたい。
- **Phase 3で実サイト確認できず見送った箇所**:
  - **賞金section（依頼書27節）**: レース結果ページにおける本賞金セクションの実際のテーブル構造・見出し文字列が既存Fixture・依頼書本文のいずれにも具体的に記載されておらず、本セッションでは実サイトへのアクセスも不可能（環境固有のプロキシ制約）なため、実装を見送った。`RaceResultBulkRequest`側には`PrizeMoney`を送る経路がまだない。「賞金sectionなし＝正常」は現状何もしないことで自動的に満たされているが、「sectionあり＋Parse不能＝Error」の検知は未実装。
  - **ハロンタイム（依頼書22節）の分割タイムテーブル自体**: レース種別（平地/障害）に応じた「推定上り」「平均1F」という着順テーブル内の1列としての分離は実装したが、通常JRAページに別途存在する「ハロンタイム」区間分割タイムの独立したテーブル構造は、実際のヘッダー・行構成が確認できないため未実装。ただし本フェーズでは元々このテーブルを一切解析していなかったため、「障害でハロンタイムなしを正常とする」（依頼書22節の主旨）は「そもそも解析しようとしない」ことで結果的に満たされている。
  - **コーナー通過順位の列見出し文字列**: 「コーナー」という語を含み「Nコーナー」という数字を含む見出しを対象にする実装としたが、実際のJRA結果ページでこの列がどのような見出し文字列・セル書式（同着時の表記等）で出現するかは確認できていない。該当列が見つからない場合は正常（null）扱いになるため、実データで別の見出し表記だった場合は静かに何も取得できない状態になる（エラーにはならない）。実サイト確認後、見出し検出条件とセル書式のテストを追加するフォローアップとする。
  - **古い年代の券種差（依頼書13・28節）**: 「ワイド」「三連複」等、現行5券種に含まれないが実在するJRA正式券種を新たに`RacePayouts`のバケットとして追加することはしなかった（Fixtureに具体的な出現例がなく、追加すると同時に「今回追加した券種以外は全部エラーになる」設計にすると却って壊れやすくなるため）。代わりに、未知券種が出現した場合に確実に`JraUnexpectedValueException`として検知できるようにした（黙って無視しない）。将来「ワイド」「三連複」等の実データ出現を確認した時点で、既知バケットとして追加する対応が必要。

---

## 付録: 元の変更依頼書全文

以下はユーザーから提示された依頼書の原文（本変更セットの実装仕様そのもの）。

# JRAスクレイピング層 Navigation / RaceResult取得仕様 変更依頼書

## 1. 目的

既存のJRAスクレイピング実装について、JRAサイト上の実際の導線およびレース結果ページのデータ構造に合わせて、Navigation・RaceCard・RaceResultの責務を見直す。

特に以下を変更する。

1. `Calendar -> RaceList -> RaceCard -> RaceResult` という単一の縦方向Navigationを前提にしない
2. 現在の出馬情報は「出馬表」から取得する
3. 確定したレース結果は「レース結果」から取得する
4. 明確な過去レースでは出馬表を探さず、RaceResultから出馬表相当情報も復元する
5. RaceCardの取得失敗は許容する
6. RaceResultの取得失敗・解析異常は原則エラーとする
7. RaceResult Parserでは、JRA上の正常な特殊ケースとParser破損を明確に区別する

既存のBrowser / Snapshot / Parser分離、`JraSession`、`JraNavigator`、`JraPageReader` 等の基本アーキテクチャは維持する。

---

## 2. Navigationの基本方針

JRAサイト上の機能ごとにNavigationを分離する。

| 目的 | 使用するJRA導線 |
|---|---|
| 開催予定の把握 | 開催日程 |
| 現在・直近の出走情報 | 出馬表 |
| 終了したレース | レース結果 |
| 少し前の結果 | 過去のレース結果 |
| 古い結果 | 過去レース結果検索 |

`Calendar -> RaceList -> RaceCard -> RaceResult` を必須経路として扱わないこと。
RaceCardとRaceResultは、それぞれ独立した情報源として扱う。

---

## 3. RaceCard取得方針

### 3.1 基本ルール

対象レースが直近の場合は、まず「出馬表」からRaceCardを探す。

RaceCard探索対象期間は、現時点では概ね以下とする。

```text
対象日 >= 今日 - 5日
```

ただし、この5日は「今週開催」を判定するルールではない。
あくまで、

> 古いレースについて無意味に出馬表を探索しないための最適化

として扱う。

期間は後から容易に変更できるようにする。
`CurrentRacePeriod` のような意味の強い名称は避け、`RaceCardLookupPeriod` 等の名称を使用すること。

### 3.2 出馬表の有無を最終判定とする

「今週開催」等を曜日や開催カレンダーから別途判定しない。
対象レースが現在の「出馬表」に存在するならRaceCardを取得する。
存在しない場合はRaceCard取得を諦め、RaceResult側へ進む。

### 3.3 RaceCard取得失敗

RaceCardは以下を正常系として許容する。

- 出馬表に対象レースが存在しない
- 一度取得したRaceCardと再取得時の内容が異なる
- 出走取消等によって内容が変化する
- JRA側から出馬表が消える

RaceCardは「取得できれば利用する情報」であり、RaceCardが取得できなかったことだけを理由にCollector全体を失敗させない。

---

## 4. RaceCardとRaceResultの責務

### 現在・直近レース

出馬表が存在する場合は、

```text
出馬表
  ↓
RaceCard
レース終了後
  ↓
レース結果
  ↓
RaceResult
```

として別々に取得する。

RaceCardとRaceResultは同じ`RaceId`に対する別ソースであり、保存時には同一Race / RaceEntryへ収束させる。
RaceCardには、例えば馬主等、RaceResultでは十分に取得できない情報が含まれるため、RaceResultで代替しない。

### 過去レース

明確な過去レースについて、過去の「出馬表」を探しに行かない。

```text
レース結果
  ↓
RaceResult
  ├─ 出馬表相当情報
  └─ 結果情報
```

とする。

`RaceResultParser` は着順等だけでなく、結果ページに存在する出走馬情報も解析すること。
過去レースではその情報を利用して`RaceEntry`を作成・更新し、出馬表相当の状態を復元する。

---

## 5. RaceResult Navigation

終了済みレースについては、結果系Navigationだけを使用する。

概念的なfallbackは以下とする。

```text
レース結果
    ↓ 見つからない
過去のレース結果
    ↓ 見つからない
過去レース結果検索
```

過去レース結果を取得するために、

- 開催日程
- 出馬表

を経由する必要はない。
RaceCard探索の「5日」という値をRaceResult Navigationの分岐条件として流用しないこと。
RaceResult側は、JRAサイト上で対象結果を取得できる経路を使用する。

---

## 6. RaceResult取得失敗の扱い

RaceCardとは異なり、RaceResultは原則として厳格に扱う。

終了済みレースとしてRaceResult取得を要求したにもかかわらず、

- 対象ページを取得できない
- 対象RaceIdと異なるページを取得した
- 結果ページの必須構造が存在しない
- 必須値を解析できない
- JRAとして未知の値が出現した
- 結果データ内に矛盾がある

場合はCollectorを失敗させる。
「何となく値を取得できた」状態で成功扱いにしない。

---

## 7. Parserの基本原則

RaceResult Parserは次の段階で処理する。

```text
PageSnapshot
    ↓
Raw値抽出
    ↓
JRA固有型へのParse
    ↓
RaceResult全体のValidation
    ↓
JraRaceResultPage
```

原則として`Unknown`へ丸めない。

以下を区別する。

| 状態 | 処理 |
|---|---|
| 仕様上optionalな要素が存在しない | null / empty |
| 要素が存在し既知値 | 正常Parse |
| 空欄が仕様上許容される | null |
| 必須要素が存在しない | Error |
| 未知の値 | Error |
| 既知項目だが形式不正 | Error |
| 値自体はParse可能だが他項目と矛盾 | Error |

特に、

> 「JRA上で値が存在しない」

ことと、

> 「Parserが値を理解できなかった」

ことを同一視しない。

---

## 8. Race Identity Validation

Navigationで要求した`RaceId`と、ページ自身から解析したRaceIdを必ず照合する。

```text
expected.Date   == parsed.Date
expected.Course == parsed.Course
expected.Number == parsed.Number
```

一つでも異なる場合はエラーとする。
HTMLとして正常なページが取得できていても、別レースなら成功扱いにしない。

---

## 9. レース基本情報Validation

以下を基本仕様とする。

| 項目 | 必須 | Validation |
|---|---:|---|
| Date | 必須 | DateとしてParse可能、RaceId一致 |
| RaceCourse | 必須 | JRA中央競馬場の既知集合、RaceId一致 |
| RaceNumber | 必須 | 1～12、RaceId一致 |
| RaceName | 必須 | trim後非空 |
| StartTime | 必須 | 時刻としてParse可能 |
| Weather | 必須 | JRA既知値 |
| Distance | 必須 | 正整数としてParse可能 |
| Course構造 | 必須 | 既知のJRAコース表現として解析可能 |
| MeetingNumber | 必須 | 正整数 |
| MeetingDay | 必須 | 正整数 |
| RaceConditions | 必須 | 取得・解析可能 |
| PrizeMoney | 任意 | 存在する場合はParse成功必須 |
| TrackCondition | 条件付き | 存在するsurfaceについてParse成功必須 |

---

## 10. 天候

JRA実データに基づき、少なくとも以下を既知値として扱う。

```text
晴
曇
小雨
雨
小雪
雪
```

内部ではenum等へ明示的に変換する。
未知値は`Unknown`へ変換せずエラーとする。

例えば以下はエラー。

```text
天候 = "不明"
天候 = "良"
天候 = "東京"
```

JRA側で将来新しい正式表記が追加された場合は、Parserとテストを更新して対応する。

---

## 11. 馬場状態

既知値は、

```text
良
稍重
重
不良
```

とする。
それ以外はエラー。

SurfaceとTrackConditionは分離する。

例えば、

```text
Surface = Turf
TrackCondition = 良
```

として扱う。

---

## 12. コース情報

単純な、

```text
Turf
Dirt
Jump
```

だけのenumへ押し込まない。

JRAには例えば、

```text
1,600メートル（芝・左）
1,400メートル（ダート・左）
2,890メートル（芝 外内）
3,000メートル（芝→ダート）
```

等の構造が存在する。

少なくとも概念的に、

```text
RaceCourseSpec
- DistanceMeters
- RaceType
- Surfaces
- Direction / Layout
- RawLayout
```

程度へ分解する。

障害では、

```text
芝→ダート
```

等、複数surfaceを通過するケースを許容する。
未知のコース構造を黙って無視しない。Parse不能ならエラーとする。
`RawLayout`はデバッグ・将来対応用として保持してよい。

---

## 13. RaceName

RaceNameは自由文字列として扱い、enum化しない。

例えば以下はすべて正常。

```text
3歳未勝利
3歳以上1勝クラス
障害3歳以上未勝利
○○特別
○○ステークス
```

Validationは過剰に行わず、

- null不可
- trim後空文字不可
- RaceNameを取得すべきDOMから取得できていること

を基本とする。

明らかなUI固定文言、

```text
レース結果
払戻金
開催選択へ戻る
レース選択へ戻る
```

等をRaceNameとして取得した場合はParser異常としてエラーにしてよい。
名前の文字種制限や既知レース名辞書との照合は行わない。

---

## 14. RaceEntry基本情報

各結果行について以下を解析する。

| 項目 | 基本方針 |
|---|---|
| HorseNumber | 必須、正整数、レース内一意 |
| FrameNumber | 必須、1～8 |
| HorseName | 必須、trim後非空 |
| Sex | 必須、既知値 |
| Age | 必須、正整数 |
| AssignedWeight | 必須、正数 |
| JockeyName | 必須、非空 |
| TrainerName | 必須、非空 |
| ResultStatus | 必須、既知状態 |
| FinishPosition | 状態依存 |
| Time | 状態依存 |
| Margin | 状態依存 |
| Popularity | nullable |
| BodyWeight | nullable |
| BodyWeightChange | nullable |
| EstimatedLast3F | 条件付き |
| Average1F | 条件付き |
| OriginalFinishPosition | 降着時 |

馬名・騎手名・調教師名に日本語文字限定等のValidationを設けない。
外国人騎手等を正常に扱えること。

---

## 15. 性齢

JRAの、

```text
牡6
牝5
せん5
```

等を、

```text
Sex
Age
```

へ分解する。

少なくとも、

```text
牡
牝
せん
```

を既知値とする。
未知の性別表現を`Unknown`へ丸めない。
解析不能ならエラー。

---

## 16. ResultStatus

少なくとも以下を明示的にモデル化する。

```text
Finished
Cancelled
Excluded
DidNotFinish
Disqualified
```

JRA表示との対応:

| JRA表示 | Status |
|---|---|
| 数字着順 | Finished |
| 取消 | Cancelled |
| 除外 | Excluded |
| 中止 | DidNotFinish |
| 失格 | Disqualified |

未知の状態文字列はエラー。
`UnknownResultStatus`は設けない。

---

## 17. FinishPosition

`FinishPosition`はnullableとする。

| Status | FinishPosition |
|---|---|
| Finished | 必須 |
| Cancelled | null |
| Excluded | null |
| DidNotFinish | null |
| Disqualified | null |

Finishedの場合は正整数であること。
ただし順位の一意性を要求しない。

同着では、

```text
1
2
3
3
5
```

のような順位を正常とする。
順位が完全な連番であることも要求しない。

---

## 18. 降着

降着は`ResultStatus`を別状態にしない。
確定後の着順を、

```text
FinishPosition
```

へ保存する。
元の入線順位が取得できる場合、

```text
OriginalFinishPosition
```

としてnullableで保持する。

例:

```text
Status = Finished
FinishPosition = 10
OriginalFinishPosition = 1
```

通常完走馬では`OriginalFinishPosition = null`。
降着表現を検出したのに元順位を解析できない場合はエラーとする。

---

## 19. Time

通常完走馬では必須。
JRAの走破タイム形式を明示的にParseする。

状態別ルール:

| Status | Time |
|---|---|
| Finished | 必須 |
| Cancelled | null |
| Excluded | null |
| DidNotFinish | null許容 |
| Disqualified | 有無どちらも許容 |

失格馬にタイムが存在することを異常扱いしない。

---

## 20. Margin

着差については現段階で過剰に数値正規化しない。

JRAには、

```text
ハナ
アタマ
クビ
1/2
1 3/4
大差
同着
(1位降着)
```

等の特殊表現が存在する。

初期実装では必要に応じて、

```text
MarginRaw
OriginalFinishPosition
IsDeadHeat
```

等として扱う。

1着馬では着差なしを正常とする。
取消・除外・中止等でも着差なしを正常とする。
通常完走の2着以下で着差が完全に取得不能な場合はParser異常の可能性が高いため、Validation対象とする。

---

## 21. 平地と障害の結果列差異

平地と障害を同一の列構成としてParseしない。

平地では、

```text
推定上り
```

を扱う。

障害では、

```text
平均1F
```

を扱う。

内部でも必要に応じて、

```text
EstimatedLast3F
Average1F
```

を分離する。

障害競走に`EstimatedLast3F`を要求しない。

---

## 22. ハロンタイム

障害競走ではハロンタイムが存在しないことを正常とする。

したがって、

```text
Flat
    → ハロンタイムを解析
Jump
    → ハロンタイムなしを許容
```

とする。

レース種別を考慮せずハロンタイム必須とする実装は禁止する。

---

## 23. コーナー通過順位

コーナー数を固定しない。

```text
CornerNumber
OrderRaw
```

等の可変リストとして扱う。

「必ず1～4コーナーが存在する」といったValidationは行わない。
取得されたコーナー番号・構造がJRAとして解析不能な場合のみエラーとする。

---

## 24. 馬体重

以下をnullableとする。

```text
BodyWeight
BodyWeightChange
```

例えば、

```text
482(0)
494(+2)
400
```

等を扱えるようにする。

JRA上で値自体が存在しないケースは正常になり得る。
ただし、

> 値が存在しているのに数値として解析できない

場合はエラーとする。
値なしをParser失敗として扱わない。

---

## 25. 人気

`Popularity`はnullableとする。
古いレース等で存在しないケースを許容する。

値が存在する場合は、

```text
Popularity >= 1
```

を要求する。
可能であればレース内出走頭数との整合性も確認する。
固定上限18等をhard-codeする必要はない。

---

## 26. 斤量

斤量はdecimal等の数値としてParseする。
値が存在するのに数値化できない場合はエラー。
正数であることを要求する。
50～65kg等の人工的な範囲制限は設けない。

---

## 27. 本賞金

レース賞金はRaceCard / RaceResultのどちらから取得してもよい。

RaceResultでは、

```text
賞金sectionなし
    → RaceResult自体は正常
賞金sectionあり
    ↓
正常にParseできる
    → 保存
賞金sectionあり
    ↓
Parse不能
    → Error
```

とする。

「存在しない」と「存在するが読めない」を区別する。

---

## 28. 払戻

払戻はRaceResultの責務とする。
券種は既知のJRA券種へ明示的にParseする。
ただし全レースについて現在の全券種が存在することを要求しない。
古い年代、発売条件、頭数等による券種差を許容する。

基本原則:

```text
券種なし
    → 正常になり得る
既知券種あり + Parse成功
    → 正常
券種らしきデータあり + 未知券種
    → Error
払戻値あり + Parse不能
    → Error
```

未知券種を無視しない。
将来JRAが券種を追加した場合にParser異常として検知できるようにする。
返還、特殊払戻等についても、JRA上の正式表現として確認できるものは正常系としてモデル化する。

---

## 29. RaceResult全体Validation

各フィールドのParse後、保存前にRaceResult全体をValidationする。

最低限以下を確認する。

```text
RaceIdが要求値と一致する
RaceNameが存在する
Weatherが既知値
Courseが正常に解析済み
結果行が1件以上存在する
各RaceEntryについて:
    HorseNumberが存在する
    HorseNumberが一意
    FrameNumberが正常
    HorseNameが存在する
    Sex/Ageが解析済み
    AssignedWeightが解析済み
    JockeyNameが存在する
    TrainerNameが存在する
    ResultStatusが既知
Finished:
    FinishPositionあり
    Timeあり
Cancelled:
    FinishPositionなし
Excluded:
    FinishPositionなし
DidNotFinish:
    FinishPositionなし
Disqualified:
    FinishPositionなし
降着:
    FinishPositionあり
    OriginalFinishPositionあり
```

同着によるFinishPosition重複は正常。
失格馬にTimeが存在しても正常。

---

## 30. 部分保存を避ける

RaceResult Parser / Validationが完了する前にAPIへの書き込みを開始しない。

```text
Parse
 ↓
Validation
 ↓
RaceResult完成
 ↓
API書き込み
```

とする。

途中まで解析できたRaceResultを部分的に保存してから後半で失敗する構造を避ける。

既存のRaceResult収集では、

```text
DeclareRaceResultAsync
    ↓
DeclareRaceEntryResultAsync
```

の順序を維持すること。
以前発生した、RaceResult未宣言のままEntryResultを書き込んでAPI 409になる問題を再発させない。

---

## 31. Parser例外

原因追跡が容易になるよう、可能であればParser異常を分類する。

概念的には、

```text
JraPageStructureException
    必須section/table/columnが存在しない
JraUnexpectedValueException
    天候等に未知値が出現
JraValueParseException
    既知項目の値を型へ変換できない
JraRaceIdentityMismatchException
    要求RaceIdとページが一致しない
JraResultConsistencyException
    Parse結果同士が矛盾
```

程度を検討する。
必ずしもクラス数をこの通り増やす必要はないが、ログ上で原因を区別できること。

ログには可能な範囲で、

```text
RaceId
URL
Parser
FieldName
RawValue
```

を含める。
HTML全文を通常ログへ出力しない。

---

## 32. テスト方針

RaceResultParserについて、正常な通常レースだけでなくJRA実データ上存在する特殊ケースをFixture化する。

最低限以下をテストする。

### 通常系

- 通常の芝レース
- 通常のダートレース
- 障害芝
- 障害芝→ダート
- 古い年代のレース

### 出走馬特殊状態

- 取消
- 除外
- 競走中止
- 失格
- 同着
- 降着
- 同一レースに複数特殊状態が存在

### 欠損正常系

- 取消馬にTimeなし
- 中止馬にFinishPositionなし
- 失格馬にTimeあり
- BodyWeightChangeなし
- Popularityなし
- 障害でハロンタイムなし
- 古い年代で現在の一部券種なし
- 賞金情報なし

### Parser異常系

- Weatherに未知値
- TrackConditionに未知値
- ResultStatusに未知値
- Sexに未知値
- RaceNumber不正
- 要求RaceIdとページRaceId不一致
- 必須tableなし
- 結果行0件
- HorseNumber Parse不能
- HorseNumber重複
- FinishedなのにFinishPositionなし
- FinishedなのにTimeなし
- Course構造が未知
- 払戻値が存在するのにParse不能
- RaceNameが空
- DOM位置変更を模したFixture

テストでは「Parserが例外になった」だけでなく、可能な範囲で例外種別と対象Fieldも確認すること。

---

## 33. 実装時の調査方針

今回の仕様はJRA実ページを基準としているが、実装中に既存Fixtureや追加のJRAページから未考慮パターンを発見した場合、想像で値を補完しない。

以下の順で対応する。

```text
未知パターン発見
 ↓
JRA上の正式な表現か確認
 ↓
正常なJRAデータならモデル・Parser・テストを拡張
 ↓
Parserの取り違えならParserを修正
```

正常な未知値を安易に`Unknown`として通す対応は禁止する。

---

## 34. 完了条件

以下を満たした時点で本変更を完了とする。

1. RaceCardとRaceResultのNavigationが独立している
2. 直近レースでは出馬表を優先的に探索できる
3. RaceCardが存在しなくても正常にRaceResultへ進める
4. 古いレースでは出馬表を探索しない
5. 過去RaceResultからRaceEntry相当情報を復元できる
6. RaceResult Navigationが結果系導線だけで完結する
7. RaceResultがRaceIdを自己検証する
8. 天候・馬場・結果状態等の未知値をエラーにできる
9. 取消・除外・中止・失格・同着・降着を正常に扱える
10. 平地・障害のページ差異を扱える
11. 正常な欠損とParser異常を区別できる
12. 払戻をRaceResultとして取得できる
13. 古いレースの券種差を正常に扱える
14. RaceResult全体のValidation完了前に部分保存しない
15. RaceResult宣言後にEntryResultを保存する既存順序を維持する
16. 上記特殊ケース・異常ケースを自動テストでカバーする

既存アーキテクチャを不必要に作り直さず、本変更に必要な範囲でモデル・Parser・Navigator・Workflow・テストを修正すること。
実装中に仕様上判断が必要な未知のJRA表現を発見した場合は、推測で吸収せず、その実データを確認した上で仕様へ追加すること。

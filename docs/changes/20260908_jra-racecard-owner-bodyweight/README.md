# JRA出馬表の馬主・馬体重取得修正

- Status: Proposed
- Owner: HorseRacingPrediction maintainers
- Created: 2026-09-08
- Updated: 2026-09-08

## Context

JRA出馬表から取得した馬主名と馬体重が保存結果へ正しく反映されない。2026-09-08に実サイトの
出馬表（2026-09-06 中山1R）をPlaywrightで取得し、文字列snapshotと対象セルの`outerHTML`を確認した。

対象セルは次の意味構造を持つ。

```text
馬名:       ヴェストラン                 class=name
単勝オッズ: 28.0                         class=odds / num
人気:       (6番人気)                    class=pop_rank
馬体重:     448kg(0)                     class=weight / transition
馬主:       中西 宏彰                    class=owner
生産者:     (有)ケンブリッジバレー       class=breeder
調教師:     本田 優(栗東)                class=trainer / division
```

ユーザー提示例の正しい対応は、馬主=`藤田 晋`、生産者=`ノーザンファーム`、
調教師=`武 幸四郎`、馬体重=`488`、増減=`-2`である。

現行処理には三つの問題がある。

1. `PlaywrightWebBrowser.GetCellTextAsync`は改行を保持するものの、CSS classを捨てて文字列だけを返す。
2. `RaceCardPageParser.ParseHorseNameCell`は数値だけのオッズ行（例:`28.0`、`10.7`）を除外しないため、
   「調教師以外の最初の候補」を馬主として選ぶ処理がオッズを馬主名にする。
3. parserは`448kg(0)`を除外するだけで`RaceEntry`へ格納せず、workflowも
   `declaredWeight`/`declaredWeightDiff`を常にnull、出走登録の`ownerName`も未指定で送信する。
   別途行う馬プロフィール更新だけでは、レース時点の馬主を表すEntryへ保存されない。

## Goals

- 馬名複合セルから馬主、生産者、調教師、馬体重、増減を混同せず抽出する。
- 馬体重と増減をレース出走の`DeclaredWeight`/`DeclaredWeightDiff`へ保存する。
- 馬主名を馬プロフィールとレース時点の出走スナップショットの両方へ保存する。
- DOMの意味情報が得られる実ブラウザーと、文字列だけの既存fixture/fake browserの双方を扱う。

## Non-goals

- 生産者を新しいドメインオブジェクトとして保存すること。
- 単勝オッズ・人気の永続化。
- RaceResultからの馬主復元。
- JRA以外のprovider向けselector追加。

## Documentation updates

- `docs/23-jra-scraping-redesign.md`: 汎用セルfragment metadataの責務、RaceEntryの馬主・馬体重、
  `AssignedWeight`との区別、馬主の二箇所保存を現在の設計として追記。本変更後のcanonical sourceとする。
- `docs/22-collector-design.md`と`docs/10-domain-design.md`を確認。Collector/API境界と時点付き馬体重の
  原則は既に記載されており、追加変更は不要。

## Technical impact

### Browser snapshot

- `PageTableSnapshot`へ、既存`Headers`/`Rows`を壊さない任意のセルmetadataを追加する。
- metadataはセルの正規化済み全文と、子孫要素のtag名・class token・正規化済みテキストを保持する。
- `PlaywrightWebBrowser`はDOMのclassを意味解釈しない。JRA固有の`owner`等を条件分岐に書かない。
- fragment数・文字数には上限を設け、前走欄など深いDOMでsnapshotが過大にならないようにする。
- `innerText`による改行付き`Rows`は後方互換用として維持する。

### RaceCard parser/model

- `RaceCardPageParser`はmetadataがある場合、exact class tokenの`name`、`owner`、`breeder`、
  `trainer`、`weight`、`transition`を優先する。部分一致は使わない。
- metadataがないfixtureでは文字列fallbackを使う。fallbackはオッズ単独行を数値形式で除外し、
  人気・馬体重・血統を除いた並びを、header記載順（馬主/生産者/調教師）として厳格に検証する。
- `RaceEntry`へ`BodyWeight`（int?）と`BodyWeightChange`（int?）を追加する。
- `NNNkg(+N)`、`NNNkg(-N)`、`NNNkg(0)`を解析する。欄自体がない場合はnull、値があるのに
  解析不能な場合は`JraValueParseException`とする。JRAの正式な欠損表記を実データで確認した場合は
  fixtureと仕様へ追加し、推測でnullへ丸めない。

### Workflow/API

- `JraRaceCardCollectionWorkflow`は`UpsertRaceEntryAsync`のowner対応overloadを使用し、
  `OwnerName`、`BodyWeight`、`BodyWeightChange`を同じ出走登録へ渡す。
- 現在馬主を更新する`UpsertHorseWithOwnerAsync`は維持する。出走登録との二つの意味を混同しない。
- 既存Entryに対する再収集がowner/馬体重を更新できるかを確認する。現行APIが既存Entryを即returnして
  更新不能なら、冪等upsertとして更新commandを追加するか、未更新を明示的エラーにする。黙って成功扱いにしない。

## Decisions

- 推奨案は、ブラウザー層で汎用fragment metadataを保持し、JRA parserがclassの意味を解釈する方式。
  DOM classを`PlaywrightWebBrowser`へ直書きせず、文字列の表示順推測だけにも依存しない。
- 生産者は馬主との位置関係を検証するため抽出するが、今回の保存対象にはしない。
- 馬主は現在プロフィールとレース時点Entryの両方へ保存する。管理UI設計上も両者は別の事実である。

## Acceptance criteria

- 提示例からOwnerName=`藤田 晋`、TrainerName=`武 幸四郎`、BodyWeight=`488`、BodyWeightChange=`-2`を得る。
- 実サイト例からOwnerName=`中西 宏彰`を得て、オッズ`28.0`や生産者名を馬主にしない。
- 法人馬主`(株)ネクストトライ`をそのまま保持する。
- `448kg(0)`、`456kg(+6)`、`488kg(-2)`を正しく分解する。
- optionalな馬体重欄なしと、値あり解析不能を区別する。
- workflowがEntryへOwnerName/DeclaredWeight/DeclaredWeightDiffを渡す。
- 馬プロフィールにもOwnerNameを保存する。
- 実サイトRaceCard E2Eで全Entryの馬主が数値・人気・生産者になっておらず、馬体重が取得できる。
- Browser、RaceCard parser、workflow、Collector/APIの関連テストとソリューションビルドが成功する。

## Delivery plan

1. 汎用table-cell fragment metadataとPlaywright抽出、単体テスト。
2. RaceEntry/RaceCard parserのsemantic優先解析と文字列fallback、fixtureテスト。
3. Workflow/API保存経路と再収集時の既存Entry挙動を修正し、統合テスト。
4. 実サイトE2E、全体ビルド・回帰テスト、検証記録更新。

## Verification record

- 設計調査時に一時診断テストで実サイトsnapshotと対象セル`outerHTML`を取得。診断テストは成果物へ残していない。
- 調査開始時点で`PlaywrightWebBrowser.cs`にユーザー変更（`Headless=false`）があり、変更していない。

## Deviations and follow-up

- 馬体重の正式な欠損表記は今回取得したレースに現れなかった。実装中に確認できなければ、既知形式だけを
  正常扱いし、未知表記は例外として観測可能にする。
- 生産者の永続化は別change recordで扱う。

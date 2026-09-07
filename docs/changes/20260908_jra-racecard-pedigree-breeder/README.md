# JRA出馬表の生産者・血統取得と馬プロフィール登録

- Status: Draft
- Owner: HorseRacingPrediction maintainers
- Created: 2026-09-08
- Updated: 2026-09-08

## Context

JRA出馬表の馬名セルには、馬主・馬体重に加えて生産者と血統が表示されている。
ユーザー提示例では次の値である。

```text
生産者: ノーザンファーム
父: アドマイヤマーズ
母: トレジャリング
母の父: Havana Gold
```

現行`RaceCardPageParser`は`breeder` fragmentを馬主との識別にだけ使い、父・母行を除外している。
`RaceEntry`およびHorse aggregate/APIにはこれらを渡すフィールドがないため保存できない。

## Goals

- 出馬表から生産者名、父名、母名、母の父名を項目境界を保って取得する。
- 取得値を現在の競走馬プロフィールへ登録し、再収集時にも非null値で更新する。
- Playwrightはテーブル全体を一括snapshot化し、JRA固有の意味解釈はparserで行う。
- 既存の馬主、馬体重、レース一覧リンク取得を維持する。

## Non-goals

- 父・母・生産者を独立aggregateとして登録し、IDで関連付けること。
- 血統表を3代以上へ展開すること。
- レース時点Entryへ生産者・血統を複製すること。
- JRA以外のprovider向け解析。

## DOM and parser design

`PageDomTextFragment`はclass、テキスト、hrefを保持している。classのない血統行も境界を失わず
取得できるよう、table snapshotの汎用対象へ`li`、`dt`、`dd`を追加する。上限48 fragment/セルは維持する。
Playwright側は`父`や`母`という文字列を解釈しない。

`RaceCardPageParser`は次の優先順位で読む。

1. 生産者はexact class token `breeder`。
2. 父・母はセルfragmentの独立した行から`父：`、`母：`を解析する。
3. `母：母名(母の父：母父名)`を母名と母父名へ分ける。全角・半角コロンと括弧周辺空白を許容する。
4. DOM metadataがないfixtureではセルの改行行に同じ規則を適用する。
5. 項目が存在しなければnull。接頭辞が存在するのに構造を解析できない場合は
   `JraValueParseException`として誤登録を防ぐ。

`RaceEntry`へ次を追加する。

```csharp
string? BreederName = null,
string? SireName = null,
string? DamName = null,
string? DamsireName = null
```

## Horse profile and API design

Horse profileへ文字列snapshotとして次を追加する。

```text
BreederName
SireName
DamName
DamsireName
```

父母をHorse IDで保持しない理由は、海外馬を含む親馬が当システムへ登録済みとは限らず、出馬表の
表示名だけで同一性を確定できないためである。今回の取得値はJRA表示のプロフィール文字列として扱う。

以下へ4項目を追加する。

- `HorseRegistered` / `HorseProfileUpdated` / `HorseState` / `HorseDetails`
- `RegisterHorseCommand` / `UpdateHorseProfileCommand`
- `HorseReadModel` / API request・response contracts / contracts read model
- EF Core migration

収集境界には既存メソッドを壊さない新overloadを追加する。

```csharp
Task<string> UpsertHorseProfileAsync(
    string registeredName,
    string? normalizedName,
    string? sexCode,
    string? birthDate,
    string? ownerName,
    string? breederName,
    string? sireName,
    string? damName,
    string? damsireName,
    CancellationToken cancellationToken = default);
```

既存`UpsertHorseAsync`と`UpsertHorseWithOwnerAsync`は互換用に維持し、新メソッドへ委譲する。
登録済みHorseへのPUTは既存と同じ非null patch semanticsとし、出馬表に項目がない再収集で既存値を消さない。
`JraRaceCardCollectionWorkflow`はEntry登録前に1回だけ`UpsertHorseProfileAsync`を呼び、馬主・生産者・
父・母・母父をまとめて渡す。

## Acceptance criteria

- 提示例から`ノーザンファーム`、`アドマイヤマーズ`、`トレジャリング`、`Havana Gold`を取得する。
- 法人格・空白・英字を表示どおり保持する。
- 生産者を馬主、父母を調教師として誤認しない。
- workflowから馬プロフィール登録へ馬主・生産者・父・母・母父が渡る。
- 新規登録と既存プロフィール更新の両方で4項目が保存される。
- 欠損値によって既存の非nullプロフィールを消さない。
- RaceCard parser、workflow、Horse domain/API/Collector、実サイトE2E、全体回帰テストが成功する。

## Delivery plan

1. DOM fragment対象とRaceCard parser/modelを拡張しfixture・実ブラウザーテストを追加。
2. Horse aggregate、commands、read model、API contracts、migrationを拡張。
3. Collector API clientとworkflowを接続し、新規・既存登録テストを追加。
4. 実サイトE2E、全体ビルド・回帰テスト、change record更新。

## Verification record

- 未実施（設計段階）。

## Deviations and follow-up

- なし。

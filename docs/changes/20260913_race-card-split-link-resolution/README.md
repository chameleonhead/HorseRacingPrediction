# 出馬表の分離リンク解決とURL保存

- Status: Implemented
- Owner: HorseRacingPrediction maintainers
- Created: 2026-09-13
- Updated: 2026-09-13

## Context

本番の`RaceCard/JRA/20260913:Nakayama:10`が、公開済みの一覧に10Rと出馬表URLが存在するにもかかわらず、`JraNavigationException: 10R のリンクが見つかりませんでした。`で繰り返し失敗した。ローカルの新規Playwrightセッションでも同じ例外を再現した。

実サイトでは、同一URLに対して`10レース`と`出馬表`が別々のリンク要素として存在する。既存実装とFixtureは、単一リンクのラベルにレース番号と`出馬表`が共存する理想化された構造を前提としていた。また一覧Parserは番号セル内の`出馬表`ラベル付きfragmentだけを参照するため、`RaceCardUrl`を未設定にしていた。

## Goals

- レース番号リンクと目的種別リンクが分離していても、同一URLで意味を結合してRaceCard URLを特定する。
- 一覧Parserが特定したRaceCard URLを`RaceSummary.RaceCardUrl`へ保持し、Discovery requestのExplicit URLとしてHTTPSに正規化して保存する。
- 単なる同一レース番号のオッズ・結果リンクをRaceCardとして誤選択しない。
- 新規ブラウザーセッションからのフルナビゲーションと、一覧表示済みのショートカット経路の両方を検証する。
- RaceResultの分離リンク構造も同じURL対応ロジックで扱えるか確認し、必要な横展開を行う。

## Non-goals

- JRAのURLをResource identityとして扱わない。
- URLのvolatile tokenを固定値として永続的な正しさの根拠にしない。
- Oddsのページ内導線やドメインSnapshot仕様は変更しない。

## Decisions

- 候補は`レース番号ラベルを持つリンク`と`目的種別ラベルを持つリンク`が同じ解決済みURLを共有する場合に採用する。レース番号だけのgeneric fallbackはフルナビゲーションでは有効化しない。
- 一覧Parserでは行内fragmentを横断し、同一URLの意味を結合する。行に必要情報がない場合はページ全体リンクからRace IDと同一URL対応を用いて補完する。
- NavigatorはParserが保持したURLを第一候補として直接Navigateし、遷移後にRaceCardかつ対象Race IDであることを検証する。ParserがURLを保持できない場合も同一URL対応で探索する。
- RaceResultにも同じ分離構造がある場合は共通選択ロジックを利用するが、目的ラベルと遷移後Page kind/identityを個別に検証する。
- リンク探索失敗の分類変更は、未公開との区別に追加情報が必要なため本変更では行わない。正常な公開済みリンクを見落とす原因を除去する。

## Acceptance criteria

1. `10レース`と`出馬表`が別要素で同一URLを持つ一覧をParseすると、10Rの`RaceCardUrl`が設定される。
2. `10レース`と`オッズ`だけが同一URLの場合、RaceCard URLとして採用しない。
3. 新規セッションから分離リンク構造の中山10R相当へ遷移し、RaceCardかつ対象Race IDとして検証される。
4. Discovery handlerがParserの相対RaceCard URLを発見元ページ基準のHTTPS URLへ変換してCollectionRequestへ渡す。
5. RaceResultの既存ナビゲーション回帰テストと実サイトSmokeが成功する。
6. 本番で対象RaceCardを再取得し、新規試行が成功するか、修正と無関係な外部障害の場合は診断情報を確認できる。

## Closure ledger

| Finding | State | Evidence |
|---|---|---|
| 分離リンクを同一URLで意味結合 | Closed | Parser/Navigator fixture tests（相対・絶対URL表現揺れを含む） |
| RaceCardUrl未設定 | Closed | Parser + Discovery sink assertion |
| オッズ等の誤候補防止 | Closed | ambiguous fixture test |
| cold/warm経路差 | Closed | fresh navigator + current-list tests |
| RaceResult/Odds横展開 | Closed | inventory and regression tests |
| 実サイト再確認 | Closed | current RaceCard / completed RaceResult smoke |
| 本番再確認 | Dependent | deploy後に対象ジョブを再取得 |

## Documentation updates

- `.codex/skills/learn-from-implementation-failures/SKILL.md`: 外部サイトのcold-session経路と、同一URLへ意味が分割された複数要素をFixtureで再現する検証ゲートを追加する。
- 既存アーキテクチャ文書の設計原則は変更しない。URLは取得候補であり、Resource identityではないという既存方針を維持する。

## Verification record

- 2026-09-13: 本番27試行を確認。最新は29.6秒後に`JraNavigationException`、詳細は`10R のリンクが見つかりませんでした。`。
- 2026-09-13: ローカル新規Playwrightセッションで同一Race IDを実行し、約12秒で同じ例外を再現した。
- 2026-09-13: 実サイト一覧に1R〜12Rがあり、`10レース`と`出馬表`が同一の`/JRADB/accessD.html?...`を持つ別リンクとして存在することを確認した。
- 2026-09-13: 一覧を事前表示した経路では同じ対象の取得に成功し、cold/warm経路差を確認した。
- 2026-09-13: `JraRaceLinkSelector`を導入し、レース番号と目的ラベルが別要素でも解決先URLが同じ場合に結合した。相対・絶対URLの表現揺れは比較時に正規化する。
- 2026-09-13: Parser/Navigator focused tests 51件、Discovery handler tests 9件、Externalを除くsolution tests 857件（成功856、skip 1）が成功した。
- 2026-09-13: 新規セッションの対象中山10R相当、現在週RaceCard、完了済みRaceResultの実サイト取得が成功した。
- 2026-09-13: Discovery handlerが相対RaceCard URLを発見元ページ基準のHTTPS URLへ変換して依頼する既存テストを再確認した。

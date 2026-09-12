# 収集状態の明確化と出馬表リンク選択の修正

- Status: Proposed
- Owner: HorseRacingPrediction maintainers
- Created: 2026-09-13
- Updated: 2026-09-13

## Context

本番の出馬表収集で、レース番号セル内の最初のリンクを出馬表URLと仮定した結果、実際にはオッズページへ遷移し、`RaceCard`要求が`RaceOdds`として識別された。既存fixtureはリンクを1件だけ持つ理想形で、実サイトE2Eは`External`として通常CIから除外されていたため発見できなかった。

また、`RaceResult/JRA/20260905:Sapporo:8`は、過去の障害が解決して収集成功済みであり、その後の新しい再取得Taskが実行待ちである。現画面は上部を「処理待ち」としながら、解決済みの過去Attemptを「最新の失敗」「要確認」と強調するため、現在の処理状態・保持データ・過去障害の区別がつかない。

## Goals

- 複数のリンク候補から意味的に出馬表リンクを選び、要求したResource種別・Race IDを終端で検証する。
- ページ種別不一致を汎用例外ではなく`UnexpectedPage`として、URLと実際のページ種別を保持する。
- 詳細画面で「現在の処理」「保持しているデータ」「未解決の障害」を別々に、一目で判断できる。
- 解決済み障害を現在の警告として表示しない。

## Non-goals

- SQS、Lambda、マイクロバッチ、メモリ設定は変更しない。
- 過去のAttemptやFailure履歴を削除しない。
- URLをResource identityとして扱わない。

## Experience and interaction design

詳細ページ上部を次の順にする。

1. Header statusは最新Taskの状態を表示する（例: `実行待ち`）。
2. Summaryは「現在の処理」「保存データ」「障害対応」の3項目を明示する。
3. 最新TaskがActiveなら、情報色の現在処理パネルを表示する。例: 「再取得を待っています」「前回取得済みのデータは引き続き利用できます」。
4. 赤い障害パネルはOpenまたはRecoveryInProgressのFailureに対応する失敗だけを表示する。
5. Resolved/Supersededの障害と古い失敗Attemptは履歴タブにのみ表示する。

表示例は [状態別ワイヤーフレーム](mocks/job-detail-status.md) を正とする。

## Mocks

- [状態別ワイヤーフレーム](mocks/job-detail-status.md)

## Documentation updates

- `.codex/skills/learn-from-implementation-failures/SKILL.md`: 外部アダプターfixtureの忠実度、曖昧候補、終端Resource検証、通常CIから除外されたlive testの扱いを再発防止ゲートとして追加した。
- 実装承認後は `docs/changes/20260913_collection-failure-investigation-ui/README.md` に、現在障害だけを強調する規則を追記する。

## Technical impact

- `RaceListPageParser`は単純な先頭anchor選択を廃止し、リンクのラベル・属性・URL特性から出馬表候補を選ぶ。判断不能な場合は候補URLを推測で採用せず、ナビゲーションによる探索へフォールバックする。
- `JraNavigator.ToRaceCardAsync`は遷移後に`JraRaceCardPage`かつ要求Race IDであることを検証する。
- `JobDetail`はAttempt単体ではなくFailure resolutionとTask時系列を合わせて現在警告を決定する。可能ならAPI ReadModel側で`CurrentTask`、`LastSuccessfulAt`、`ActiveFailure`を明示し、UIに状態推論を散在させない。

## Decisions

- `CollectionState`と`CollectionTask`は意味が異なるため一つの曖昧な「現在の状態」へ畳み込まない。Taskは現在の作業、Stateは保持データの鮮度として表示する。
- 過去に失敗があっても、そのFailureがResolvedなら赤い現在警告を表示しない。
- live E2Eを毎回CIへ追加するのではなく、production-shaped fixtureによる決定的テストを通常CIに置き、外部サイト変更に関わるリリースでは限定的なExternal smokeを実行・記録する。

## Acceptance criteria

1. レース番号セルにオッズリンクが先、出馬表リンクが後のfixtureでも出馬表を選択する。
2. 正しい候補を決定できない場合、先頭リンクを採用せず既存Discoveryへフォールバックする。
3. RaceCard要求がRaceOdds等へ到達した場合、URL・実ページ種別・要求Race IDを含む`UnexpectedPage`となる。
4. 指定されたSapporo 8Rは「再取得は実行待ち」「データは取得済み（最終取得日時）」「未解決障害なし」と表示される。
5. 解決済み`DispatchAttemptsExceeded`は赤い最新障害パネルに出ず、障害履歴には残る。
6. 未解決障害がある場合だけ赤い障害パネルを表示し、対応中は「再取得処理中」と区別する。
7. Taskなし・成功のみ・Active+過去成功・Open failure・Resolved failureのcomponent testsを追加する。
8. production-shaped parser fixture、Navigator終端検証、異種ページ分類のテストを通常CIで実行する。
9. 現在状態の意味がデスクトップと狭幅の両方で色だけに依存せず判別できる。

## Delivery plan

1. 実サイトのレース番号セルから必要なリンク属性をsanitized fixtureへ追加する。
2. 出馬表候補選択と終端検証、UnexpectedPage分類を実装する。
3. 詳細ReadModelに現在Task・Active Failure・最終成功を明示する。
4. JobDetailの状態Summaryと現在処理／障害パネルを再構成する。
5. parser/navigation/API/component testsと限定External smokeを実行する。

## Verification record

- 2026-09-13: CloudWatchで同一バッチの中山5R以降が`Destination=RaceCard ResolvedKind=RaceOdds`となり、その後`JraCollectionException: 出馬表を取得できませんでした`で完了していることを確認した。ページ読み込み・semantic snapshotは成功しており、Playwright資源不足ではない。
- 2026-09-13: parser unit testはレース番号セルにanchorが1件だけのfixtureで`RaceCardUrl`の転記だけを検証していた。実サイトE2Eは`TestCategory=External`で、CIは`TestCategory!=External`を実行するため対象外だった。
- 2026-09-13: Sapporo 8RはTask履歴が「実行待ち」「完了」「要対応」、StateはApplied/Required 1/1・最終取得16:47:59、Failure履歴は`DispatchAttemptsExceeded`が16:47:59に解決済みだった。現在の赤いパネルは解決済みFailureに属する過去Attemptを誤って現在障害として表示している。

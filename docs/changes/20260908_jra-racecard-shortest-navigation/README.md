# JRA出馬表の現在ページ認識と最短ナビゲーション

- Status: Draft
- Owner: HorseRacingPrediction maintainers
- Created: 2026-09-08
- Updated: 2026-09-08

## Context

現行`JraNavigator.ToRaceCardAsync`は、ブラウザーがレース一覧や同一開催の出馬表を既に表示していても、
毎回`競馬トップ → 出馬表 → 開催選択 → レース一覧 → 対象レース`を辿る。JRAの出馬表には同一開催の
レース番号、同日の別開催場、同一開催の別日へ直接切り替えるUIがあるため、現在ページを認識すれば
短い経路で移動できる。

## Goals

- `ToRaceCardAsync`の最初に現在ページを型付きページとして認識する。
- レース一覧・出馬表上のリンクまたはactionをクリックして最短で対象出馬表へ移動する。
- 最短経路の到達先を`RaceId`で検証し、不成立時だけ現行のフルパスへフォールバックする。
- JRAのセッション依存URLを不用意に再GETせず、表示中ページの操作を優先する。
- 既存の期間判定、例外分類、RaceResultナビゲーションを変更しない。

## Non-goals

- RaceResultの最短遷移。
- RaceCardLookupPeriodの変更。
- ページURLの永続キャッシュ。
- JRA以外のprovider対応。

## Current page recognition

`ToRaceCardAsync`は期間判定後、`_browser.CurrentUrl`がある場合に`_pageReader.ReadAsync`を1回呼び、
現在snapshotを既存parserで`JraRaceListPage`、`JraRaceCardPage`などへ分類する。新しいURL文字列推測は
追加しない。現在ページがすでに要求RaceIdの`JraRaceCardPage`なら操作せず返す。

現在ページのsnapshot取得・parseに成功しても利用可能な短縮経路がない場合はフルパスへ進む。
ページ構造異常を黙って正常扱いしないため、最短経路でクリックした後の到達ページがRaceCardではない、
またはRaceIdが不一致の場合だけ「短縮経路不成立」としてログへ残し、フルパスを再実行する。

## Route selection

### RaceList → RaceCard

現在ページが要求RaceIdと同じ日・競馬場の`JraRaceListPage`なら、対象`RaceSummary`の存在を確認し、
現在表示中ページにある対象レース番号リンクをクリックする。snapshotに保持した`RaceCardUrl`は対象リンクの
特定・診断に使うが、セッション依存URLの再GETはしない。

```text
RaceList(date=A, course=X) → click target race number → validate RaceCard(target)
```

### RaceCard → same date and course

現在ページが同日・同競馬場の別RaceCardなら、ページ上部の対象レース番号をクリックする。

```text
RaceCard(A/X/1R) → click 2R → validate RaceCard(A/X/2R)
```

### RaceCard → same date, different course

対象競馬場名のリンク/actionをクリックする。JRAが現在のレース番号を維持して対象競馬場のRaceCardへ
遷移した場合はRaceIdを確認して完了する。レース一覧または別レース番号へ遷移した場合は、続けて対象の
レース番号をクリックする。

```text
RaceCard(A/X/1R) → click course Y
  → RaceCard(A/Y/1R) when target is 1R: done
  → otherwise click target race number → validate
```

### RaceCard → different date in the displayed meeting

対象日を表すリンク/action（`M月D日`を優先）をクリックする。遷移後に対象RaceIdなら完了し、レース番号が
異なる場合またはレース一覧へ移動した場合は、続けて対象レース番号をクリックする。

```text
RaceCard(A/X/1R) → click date B
  → RaceCard(B/X/current R) or RaceList(B/X)
  → when needed click target race number → validate
```

「同一開催」は同じ競馬場で、JRA画面上に対象日切替UIが存在する範囲を指す。画面から対象日を選べない場合、
開催回番号を日付から推測せずフルパスへフォールバックする。

### Other cases

現在ページがUnknown、Calendar、別日かつ別競馬場、または必要なリンク/actionがない場合は、現行の
`ToRaceListAsync → 対象レース`経路をそのまま使う。

## Click and validation API

`JraNavigator`へprivate helperを追加する。

```csharp
Task<JraRaceCardPage?> TryNavigateFromCurrentPageAsync(
    IJraPage currentPage,
    RaceId target,
    CancellationToken cancellationToken);

Task<bool> TryClickRaceNumberAsync(int raceNumber, CancellationToken cancellationToken);
Task<bool> TryClickCourseAsync(RaceCourse course, CancellationToken cancellationToken);
Task<bool> TryClickDateAsync(DateOnly date, CancellationToken cancellationToken);
Task<JraRaceCardPage?> ReadAndValidateRaceCardAsync(RaceId target, CancellationToken cancellationToken);
```

クリック候補は現在snapshotの`PageLinkSnapshot`/`PageActionSnapshot`の表示テキストを使う。候補の探索と
JRA固有ラベル解釈はNavigatorで行い、`PlaywrightWebBrowser`へ競馬場名・日付・レース番号を直書きしない。
既存`IWebBrowser.ClickAsync`を利用する。

## Failure and fallback

- 対象リンク/actionがない: フルパスへフォールバック。
- クリック後のページ種別またはRaceIdが不一致: 警告ログ後、フルパスへフォールバック。
- cancellation: 即時伝播し、フォールバックしない。
- 期間外: 現行どおり操作前に`OutOfDisplayedRange`。
- フルパスも失敗: 現行の`JraNavigationException`を呼び出し元へ返す。

## Acceptance criteria

- 現在表示中ページを既存parserで認識してから経路を選ぶ。
- 同一RaceCardへの要求はクリック・Navigateを行わない。
- 同一日・競馬場のRaceListからレース番号クリック1回でRaceCardへ移動する。
- 同一日・競馬場のRaceCard間をレース番号クリック1回で移動する。
- 同一日の別競馬場は競馬場クリック後、必要な場合だけレース番号をクリックする。
- 同一競馬場の別日は日付クリック後、必要な場合だけレース番号をクリックする。
- 最短経路で誤ったRaceCardへ到達した場合は成功扱いせずフルパスで回復する。
- 既存のフルパス、RaceResult、期間判定テストが成功する。
- Fake browserによる各経路テストと、利用可能な開催に対する実サイト連続RaceCard E2Eが成功する。

## Delivery plan

1. 現在ページ認識と到達RaceId検証helperを追加。
2. RaceList、同一開催RaceCard、別競馬場、別日の短縮経路を順に追加。
3. 各経路とフルパスfallbackの単体テストを追加。
4. 実サイトで同一開催の複数レースを連続取得し、実行した操作・所要時間を検証記録へ追記。

## Verification record

- 未実装。承認後に記録する。

## Deviations and follow-up

- JRA画面で別開催場・別日の切替後に現在レース番号が維持されるかはページ状態に依存するため、必ず到達
  RaceIdを読み直し、必要なときだけレース番号を追加クリックする。

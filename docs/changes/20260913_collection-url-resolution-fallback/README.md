# 収集URLの正規化とDiscoveryフォールバック

- Status: Implemented
- Owner: HorseRacingPrediction maintainers
- Created: 2026-09-13
- Updated: 2026-09-13

## Context

RaceResult `20260906:Sapporo:7`のDiscovery依頼に、JRAページから得た相対URL `/JRADB/accessS.html?...` がWindowsの`Uri.TryCreate(..., UriKind.Absolute)`によって`file:///JRADB/...`として保存されていた。Workerは非HTTP URLを拒否してDiscoveryへフォールバックしたが、その後のJRAトップ遷移で`ERR_INSUFFICIENT_RESOURCES`が発生したため最終的に失敗した。

## Goals

- 発見した相対URLを発見元ページのHTTP(S) URLで解決してからCollectionRequestへ渡す。
- `file:`などHTTP(S)以外のURLをExplicit URL・ResourceLocation候補として保存または実行しない。
- URL候補が不正・取得不能・別Resourceの場合でも、通常のResource Discoveryへフォールバックする。
- URL候補の失敗とDiscoveryの最終失敗を診断可能なまま保持する。

## Non-goals

- Playwright/Lambdaのメモリ設定は変更しない。
- 過去に保存されたCollectionRequest履歴を削除または書き換えない。
- JRAの相対URLを永続identityとして扱わない。

## Decisions

- URL解決は`new Uri(baseHttpUri, relativeOrAbsolute)`相当で行い、最終schemeが`http`または`https`の場合だけ候補として採用する。
- API/store境界でもHTTP(S)制約を検証し、上流の誤変換が永続化されないよう多層防御する。
- Candidate loopが失敗してもRaceResult/RaceCard handlerは既存Discovery workflowを実行する。候補失敗だけでTaskを終端失敗にしない。
- 既存の`file://`依頼履歴は監査履歴として残す。再取得時は通常経路から正しい候補を再発見する。

## Acceptance criteria

1. `/JRADB/...`を発見したRaceResult requestには`https://www.jra.go.jp/JRADB/...`が保存され、`file://`にならない。
2. `file:` URLを管理APIへ指定した場合はvalidation errorとなりTask候補へ入らない。
3. 既存DBに非HTTP候補が存在してもAcquire時に実行候補へ含めない。
4. 無効な候補の後にDiscovery workflowが実行され、成功できる。
5. Windows/Linuxに依存しないURL解決テストを通常CIで実行する。

## Verification record

- 2026-09-13: 本番依頼履歴8件中Discovery由来7件に`file:///JRADB/accessS.html...`を確認した。
- 2026-09-13: 同Resourceの恒久的失敗は、候補処理後のDiscoveryでJRAトップへ遷移した際の`PlaywrightException: net::ERR_INSUFFICIENT_RESOURCES`であり、`file://`自体が最終例外ではないことを確認した。
- 2026-09-13: ユーザーからURL取得不能時のフォールバック対応依頼を受け、Execution Modeで実装を開始した。
- 2026-09-13: Discoveryで得た相対URLを発見元ページ基準で解決し、API・Store・Location登録でHTTP(S)のみを許可した。
- 2026-09-13: Acquire/Resolve時は既存DBの非HTTP LocationおよびExplicit URLを候補から除外するようにした。監査用の依頼履歴は変更していない。
- 2026-09-13: RaceCardに加えRaceResultでも、候補のページ種別が不正な場合にDiscovery workflowへ戻って成功するテストを追加した。
- 2026-09-13: `dotnet build HorseRacingPrediction.sln -c Release --no-restore` 成功（警告0、エラー0）。
- 2026-09-13: `dotnet test HorseRacingPrediction.sln -c Release --no-build --filter "TestCategory!=External"` 成功（736件成功、1件スキップ）。
- 2026-09-13: `JraSiteE2ETests.完了済みRaceResult取得` の実サイトテスト成功（1件、13秒）。
- 2026-09-13: `git diff --check` 成功。

## Acceptance criteria traceability

| # | Production path | Status |
|---|---|---|
| 1 | Race discovery → source-page基準URL解決 → request sink → store | Verified |
| 2 | 管理API → HTTP(S) validation → store | Verified |
| 3 | Store Acquire/Resolve → legacy URL filtering → worker candidate list | Verified |
| 4 | RaceResult candidate validation failure → discovery workflow → domain result | Verified |
| 5 | OSに依存しない文字列入力によるroot-relative URL単体テスト | Verified |

## Documentation updates

本修正は承認済み収集基盤の「URLは候補でありDiscoveryへフォールバックする」という既存方針を実装上補強するものであり、change record以外の設計・運用ドキュメント更新は不要と判断した。

## Implementation notes

- 設計との差分なし。
- 過去の`file://`依頼履歴は原因調査用に保持するが、再実行候補には使わない。
- Playwrightのメモリ設定は変更していない。今回観測した`ERR_INSUFFICIENT_RESOURCES`は別の実行時障害として、既存のretry/recovery対象になる。
- 残課題なし。

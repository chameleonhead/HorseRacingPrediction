# 未来開催レース探索の公開待ち扱い

- Status: Implemented
- Owner: HorseRacingPrediction maintainers
- Created: 2026-09-12
- Updated: 2026-09-12

## Context

開催レース探索は通常依頼で基準日の前後7日を走査する。JRAカレンダーに開催予定が掲載済みでも、未来日のレース一覧が未公開の場合がある。現在の探索handlerはそのページ取得・識別失敗を日付の意味で分類せず、例外をtask全体の失敗へ伝播させるため、9月12日時点の9月19日開催分が運用画面の障害として表示される。

未公開はシステム障害でもデータ異常でもなく、予定された状態遷移である。過去日または当日について取得できない場合とは区別する。

## Goals

- 未来開催日のレース一覧未公開を `ResourceNotYetAvailable`（画面表示は「公開待ち」）として扱う。
- 公開を期待できる時刻まで待って通常経路で自動再試行する。
- 同じ探索窓内で既に発見できた開催日・レースのCollectionRequestは失わない。
- 過去日・当日の予期しないページ、parse不良、通信障害は従来どおり原因別に障害として検出する。

## Non-goals

- JRAの公開時刻を固定の外部仕様として保証しない。
- 未来レースを推測で作成しない。
- HTTP 200だけでレース一覧を成功扱いしない。

## Experience and interaction design

- 未来日が未公開の場合、収集管理の「要対応」には表示しない。
- 状態は「公開待ち」とし、次回予定日時を表示する。
- 公開予定を過ぎても取得できない場合は再試行を継続するが、無制限な短間隔再試行は行わない。
- 当日または過去日のページ不整合は「別ページを検出」「読み取り失敗」等として要対応に残す。

## Documentation updates

- `docs/changes/20260911_unified-collection-platform/README.md`: 実装完了後、未来日Discoveryの状態分類と本番検証結果を追記する。収集基盤全体のcanonical change recordとの関係を維持する。

## Technical impact

- `JraRaceDiscoveryCollectionHandler` で日付単位に未公開を分類し、未来日の一覧取得失敗をtask全体の恒久障害にしない。
- `CollectionAttemptCompletion.RetryAt` に、現在時刻・対象開催日・設定値から計算した次回確認時刻を設定する。
- 一部日付の発見成功後に未来日が未公開だった場合も、生成済みrequestは保持し、探索taskのみ公開待ちとして再実行可能にする。
- 日付境界はJSTで判定し、実行ホストのlocal timezoneへ依存しない。

## Decisions

- 未来日の未公開は成功ではなく「公開待ち」とする。成功にするとResourceType `Race` の現在policyでは再探索されず、公開後のレースを取り逃がすため。
- retry間隔は短い指数backoffではなく日付を考慮する。開催日まで十分遠い場合は翌日の設定時刻、開催前日以降は設定可能な間隔で確認する。
- 未来日以外の例外を一律に握りつぶさない。

## Acceptance criteria

1. JST基準で未来日の開催がカレンダーに存在し、レース一覧が未公開ならAttempt結果は `ResourceNotYetAvailable`、error codeは `RaceListNotYetAvailable` になる。
2. 上記taskはFailed/Unavailableにならず、Pending（公開待ち）となり、未来の`NextCollectionAt`を持つ。
3. 同じ走査で公開済み日から作成したRaceCard/RaceOdds/RaceResult requestは保持される。
4. 当日・過去日のUnexpected Pageまたはparse失敗は公開待ちへ誤分類されない。
5. timeout、429、503等は従来のTransient/AccessLimited分類を維持する。
6. 9月12日時点の9月19日相当fixtureで自動テストし、本番相当SQS/Lambda経路でAttemptと状態Projectionを確認する。
7. 管理画面では公開待ちが「要対応」の障害件数に含まれず、待機中として次回予定を確認できる。

## Delivery plan

1. 未来日一覧未公開を再現するhandlerテストを追加する。
2. JSTの日付判定とretry時刻policyを実装する。
3. handlerからStore完了処理・管理Projectionまでの結合テストを追加する。
4. Release build、Collector/APIテスト、実ブラウザーで表示を検証する。
5. デプロイ後、既存の9月19日探索を通常Recoveryし、「公開待ち」へ遷移することを確認する。

## Verification record

- 2026-09-12: 現行実装を調査。Discovery handlerは通常依頼で基準日±7日を逐次処理し、日付単位の未来日判定や未公開例外変換を行っていない。Storeは既に`ResourceNotYetAvailable`をretryableとしてPendingへ戻すため、主な修正点はhandlerの分類とretry時刻である。
- 2026-09-12: JST基準の未来日に`NotYetPublished`またはレース一覧以外のページを検出した場合、`ResourceNotYetAvailable / RaceListNotYetAvailable`と日付依存の`RetryAt`を返すようにした。前日以降は設定間隔、遠い開催日は翌日のJST設定時刻を使用する。
- 2026-09-12: 未公開日を検出しても探索窓の走査を継続し、後続の公開済み日から生成したRaceCard/RaceOdds/RaceResult requestを保持する。最初の未公開日で処理を終了する実装は採用しなかった。
- 2026-09-12: HTTP 503等と`JraPageParseException`は未来日でも公開待ちへ変換せず、既存の原因分類へ伝播させる。JST日付境界、当日Unexpected Page、後続日継続、設定可能な近日期間をhandlerテスト8件で検証した。
- 2026-09-12: API→outbox→SQS通知JSON→Worker→完了API→Storeの本番相当境界を通し、Taskが将来`AvailableAt`付きReady、StateがPendingかつ将来`NextCollectionAt`、Attemptが公開待ち、Failure通知が0件となることをE2Eテスト2件で検証した。
- 2026-09-12: `dotnet test tests/HorseRacingPrediction.Collector.Tests/HorseRacingPrediction.Collector.Tests.csproj -c Release --filter FullyQualifiedName~JraRaceDiscoveryCollectionHandlerTests --no-restore` は8件成功、失敗0件。`dotnet test tests/HorseRacingPrediction.Api.Tests/HorseRacingPrediction.Api.Tests.csproj -c Release --filter FullyQualifiedName~CollectionPlatformDiscoveryEndToEndTests --no-restore` は2件成功、失敗0件。

## Acceptance-criterion matrix

| # | Status | Evidence |
|---|---|---|
| 1 | Verified | 2026-09-12 JST・2026-09-19 fixtureでAttempt結果とerror codeを検証 |
| 2 | Verified | API/SQS/Worker E2EでReady task、Pending state、未来のAvailableAt/NextCollectionAtを検証 |
| 3 | Verified | 未公開日の前後に発見した3種類のrequestが保持され、後続公開日も走査されることを検証 |
| 4 | Verified | JST当日Unexpected Pageと未来日ParseFailureが公開待ちにならないことを検証 |
| 5 | Verified | HTTP 503をそのまま上位のTransient分類へ伝播することを検証。429/timeout分類は共通Worker異常系テストで検証 |
| 6 | Verified | 本番と同じ通知JSON、acquire/complete API、永続Storeを通るE2Eテストを実施 |
| 7 | Verified | Failure通知0件、待機task/state projection、次回予定をAPIで検証。既存詳細画面の公開待ち表示と次回予定表示を再確認 |

## Remaining operations

- 実AWS上のSQS/Lambda確認と既存9月19日taskのRecoveryは、統一収集基盤U10のmaintenance windowで実施する。ローカル実装の未完了項目ではない。

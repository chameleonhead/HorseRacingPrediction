# 過去レース結果検索ナビゲーションの安定化

- Status: Implemented
- Owner: Main agent
- Created: 2026-09-15
- Updated: 2026-09-15

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Complete | Historical経路限定の3回・500ms間隔の再探索と診断例外を実装 |
| Verification | Complete | navigation 44件、Scraping 245件、CI相当964件、実サイトE2E 1件が成功 |
| Deployment/operation | Not applicable | デプロイと失敗対象の再取得は承認済みNon-goals |

## Context

`Race:JRA:20260419:Nakayama:9` の `race-detail` 収集は、2026-09-15 08:44:16 JST の試行で
`InvalidOperationException: テキスト '過去レース結果検索' に一致するクリック可能要素が見つかりませんでした。`
となり、恒久的な失敗として記録された。

対象日は実行日の149日前であるため、`JraNavigator` は92日以内の直近開催経路ではなく
Historical経路を選択する。この経路は「レース結果 開催選択」ページへ遷移した直後に
`ClickAsync("過去レース結果検索")` を一度だけ実行する。`ClickAsync` はその時点の候補要素数を固定して
全候補を走査するため、JRA側のJS遷移後に本文要素の反映が遅れた場合、走査中に対象リンクが追加されても
候補へ含まれず失敗する。

2026-09-15の実サイト確認では、対象要素は以下の形で存在した。

```html
<a href="#" class="btn-def blue"
   onclick="return doAction('/JRADB/accessS.html', 'pw01skl00999999/B3');">
  過去レース結果検索
</a>
```

したがって固定文言そのものの変更ではなく、JS画面遷移とクリック対象探索のタイミング競合を第一原因として扱う。
ただし失敗記録に要求URL・最終URLが保存されていないため、異常ページへ到達した場合も同じ例外表示になるという
診断上の不足もある。

## Goals

- JRAのレース結果画面がJS遷移後に遅れて構築されても、過去レース結果検索へ到達できるようにする。
- 対象要素が最終的に存在しない場合は、現在URLを含むナビゲーション例外として記録し、運用画面から到達ページを判別できるようにする。
- 実サイトを模した遅延表示テストにより、今回の回帰を検出できるようにする。

## Non-goals

- JRAの検索フォーム、レース結果Parser、DB保存処理の仕様変更。
- 92日のCurrent/Recent/Historical判定基準の変更。
- デプロイ、既存失敗タスクの再実行、または本番データの変更。
- すべてのブラウザー操作へ一律の再試行を追加すること。

## Documentation updates

- `docs/23-jra-scraping-redesign.md`: Historical Race SearchはJS遷移後の遅延表示を考慮して待機し、失敗時に到達URLを残すという現在の設計判断を追記する。この文書をJRAナビゲーションの正規設計として維持する。
- `docs/changes/20260915_historical-race-search-navigation/README.md`: 障害固有の判断、受け入れ基準、実装・検証結果を記録する。

## Technical impact

- `JraNavigator.ToHistoricalRaceSearchAsync` に、対象リンクが現れるまでの短い上限付き再試行を追加する。
- 再試行はHistorical Race Searchの対象リンクに限定し、他のクリック操作の待機時間や失敗分類は変更しない。
- 上限到達時は `JraNavigationException` として、失敗理由、現在URL、対象文言を保持する。
- `FakeWebBrowser` または専用テストダブルで、初回は対象リンクなし、次回は出現するJS遅延相当の状態を再現する。

## Decisions

- 対象文言は2026-09-15の実サイトに現存するため変更しない。
- 汎用 `PlaywrightWebBrowser.ClickAsync` 全体の挙動は変更せず、実サイトで未検証だったHistorical経路に限定して回復処理を置く。影響範囲と待機時間の増加を限定するためである。
- 再試行はキャンセル可能かつ上限付きとし、無限待機や恒久失敗の隠蔽を行わない。
- URL欠落を解消するため、対象が現れない場合はドメイン固有のナビゲーション例外へ変換する。

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | 「レース結果」遷移直後には存在せず、短時間後に現れる「過去レース結果検索」を辿って対象レース結果へ到達できる | T1, T2 | 遅延表示を再現するナビゲーションテスト | Verified |
| AC2 | 待っても「過去レース結果検索」が表示されない場合、収集管理画面のエラー詳細で「どのJRAページで止まったか（URL）」と「何を探していたか」を確認できる | T1, T2 | リンクが最後まで表示されないケースで、エラーに現在URLと `過去レース結果検索` が含まれることを確認する | Verified |
| AC3 | 今週・直近のレース結果収集と、従来の過去年月を指定する検索が、修正前と同じ手順で引き続き成功する | T2 | 今週、直近、過去年月検索の既存ナビゲーションテストを実行する | Verified |
| AC4 | 実サイトの「レース結果 開催選択」から「過去レース結果検索」へ遷移できる | T3 | 明示有効化されたJRA E2Eまたは手動実サイト確認 | Verified |

## Delivery plan

1. Historical経路専用の待機・例外変換を実装する。
2. 遅延表示と恒久欠落のテストを追加する。
3. 関連テスト、format、build、CodeGraph同期、実サイト確認を実行する。
4. change recordへ証拠を記録して最終レビューする。

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | Historical検索リンクの上限付き再試行と診断可能な例外を実装（AC1, AC2） | Main | Lead tier | 承認 | `src/HorseRacingPrediction.Scraping/Jra/Navigation/` | T2のテスト | 3回・500ms間隔の再探索とURL付き例外 | Verified |
| T2 | 遅延表示、恒久欠落、既存経路の回帰テストを追加（AC1–AC3） | Worker | Worker tier | T1の契約確定 | `tests/HorseRacingPrediction.Scraping.Tests/Navigation/` | `dotnet test` | navigation 44/44、Scraping 244/244成功 | Verified |
| T3 | 統合検証、実サイト確認、文書更新、最終監査（AC3, AC4） | Main | Lead tier | T1, T2 | change record、CodeGraph（派生物） | format/build/test/E2E | format/build/CI相当全テスト/E2E成功 | Verified |

## Review gates

- **Design and task-split review** — Reviewer: Main agent. Inputs: 本番障害表示、現在のJRA実サイトDOM、`JraNavigator`、`PlaywrightWebBrowser`、既存FakeWebBrowserテスト。Decision: 文言変更ではなくHistorical経路限定の上限付き再試行と診断改善を採用。AC1–AC4はT1–T3と検証へ相互に追跡済み。T1とT2は実装契約を先に確定して直列化し、共有ファイルへの並列書き込みを避ける。Follow-up: ユーザー承認後にPre-implementation reviewを記録する。
- **Pre-implementation review** — Reviewer: Main agent. Inputs: 2026-09-15のユーザー承認、確定済みAC1–AC4、`JraNavigationException`、`IWebBrowser.ClickAsync`、既存テストダブル。Decision: T1とT2をRunnable、統合を担うT3をDependentとした。実装契約はHistorical検索リンクに限定したキャンセル可能な3回・500ms間隔の再試行とし、最終失敗は現在URLと対象文言を含む`JraNavigationException`へ変換する。T1はproduction navigation、T2はnavigation testsのみを所有し、書込範囲は重複しない。Workerは仕様矛盾、テストダブルのproduction変更要求、または検証失敗時にMainへエスカレーションする。Follow-up: T1/T2完了後、Mainが差分とテスト証拠を統合レビューする。
- **Checkpoint review** — Reviewer: Main agent. Inputs: T1 production差分、T2のテストダブル・2テスト差分、navigation 44/44、Scraping 244/244、実サイトE2E。Decision: Worker差分は書込範囲内で、既定のFakeWebBrowser挙動を維持しつつAC1/AC2を再現している。T1は文字列一致以外の`InvalidOperationException`を再試行しないため、無関係な障害を隠蔽しない。Follow-up: CI相当ゲートとCodeGraphを実行する。
- **Final review** — Reviewer: Main agent. Inputs: 全差分、AC表、task表、CodeGraph caller、format、Release build、External除外全テスト、古いRaceResult実サイトE2E。Decision: T1–T3とAC1–AC4はすべてVerified。承認範囲外の変更、未完了状態、受け入れを阻害する外部blockerはない。Workerは初回受入、再試行・エスカレーション・Main修正0件。利用量・費用は取得不能だが、独立したテスト作成を並行化し、scope/quality gateを満たしたため採用。Focused self-auditでは、キャンセル、上限時間、例外分類、既存経路、実サイト接続、文書同期を確認し、再利用可能なスキル不足は認めなかった。

## Verification record

- 2026-09-15: 管理画面で例外、発生日時、処理時間、実行バッチを確認。
- 2026-09-15: JRA公式 `https://www.jra.go.jp/JRADB/accessS.html` で対象リンクの文言、要素種別、`onclick` を確認。
- 2026-09-15: ユーザーがAC1–AC4と記載範囲を承認。StatusをApprovedへ更新し、Pre-implementation reviewを完了。
- 2026-09-15: `dotnet build src/HorseRacingPrediction.Scraping/HorseRacingPrediction.Scraping.csproj --no-restore` — 成功、警告0、エラー0。
- 2026-09-15: `dotnet test ...Scraping.Tests.csproj --no-restore --filter FullyQualifiedName~...JraNavigatorTests` — 44/44成功。
- 2026-09-15: `dotnet test ...Scraping.Tests.csproj --no-restore` — 244成功、既存の明示スキップ1、失敗0。
- 2026-09-15: `dotnet test ...Scraping.Tests.csproj --no-restore --filter FullyQualifiedName~JraSiteE2ETests.古いRaceResult取得` — JRA実サイトで1/1成功。
- 2026-09-15: `codegraph sync .` — 成功。再探索で`ClickHistoricalRaceSearchAsync`のproduction callerが`ToHistoricalRaceSearchAsync`の1件、関連テストありと確認。
- 2026-09-15: `dotnet format HorseRacingPrediction.sln --no-restore --verify-no-changes` — 成功。
- 2026-09-15: `dotnet build HorseRacingPrediction.sln --no-restore --configuration Release` — 成功、警告0、エラー0。
- 2026-09-15: `dotnet test HorseRacingPrediction.sln --no-build --configuration Release --filter "TestCategory!=External"` — 964成功、既存の明示スキップ1、失敗0。
- 2026-09-15: `python .codex/skills/document-driven-development/scripts/validate_change_records.py docs/changes/20260915_historical-race-search-navigation` — issues=0。
- 2026-09-15: Worker routing — テスト作成は初回受入。再試行0、エスカレーション0、利用量・費用は取得不能。

## Deviations and follow-up

- 設計との差分なし。デプロイと失敗対象の再実行は承認済みNon-goalsとして未実施。

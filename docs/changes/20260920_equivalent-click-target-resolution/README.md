# 騎手・調教師ページの重複リンクを個別に解決する

- Status: Approved
- Change record schema: 2
- Owner: Main
- Created: 2026-09-20
- Updated: 2026-09-20
- JRA site contract impact: None

## Context

production の読み取り専用監視で high severity の `ActionableFailureGroup`（fingerprint `65ee2aecb8544f5c`）が継続した。2026-09-20 11:48 JST の診断では `trainer-profile` 1件、resource `trainer-ae6f6e74-ec5a-5954-8d2b-4e324b6cb0d1`、task `f0bca3fd-41d6-4c61-ba24-9acdfa66aa63` が対象だった。

attempt 1-4 は `TimeoutException`、attempt 5 は `InvalidOperationException`（`テキスト '騎手・調教師' に一致するクリック可能要素が一意ではありません。`）で失敗した。最終 attempt の execution batch では先行3 trainer taskも timeoutし、対象だけの保存データ不整合ではない。`JraNavigator.ToSubjectProfileAsync` はJRAトップでラベルだけを指定し、`PlaywrightWebBrowser.FindClickableLocatorAsync` は同じscore、region、text lengthの候補が2件以上あると、遷移先が同一でも例外にする。

## Goal

騎手・調教師プロフィールへの入口だけで、期待するJRA公式リンクをリンク先まで検証して選ぶ。共通クリック処理と他画面の選択規則は変更しない。

## Non-goals

- production deploy、pipeline resume、失敗履歴削除、強制再実行。
- 異なる遷移先を持つ同名候補の推測選択。
- `PlaywrightWebBrowser`の共通クリック解決規則の変更。
- 特定trainer IDだけの例外処理。
- timeout全般やJRAの処理能力問題の同時修正。

## Decision

- `JraNavigator.ToSubjectProfileAsync`はJRAトップの文字列クリックを使わず、既知のJRA公式騎手・調教師一覧URLへ直接移動する。トップメニューは通常のanchorではなくJavaScript要素であり、リンク一覧からは安全に選べないためである。
- 引退者一覧は取得済みリンクをHTTP(S)の絶対URLへ正規化し、全候補が同じJRA公式遷移先を表す場合だけDOM順の先頭をクリックする。
- 引退者一覧の期待リンクがない、JRA外・非HTTP(S)、または異なる遷移先が混在する場合は安全に停止する。
- 共通クリック解決器、馬検索、レース画面、特定trainer IDは変更しない。

## Material concerns

| ID | Concern | Evidence and impact | Disposition | State |
| --- | --- | --- | --- | --- |
| C1 | 同名だが異なる操作を誤選択し得る | 現行例外は誤操作を防ぐ安全境界 | 騎手・調教師入口に限定し、JRA公式URLの完全一致だけを畳む。異なるhrefは停止する | Resolved in design |
| C2 | production HTMLそのものは診断APIに保存されない | 重複候補のhrefを過去attemptから再構成できない | production-shaped fixtureで修正を検証し、deploy後の同resource成功とfingerprint消失を最終証拠にする | Resolved in design |
| C3 | deployなしでは監視解消を確認できない | 利用者は明示許可なしのdeployを禁止している | local実装と検証を今回の承認範囲とし、deployと再収集は別の明示許可を得る | Excluded follow-up |

## Acceptance criteria

| ID | Observable criterion | Verification | State |
| --- | --- | --- | --- |
| AC1 | trainer/jockey profileはJRAトップの同名メニューをクリックせず、対応するJRA公式一覧URLへ直接移動する。引退者一覧の同一URL重複は1回だけクリックできる。 | navigator route/duplicate-link test | Verified |
| AC2 | 引退者一覧リンクが欠落、JRA外・非HTTP(S)、または異なる遷移先を持つ場合は停止する。 | navigator negative tests | Verified |
| AC3 | 共通クリック処理、馬検索、レース画面、特定trainer IDを変更しない。 | diff review、repository search | Verified |
| AC4 | trainer/jockey profile navigationと外部JRAサイトに依存しないScraping回帰テストが成功する。 | focused test、deterministic Scraping regression、format gate | Verified |
| AC5 | deploy後、対象resourceのreplacement taskが成功し、fingerprint `65ee2aecb8544f5c` が監視から消える。失敗履歴は削除しない。 | production read-only group/resource/batch/monitor evidence | Externally blocked |

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | 騎手・調教師入口だけのリンク選択と反例テストを実装する。AC1-AC4 | Main | High capability | User approval | `JraNavigator.Subjects.cs`、navigation tests | focused tests、Scraping regression、format gate | 63 focused、277 deterministic regression、format成功 | Verified |
| T2 | 明示許可されたdeploy/recovery後に対象を再観測する。AC5 | Main + operator | High capability | T1、deploy/recovery authorization | recordのみ（production mutationは別許可） | read-only monitoring diagnostics | replacement成功、fingerprint消失 | Externally blocked |

## Review gates

- **Design and task-split review — 2026-09-20, reviewer: Main.** 個別navigator変更とproduction closureは直列依存する。T1は単一の短い変更で実装と反例テストが密接なため主担当が保持する。AC1-AC5はT1-T2へ双方向に対応する。
- **Concern and agreement review — 2026-09-20, reviewer: Main.** 誤クリック、保存されないHTML、deploy権限を確認した。C1-C2は設計で解決し、C3は今回のAC1-AC4を妨げない別許可follow-upとした。
- **Approval — 2026-09-20.** 利用者は問題箇所単位の個別対応、すなわち共通クリック処理を変更せず騎手・調教師入口だけを修正する設計を確認し、実装を明示的に依頼した。
- **Pre-implementation review — 2026-09-20, reviewer: Main.** T1をRunnable、T2をDependentとした。T1のwrite scopeは`JraNavigator.Subjects.cs`とnavigation testsだけで、期待する反例は公式一覧への直接route、引退一覧の同一リンク重複、異なるリンク先、欠落・unsafe URL。コード確認でトップメニューがJavaScript要素であることを確認したため、承認済みの個別対応内で一覧リンク探索から公式URL直接遷移へ実装方法を具体化した。共通browser変更、production操作が必要なら停止して再設計する。
- **Checkpoint review — 2026-09-20, reviewer: Main.** AC1-AC4を統合diffと実行証拠で確認した。変更は`JraNavigator.Subjects.cs`とnavigation testsに限定され、共通browserは未変更。JRA公式trainer/jockey一覧URLはいずれもHTTP 200で最終URL不変だった。
- **Final review — 2026-09-20, reviewer: Main.** T1とAC1-AC4はVerified。T2/AC5は明示的に禁止されたdeploy/recoveryを必要とするためExternally blockedで、recordはApprovedを維持する。全Scraping suiteでは外部JRAのrace-result画面に依存する既存SiteE2E 4件が`RaceListPage`を返して失敗したが、変更箇所と無関係なためproduction verificationと分離し、外部サイト非依存277件を回帰gateとした。

## Production evidence

- Observed at: 2026-09-20 11:48:43 JST
- Failure group: `617A82F777D7E281`, `trainer-profile`, `InvalidOperationException`, count 1
- Target: `Trainer/JRA/trainer-ae6f6e74-ec5a-5954-8d2b-4e324b6cb0d1`, task `f0bca3fd-41d6-4c61-ba24-9acdfa66aa63`
- Attempts: 5 total; attempts 1-4 timeout、attempt 5 ambiguous `騎手・調教師` click。最終 Lambda request `05252022-7116-552e-8587-430b745eb089`。
- Batch: `f40e4181-9cfe-47bf-b1db-f486fde2f169`; 先行3 taskはretryable timeout、対象はterminal failure。
- Flow at same cutoff: trainer-profile Realtime arrived 149 / dispatched 210 / completed 205 / active 6 / oldest 40.1 minutes。definition全体の停止ではない。
- Evidence gap: clickable候補のhref/DOM snapshotはattemptへ保存されず、過去HTMLから同一遷移先を直接証明できない。
- Mutation performed: none。

## Documentation updates

- 新規 change record のみ。共通browser契約は変更せず、navigator固有の安全な入口選択として完結する。

## Verification record

- `dotnet test tests/HorseRacingPrediction.Scraping.Tests/HorseRacingPrediction.Scraping.Tests.csproj --no-restore --filter "FullyQualifiedName~JraNavigatorTests"`: 63/63成功。
- `dotnet test tests/HorseRacingPrediction.Scraping.Tests/HorseRacingPrediction.Scraping.Tests.csproj --no-restore --filter "FullyQualifiedName!~SiteE2E"`: 277/277成功。
- `dotnet format HorseRacingPrediction.sln --no-restore --verify-no-changes`: 成功、warningなし。
- JRA公式一覧URL確認: trainer/jockeyともHTTP 200、redirectなし、一覧ラベルを含む。
- 全Scraping suite: 287件中282成功、1 skip、4失敗。4件はいずれも外部JRA race-result SiteE2Eで期待した`RaceResultPage`ではなく`RaceListPage`が返る既存外部状態であり、本変更のsubject navigation経路を通らない。

# 同一遷移先の重複クリック候補を安全に解決する

- Status: Proposed
- Change record schema: 2
- Owner: Main
- Created: 2026-09-20
- Updated: 2026-09-20
- JRA site contract impact: None

## Context

production の読み取り専用監視で high severity の `ActionableFailureGroup`（fingerprint `65ee2aecb8544f5c`）が継続した。2026-09-20 11:48 JST の診断では `trainer-profile` 1件、resource `trainer-ae6f6e74-ec5a-5954-8d2b-4e324b6cb0d1`、task `f0bca3fd-41d6-4c61-ba24-9acdfa66aa63` が対象だった。

attempt 1-4 は `TimeoutException`、attempt 5 は `InvalidOperationException`（`テキスト '騎手・調教師' に一致するクリック可能要素が一意ではありません。`）で失敗した。最終 attempt の execution batch では先行3 trainer taskも timeoutし、対象だけの保存データ不整合ではない。`JraNavigator.ToSubjectProfileAsync` はJRAトップでラベルだけを指定し、`PlaywrightWebBrowser.FindClickableLocatorAsync` は同じscore、region、text lengthの候補が2件以上あると、遷移先が同一でも例外にする。

## Goal

同じラベルを持つレスポンシブナビゲーション等の重複要素について、同一の安全な遷移先を表す場合だけ決定論的に1件を選び、異なる操作を表す曖昧な候補は従来どおり停止する。

## Non-goals

- production deploy、pipeline resume、失敗履歴削除、強制再実行。
- 異なる遷移先を持つ同名候補の推測選択。
- timeout全般やJRAの処理能力問題の同時修正。

## Decision

- clickable descriptorへaction identityを追加する。anchor/linkは現在のページを基準に解決したHTTP(S) URL、button等は安全に比較できる明示的action属性を使用し、識別不能な候補は同一扱いしない。
- 最上位候補が複数でも、全候補のaction identityが同一である場合だけDOM順の先頭を選ぶ。
- action identityが異なる、欠落する、またはHTTP(S)以外の場合は現在の曖昧性例外を維持する。
- navigator固有のラベルや対象trainer IDを特例化しない。

## Material concerns

| ID | Concern | Evidence and impact | Disposition | State |
| --- | --- | --- | --- | --- |
| C1 | 同名だが異なる操作を誤選択し得る | 現行例外は誤操作を防ぐ安全境界 | identity完全一致の場合だけ重複を畳み、反例テストで異なるhrefは例外を維持する | Resolved in design |
| C2 | production HTMLそのものは診断APIに保存されない | 重複候補のhrefを過去attemptから再構成できない | production-shaped fixtureで修正を検証し、deploy後の同resource成功とfingerprint消失を最終証拠にする | Resolved in design |
| C3 | deployなしでは監視解消を確認できない | 利用者は明示許可なしのdeployを禁止している | 実装承認後もlocal verificationまでとし、deployと再収集は別の明示許可を得る | Open decision |

## Acceptance criteria

| ID | Observable criterion | Verification | State |
| --- | --- | --- | --- |
| AC1 | 同じ表示文字・同じ領域・同じHTTP(S)遷移先を持つ可視anchorが複数あっても、1回だけ安全にクリックできる。 | Playwright browser counterexample test | Proposed |
| AC2 | 同順位の候補が異なるhref、識別不能action、または非HTTP(S)遷移を持つ場合は曖昧性例外を維持する。 | negative browser tests | Proposed |
| AC3 | trainer/jockey profile navigationの既存回帰テストが成功し、ラベルやtrainer IDの特例がない。 | scraping focused regression | Proposed |
| AC4 | deploy後、対象resourceのreplacement taskが成功し、fingerprint `65ee2aecb8544f5c` が監視から消える。失敗履歴は削除しない。 | production read-only group/resource/batch/monitor evidence | Proposed |

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | action identityを使う同一候補collapseと反例テストを実装する。AC1-AC3 | Main | High capability | User approval | `PlaywrightWebBrowser.cs`、browser/navigation tests | focused tests、Scraping regression、format gate | passing positive/negative tests | Proposed |
| T2 | 明示許可されたdeploy/recovery後に対象を再観測する。AC4 | Main + operator | High capability | T1、deploy/recovery authorization | production mutationは別許可、recordのみ | read-only monitoring diagnostics | replacement成功、fingerprint消失 | Dependent |

## Review gates

- **Design and task-split review — 2026-09-20, reviewer: Main.** selection safetyとproduction closureは直列依存する。ブラウザー選択規則は横断的な安全境界で、単一ファイルと密接な反例設計を主担当が保持する。AC1-AC4はT1-T2へ双方向に対応する。
- **Concern and agreement review — 2026-09-20, reviewer: Main.** 誤クリック、保存されないHTML、deploy権限を確認した。C1-C2は設計で解決し、C3は利用者判断待ちのため本recordはProposedを維持する。

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

- 新規 change record のみ。既存の正本設計を変更する承認済み実装はまだない。

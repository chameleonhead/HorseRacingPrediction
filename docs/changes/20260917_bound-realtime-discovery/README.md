# 競走馬の過去レースを段階的に低優先化する

- Status: Implemented
- Owner: Main
- Created: 2026-09-17
- Updated: 2026-09-17

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Complete | 起点laneの一段降格と、同一active taskのlane/priority高優先マージを実装した。 |
| Verification | Complete | focused、API、Collector、CI相当テストと時刻固定回帰を通過した。 |
| Deployment/operation | Complete | commit `68b039b` をAPI/Lambdaへ配備し、Realtime race-detail 72件が9/19-21の週末レースだけであることを確認した。 |

## Context

週末レースから作られた Realtime の競走馬プロフィールは、その馬の全過去レースも Realtime/High で登録する。
競走馬の頭数と掲載レース数の積で Realtime task が増えるため、Realtime の流入が処理量を上回り、キューが
収束しない。過去レースは予測準備に必要だが、週末レースと出走馬プロフィールそのものと同じ即時性は不要である。

過去レースの lane は起点となる競走馬 task の lane から一段下げる。別経路ですでに同じジョブが存在する場合は、
後から低い依頼が来ても降格せず、後から高い依頼が来た場合は高い lane/priority へ昇格する。これにより重複を増やさず、
最も強い既存の収集意図を保持する。

## Goals

- Realtime の競走馬が作る過去レースを Normal にする。
- Normal の競走馬が作る過去レースを Background にする。
- Background の競走馬が作る過去レースは Background のままにする。
- 同一 Resource/Definition の既存ジョブと新規依頼を、高い lane と高い数値 priority を失わないよう統合する。
- 新規 Realtime task の派生増加を抑え、Realtime 待機列を収束可能にする。

## Non-goals

- 週末レース、オッズ、週末レースから直接作られる主体プロフィールの現在の lane は変更しない。
- Realtime/Normal/Background の 80/10/10 配分は変更しない。
- レース、結果、プロフィールの取得内容やブラウザー待機方式は変更しない。
- 既存ジョブを一括更新する migration は行わない。既存ジョブは同じ Resource/Definition への新規依頼が来た時だけマージする。
- 新しいエラー群の自動復旧は別 change record とする。

## Documentation updates

- `docs/26-collection-platform-design.md`: 競走馬 lane から過去レース lane を一段下げる規則と、重複依頼の高優先マージを正本へ追加する。
- 本 change record: 要件、境界、受け入れ条件、実装・検証結果を保持する。

## Technical impact

- `JraSubjectProfileCollectionHandler.DiscoverHorseRaceHistoryAsync` は `weekendPriorityUntil` の有無ではなく、
  起点 task の lane から履歴 race-detail の lane/priority を決める。
- 対応は `Realtime → Normal/Low`、`Normal → Background/Background`、`Background → Background/Background` とする。
- `CollectionPlatformStore.RequestCoreAsync` の普通依頼重複排除は、既存の非終端 task を返す前に、既存値と新規値の
  高い方へ lane/priority をマージする。lane の強さは `Realtime > Normal > Background`、数値 priority は `Max` とする。
- lane/priority が昇格した task は、同じ Task ID と dispatch generation を保つ。未配送 task は outbox 候補の並びへ
  直ちに反映し、Running task は現在の attempt を中断せず、保存した昇格値を次回 retry/dispatch から使用する。
- 低い新規依頼による降格は行わず、既存の高優先意図を保持する。

## Resource propagation inventory

| Producer | Child resource | Current lane behavior | Can recursively expand? | Decision |
| --- | --- | --- | --- | --- |
| 3-hour planning bucket | Race discovery | Realtime/High | No. Each bucket is a durable logical resource and ordinary registration deduplicates it. | No change. |
| Race discovery | Race detail and odds | Current/future race is Realtime; result-route/backfill is Background. | No unbounded recursion. The same canonical Race ID is deduplicated. | No change. |
| Weekend race detail | Horse/Jockey/Trainer/Owner profiles | Realtime/High for the finite entries on that race card. | Only Horse expands further; the other three handlers do not create child collection tasks. | Keep direct-subject behavior in this scope. |
| Historical race detail | Horse/Jockey/Trainer/Owner profiles | Normal/Low because the race date is outside the weekend window. | Only Horse expands further. With this proposal its history becomes Background. | Covered by AC2 and AC5-AC7. |
| Horse profile | Historical races | Realtime/High while `weekendPriorityUntil` is active; otherwise Background. | Yes. One horse can create many Race tasks, and recent Race tasks can discover more Horse tasks. | Change to one-step lane demotion. |
| Horse profile | Sire, dam, trainer profiles | Background with reduced numeric priority, depth limit 3, and ancestor-cycle prevention. | Horse ancestors can expand, but every generation remains Background and is depth-bounded. | No change; preserve with AC3/AC8. |
| Jockey/Trainer profile | None | No child collection requests. | No. | No change. |
| Owner identity | None | No child collection requests. | No. | No change. |
| Odds/result availability retry | Same Resource/Definition | Existing task receives a bounded next time. | No new child task graph. | No change. |
| Scheduled refresh | Same Resource/Definition | Policy-selected lane; only when no active task exists. | No new resource. | No change. |
| Manual/recovery/backfill | Explicit target or finite date range | Operator-selected or Background. | Finite and explicitly initiated. | No change; high-priority merge applies consistently. |

The inventory therefore identifies no second recursive Realtime producer requiring the same demotion rule. The direct weekend
Jockey/Trainer/Owner volume is bounded by race-card entries and should be evaluated separately only if production evidence shows
that even this finite set is too large; changing it is not required to stop the current horse-history expansion.

## Decisions

1. 履歴 lane は一段だけ下げ、Realtime を推移的に増殖させない。
2. Realtime 起点の履歴は Normal とし、予測準備の進行と Realtime 収束を両立する。
3. Normal/Background 起点の履歴は Background とする。
4. 重複 task は作らず、非終端の既存 task と新規依頼の高い lane/priority を採用する。
5. Running task の現在の attempt は中断・重複実行せず、保存した高優先値を次回 retry/dispatch から反映する。

### Rejected alternatives

- **Realtime 馬の履歴も Realtime:** 現在の非収束要因を残すため不採用。
- **すべての履歴を Background:** 週末予測の準備が遅れすぎるため不採用。
- **既存 task を常に新規値で上書き:** 低い依頼で高優先 task が降格するため不採用。
- **既存 task と別に高優先 task を追加:** 同一対象の重複実行を増やすため不採用。

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | Realtime の競走馬プロフィールが作る過去レースは Normal/Low になる。 | T1, T2 | Horse history handler test | Verified |
| AC2 | Normal の競走馬プロフィールが作る過去レースは Background/Background になる。 | T1, T2 | Horse history handler test | Verified |
| AC3 | Background の競走馬プロフィールが作る過去レースは Background/Background のままになる。 | T1, T2 | Horse history handler test | Verified |
| AC4 | 週末レース・オッズ・直接作られる主体プロフィールの現行 lane/priority は変わらない。 | T2 | race discovery/detail 回帰テスト | Verified |
| AC5 | 同一 Resource/Definition の待機中既存 task に低い依頼が来ても lane/priority は降格しない。 | T1, T2 | store integration test | Verified |
| AC6 | 同一 Resource/Definition の待機中既存 task に高い依頼が来ると、同じ Task ID のまま lane と数値 priority がそれぞれ高い値へ昇格する。 | T1, T2 | store/outbox integration test | Verified |
| AC7 | Running task に高い依頼が来ても現在の attempt は中断・重複実行されず、昇格値が次回 retry/dispatch に使われる。 | T1, T2 | running/retry/dedup integration test | Verified |
| AC8 | 配分ロジック、失敗時の安全停止、ブラウザー取得、手動再取得は変更されない。 | T2, T3 | regression と diff review | Verified |
| AC9 | デプロイ後、新しく作られる horse-history task に Realtime がなく、Normal/Background へ振り分けられる。 | T4 | 本番ジョブ画面と時系列件数 | Verified |
| AC10 | デプロイ後、Realtime 待機数が horse-history の派生だけで増加し続けない。 | T4 | 本番の lane/definition 推移 | Verified |
| AC11 | 全 collection task producer の棚卸しで、競走馬履歴以外に再帰的な Realtime 生成経路がなく、有限・同一対象再実行・Background 深度制限のいずれかであることを確認できる。 | T2, T3 | producer inventory、CodeGraph、repository-wide search | Verified |

## Delivery plan

1. Horse history の lane/priority を起点 lane から決める。
2. store の普通依頼重複排除へ非終端 task の高優先マージを追加する。
3. 三つの起点 lane、降格防止、昇格、Running 不変、重複排除のテストを追加する。
4. focused/full gates、CodeGraph 同期、設計適合レビューを行う。
5. デプロイ後に horse-history の lane と Realtime 待機数の傾向を確認する。

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | 履歴 lane と既存 task の高優先マージを実装する。 | Main | Lead tier | Approval | Collector handler、CollectionPlatformStore | focused tests | diff とテスト結果 | Verified |
| T2 | 三段階 lane、昇降格、Running、重複排除のテストを追加する。 | Main | Lead tier | T1 | Collector/operations tests | focused/integration tests | AC1-AC8 の証拠 | Verified |
| T3 | 正本更新、全回帰、CodeGraph、セルフレビュー、commit/push を行う。 | Main | Lead/review tier | T1-T2 | docs/derived graph | repository gates | CI 成功 | Verified |
| T4 | デプロイして horse-history の振り分けと Realtime 流入の収束を確認する。 | Main | Lead tier | T3 | approved deployment/observation | production UI | AC9-AC10 の時系列証拠 | Verified |

## Review gates

- **Design and task-split review — 2026-09-17, reviewer: Main.** ユーザーの修正指示に基づき、主体種別ごとの複雑な
  伝播グラフ案を取り下げた。起点 horse task の lane を一段下げる単純規則と、同一 task では高い既存・新規値を採用する
  規則へ限定した。AC1-AC11 は生成、永続化、配送前マージ、Running 境界、全 producer の境界、本番収束を網羅する。共有 store 契約を含むため
  Main が直列で担当する。
- **Pre-implementation review — 2026-09-18, reviewer: Main.** ユーザー承認を確認。T1をRunnable、T2-T4を依存順にDependentとした。履歴lane、store重複統合、テスト、デプロイを直列実行し、主体識別復旧変更と同一ファイルを触るCollector handlerはMainが統合する。
- **Checkpoint review — 2026-09-18, reviewer: Main.** 三段階laneとactive taskの高優先マージを実装し、同じTask ID、Running attempt非中断、低優先依頼での非降格をテストで確認した。全producer再検索でも追加の再帰Realtime経路はなかった。
- **Final review — 2026-09-18, reviewer: Main.** CI/deploy成功後、本番待機列をlane/definitionで確認した。Realtimeのrace-detail 72件はすべて9/19-21の週末開催で、過去レースのRealtime混入はなかった。承認済みACに未完了状態はない。

## Verification record

- 2026-09-17: CodeGraph とソース確認で、現行 `DiscoverHorseRaceHistoryAsync` は `weekendPriorityUntil` が有効なら
  起点 lane に関係なく全履歴を Realtime/High にすることを確認した。
- 2026-09-17: 現行の普通依頼重複排除は既存 task をそのまま返し、新規依頼が高くても lane/priority を昇格しないことを確認した。
- 2026-09-17: `RequestAsync`、`RequestManyAsync`、`CollectionRequestBulkItem` の全 production caller と scheduler、
  manual/recovery/backfill entry point を棚卸しした。Horse profile 以外に子 collection task を再帰生成する subject handler はない。
  Race discovery は canonical Race ID、planning は3時間 bucket、schedule/retry は同一 Resource/Definition、backfill は有限期間であり、
  同じ Realtime 連鎖対策を追加する必要はない。
- 2026-09-18: `dotnet build` は警告0・エラー0、CI相当のReleaseテスト（`TestCategory!=External`）は全プロジェクト成功。API 228件、Collector 247件を個別にも確認した。
- 2026-09-18: 実行日に依存して過去導線へ変わるrace discovery/cardテストへ固定時刻を注入し、対象31件を安定化した。
- 2026-09-18: GitHub Actions `app-ci` と `app-deploy` が成功し、APIとCollector Lambdaを配備した。本番 `/jobs?view=waiting&type=Race&lane=Realtime&q=race-detail` は72件すべてが2026-09-19〜21の週末レースだった。

## Deviations and follow-up

- 当初案の Jockey/Trainer/Owner 個別降格は撤回し、本変更の範囲外とした。
- 新しい障害群の自動復旧設計は別 change record とし、本変更へ混在させない。

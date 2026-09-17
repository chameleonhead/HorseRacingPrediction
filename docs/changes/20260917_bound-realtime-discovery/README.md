# 競走馬の過去レースを段階的に低優先化する

- Status: Proposed
- Owner: Main
- Created: 2026-09-17
- Updated: 2026-09-17

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Not started | 承認後に過去レースの lane 決定と既存ジョブの高優先マージを実装する。 |
| Verification | Not started | ハンドラ、store、API/outbox、回帰テストを実行する。 |
| Deployment/operation | Not started | デプロイ後に Realtime 流入と既存ジョブの優先度を確認する。 |

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
| AC1 | Realtime の競走馬プロフィールが作る過去レースは Normal/Low になる。 | T1, T2 | Horse history handler test | Not started |
| AC2 | Normal の競走馬プロフィールが作る過去レースは Background/Background になる。 | T1, T2 | Horse history handler test | Not started |
| AC3 | Background の競走馬プロフィールが作る過去レースは Background/Background のままになる。 | T1, T2 | Horse history handler test | Not started |
| AC4 | 週末レース・オッズ・直接作られる主体プロフィールの現行 lane/priority は変わらない。 | T2 | race discovery/detail 回帰テスト | Not started |
| AC5 | 同一 Resource/Definition の待機中既存 task に低い依頼が来ても lane/priority は降格しない。 | T1, T2 | store integration test | Not started |
| AC6 | 同一 Resource/Definition の待機中既存 task に高い依頼が来ると、同じ Task ID のまま lane と数値 priority がそれぞれ高い値へ昇格する。 | T1, T2 | store/outbox integration test | Not started |
| AC7 | Running task に高い依頼が来ても現在の attempt は中断・重複実行されず、昇格値が次回 retry/dispatch に使われる。 | T1, T2 | running/retry/dedup integration test | Not started |
| AC8 | 配分ロジック、失敗時の安全停止、ブラウザー取得、手動再取得は変更されない。 | T2, T3 | regression と diff review | Not started |
| AC9 | デプロイ後、新しく作られる horse-history task に Realtime がなく、Normal/Background へ振り分けられる。 | T4 | 本番ジョブ画面と時系列件数 | Not started |
| AC10 | デプロイ後、Realtime 待機数が horse-history の派生だけで増加し続けない。 | T4 | 本番の lane/definition 推移 | Not started |

## Delivery plan

1. Horse history の lane/priority を起点 lane から決める。
2. store の普通依頼重複排除へ非終端 task の高優先マージを追加する。
3. 三つの起点 lane、降格防止、昇格、Running 不変、重複排除のテストを追加する。
4. focused/full gates、CodeGraph 同期、設計適合レビューを行う。
5. デプロイ後に horse-history の lane と Realtime 待機数の傾向を確認する。

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | 履歴 lane と既存 task の高優先マージを実装する。 | Main | Lead tier | Approval | Collector handler、CollectionPlatformStore | focused tests | diff とテスト結果 | Proposed |
| T2 | 三段階 lane、昇降格、Running、重複排除のテストを追加する。 | Main | Lead tier | T1 | Collector/operations tests | focused/integration tests | AC1-AC8 の証拠 | Proposed |
| T3 | 正本更新、全回帰、CodeGraph、セルフレビュー、commit/push を行う。 | Main | Lead/review tier | T1-T2 | docs/derived graph | repository gates | CI 成功 | Proposed |
| T4 | デプロイして horse-history の振り分けと Realtime 流入の収束を確認する。 | Main | Lead tier | T3 | approved deployment/observation | production UI | AC9-AC10 の時系列証拠 | Proposed |

## Review gates

- **Design and task-split review — 2026-09-17, reviewer: Main.** ユーザーの修正指示に基づき、主体種別ごとの複雑な
  伝播グラフ案を取り下げた。起点 horse task の lane を一段下げる単純規則と、同一 task では高い既存・新規値を採用する
  規則へ限定した。AC1-AC10 は生成、永続化、配送前マージ、Running 境界、本番収束を網羅する。共有 store 契約を含むため
  Main が直列で担当する。
- **Pre-implementation review:** approval pending.
- **Checkpoint review:** pending.
- **Final review:** pending.

## Verification record

- 2026-09-17: CodeGraph とソース確認で、現行 `DiscoverHorseRaceHistoryAsync` は `weekendPriorityUntil` が有効なら
  起点 lane に関係なく全履歴を Realtime/High にすることを確認した。
- 2026-09-17: 現行の普通依頼重複排除は既存 task をそのまま返し、新規依頼が高くても lane/priority を昇格しないことを確認した。

## Deviations and follow-up

- 当初案の Jockey/Trainer/Owner 個別降格は撤回し、本変更の範囲外とした。
- 新しい障害群の自動復旧設計は別 change record とし、本変更へ混在させない。

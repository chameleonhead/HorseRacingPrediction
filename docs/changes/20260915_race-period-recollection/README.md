# 期間を指定してレース情報を再取得する

- Status: Proposed
- Owner: HorseRacingPrediction maintainers
- Created: 2026-09-15
- Updated: 2026-09-15

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Not started | 承認後に期間再取得 API、永続化、管理 UI、Collector 経路を実装する |
| Verification | Not started | 関連 component/API/Store/Collector テスト、ブラウザー確認、solution gate を実行する |
| Deployment/operation | Not started | 実装検証後、ローカル開発環境から 2026-09-12〜2026-09-13 (JST) の依頼を登録する |

## Context

管理画面の現行「過去データ収集」は年月単位の Backfill であり、未取得日の補完を目的とする。Store は任意の包括日付範囲を保持できるが、同じ日付の `race-discovery` 状態が既にあれば登録を省略するため、収集済みレースの明示的な再取得には使えない。

利用者は任意期間のレース情報を再取得したい。最初の実運用対象は、2026-09-15 JST から見た直前の暦上の週末である 2026-09-12（土）から 2026-09-13（日）までとする。

旧 `RaceDayReacquisition` は統合収集基盤への切替で削除済みであり、旧ジョブを復活させず、現行の Resource / CollectionDefinition / Request / Task / Batch 経路で実現する。

## Goals

- JST の開始日と終了日を指定し、両端を含む各日について JRA レースを再発見・再取得できる。
- DB 未登録レースだけでなく、すでに収集成功した開催日も新しい手動再取得依頼として実行できる。
- 期間全体を追跡でき、失敗した日だけを既存の欠損復旧経路から再依頼できる。
- 同一送信の再試行と実行中タスクの重複を抑止しつつ、完了後の明示的な再取得は許可する。
- 実装検証後、2026-09-12〜2026-09-13 の再取得依頼をローカル開発環境へ登録する。

## Non-goals

- 月単位の未取得補完 Backfill を削除または意味変更すること。
- 未来日の予約収集、31日を超える一括依頼、JRA 以外の provider。
- 過去オッズ時系列の再構築。
- 競走馬・騎手・調教師プロフィールを期間指定で網羅再取得すること。
- 旧 `RaceDayReacquisition` ジョブ/API/UIを復活させること。

## Experience and interaction design

主要利用者は管理画面から収集を運用する利用者であり、開始地点は `/jobs` の `収集を依頼`、完了状態は期間バッチの受付と追跡導線の表示である。

- `収集を依頼` ダイアログに `期間を指定してレースを再取得` を追加する。新しいサイドバーメニューは追加しない。
- フォームは `先週末` preset、`開始日`、`終了日`、固定値 `JRA` を表示する。日付は JST、両端を含む。2026-09-15 に preset を選ぶと 2026-09-12〜2026-09-13 になる。
- 入力は開始日、終了日の順に並べ、狭幅では縦積みにする。各入力にラベルと近接したエラーを表示する。
- `内容を確認` で対象日数、期間、再発見対象、更新対象、除外対象を表示し、その後の `再取得を依頼` だけを primary action とする。
- 受付後は入力を閉じ、期間、受付日数、新規 task 数、実行中 task への集約数と期間詳細への導線を表示する。通信失敗時は入力を保持する。
- Loading、empty、validation error、通信 error、収集停止中、accepted、partial/failed、completed を色だけに依存せず文言で示す。
- 完了済み期間を同じ日付で再度依頼した場合も、新しい batch ID と依頼履歴を作る。二重送信や同じ batch ID の再送は再利用し、同じ Resource + Definition の active task は増やさない。

[テキストワイヤーフレーム](mocks/request-race-period-recollection.md)を画面階層と状態の仕様とする。

## Documentation updates

- `docs/26-collection-platform-design.md`: 現行基盤における期間再取得の Request/Task/Batch、重複抑止、再実行契約を提案として追加する。収集基盤の正本。
- `docs/20-admin-ui-design.md`: 削除済みの単日ジョブを前提とした説明を、`/jobs` の期間再取得 UX 提案へ置き換える。管理 UI の正本。
- `docs/22-collector-design.md`: 旧 `RaceDayReacquisition` 記述を、現行 `race-discovery` → `race-detail` 経路の期間再取得提案へ更新する。Collector 現行動作の正本。
- `docs/21-admin-ui-design-guidelines.md`: Fluent、状態、responsive、accessibility の既存規則で十分なため変更しない。
- 過去の change record は当時の履歴として変更しない。

## Technical impact

- 管理 API に期間再取得の preview/submit endpoint と request/receipt model を追加する。日付は ISO `yyyy-MM-dd`、JST の包括範囲とする。
- 受付範囲は過去または当日、1〜31日とする。開始日後の終了日、未来日、上限超過は field error を返し、task を作成しない。
- 期間ごとに一意な batch ID を発行し、既存 BackfillBatch の範囲・進捗投影を再利用する。ただし理由を通常 Backfill と区別した `PeriodRecollection` とし、既存状態の有無で省略しない。
- 同じ batch ID + 日付の再送は既存 Request を返す。別 batch ID による完了後の再依頼は新しい Request/Task を作る。active task がある日付は Request 履歴を残して既存 task へ集約する。
- 各日を `ResourceType.Race / race-discovery` の独立 task とし、対象日だけを探索する。発見したレースは現行 `race-detail` へ展開し、batch ID と対象日を伝播する。
- 直近5日以内は現行 `race-detail` 契約に従い出馬表と結果を取得し、それ以前は結果、天候、馬場、払戻を取得する。公式にない値で保存済み値を消さない。オッズ時系列は対象外とする。
- 既存 outbox、SQS envelope、lease、retry、pause、failure notification を利用する。新しい長時間単一 task は作らない。
- UI は既存 Fluent dialog/date field pattern と component test 基盤を利用する。

## Decisions

### 採用: 暦上の直前土曜日・日曜日

`先週末` は操作日の直前に完了した土曜日と日曜日とする。祝日月曜や金曜開催を暗黙に含めず、必要なら明示日付で範囲を広げる。表示する解決済み日付により誤解を防ぐ。

### 採用: 1 batch、1日1 discovery task

日ごとの再試行・進捗と Lambda 実行上限を維持し、現行 dispatcher の同日 microbatch と `race-detail` 展開を再利用する。期間全体を1 taskに詰めない。

### 採用: Backfill と明示的再取得を区別

既存 Backfill の「収集済みなら省略」を維持する。期間再取得は terminal task 後も新しい依頼を作れる手動理由として扱い、対象日だけを探索する。

### 採用: 31日上限、未来日は拒否

既存月次 Backfill と同程度の最大展開数に抑え、誤操作時の負荷を限定する。開催のない平日は正常な探索対象であり、JRA開催なしとして完了できる。

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | `/jobs` の収集依頼から、JSTの開始日・終了日と `先週末` preset を使って期間再取得を確認・送信できる | T2, T3 | component test + browser scenario | Not started |
| AC2 | 2026-09-15 JST の `先週末` は 2026-09-12〜2026-09-13 と表示され、包括2日としてpreviewされる | T2, T3 | deterministic date test | Not started |
| AC3 | 未入力、逆転、未来日、31日超過は入力付近に理由を表示し、APIに依頼を作らない | T1, T3 | endpoint/component validation tests | Not started |
| AC4 | 各対象日に1件の `race-discovery` request/task が現在基盤へ永続化・dispatchされ、DB未登録レースを含む当日の公式レースが `race-detail` へ展開される | T1, T2, T4 | Store/API/E2E transport tests | Not started |
| AC5 | 既に成功済みの日も新しい期間依頼で再取得され、同じ送信の再試行とactive taskは重複実行されない | T1, T2, T4 | terminal rerun/idempotency/concurrency tests | Not started |
| AC6 | 期間と日別の待機・成功・失敗・開催なしを追跡でき、失敗箇所を再依頼できる | T2, T3, T4 | API projection/component tests | Not started |
| AC7 | 月次Backfill、通常の単一Resource再取得、pause/retry/lease/outboxに回帰がない | T2, T4 | existing regression suite | Not started |
| AC8 | desktop/狭幅で主要操作と日付・エラーが失われず、ラベル、keyboard focus、busy、status/alertが利用できる | T3, T4 | browser responsive/accessibility check | Not started |
| AC9 | 実装・検証後、ローカル開発環境に 2026-09-12〜2026-09-13 の期間再取得依頼が1 batchとして登録され、受付結果を確認できる | T5 | local API/UI receipt and batch detail | Not started |

## Delivery plan

1. 期間 request/preview/receipt と Store の再取得 batch 展開契約を追加する。
2. `PeriodRecollection` を対象日だけの discovery として dispatcher/collectorへ接続する。
3. `/jobs` の短い確認フロー、preset、validation、receiptを実装する。
4. 実 transport と persistence 境界、失敗・再送・active集約、UI状態を検証する。
5. 正本文書と change record を検証結果へ同期し、ローカルで先週末の依頼を登録する。

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | API契約、範囲validation、Store batch展開と冪等性を実装する (AC3, AC4, AC5) | Main | Lead tier | - | CollectionPlatform API/Store models and focused tests | Store/API tests | request/task/batch assertions | Proposed |
| T2 | reason、dispatcher、collector discovery/detail伝播と回帰を実装する (AC1, AC4, AC5, AC6, AC7) | Worker候補、Main統合 | Worker tier | T1 contract freeze | Collector/dispatcher and focused tests | transport/E2E tests | persisted envelope and downstream requests | Proposed |
| T3 | `/jobs` の期間フォーム、preview、受付表示を実装する (AC1, AC2, AC3, AC6, AC8) | Worker候補、Main統合 | Worker tier | T1 contract freeze | Blazor page/client/component tests | bUnit + browser | observable workflow evidence | Proposed |
| T4 | 全diff、設計適合、回帰、CodeGraph、format/build/testを検証する (AC4-AC8) | Main | Lead tier | T1, T2, T3 | change record/docs; production read-only review | CI-equivalent gates | commands/results and AC matrix | Proposed |
| T5 | ローカル環境へ先週末の依頼を登録し受付を確認する (AC9) | Main | Lead tier | T4 | local application state only | batch receipt/detail | batch ID and accepted dates (credentials not recorded) | Proposed |

T2/T3 は T1 の契約確定後にのみ並列化し、相互の write scope を共有しない。formatter、solution-wide generated output、change record、正本文書は Main が所有する。

## Review gates

- **Design and task-split review** — Reviewer: Main。Inputs: CodeGraph、現行 UI/API/Store/Collector、既存 docs/change records、D1/D2 read-only discovery。Decision: 旧単日ジョブを復活せず、Backfillと期間再取得を分離し、T1契約後のみT2/T3を並列化する。ACは全てtask/verificationへ対応済み。Follow-up: ユーザー承認を得る。
- **Pre-implementation review** — 承認後、全taskのfrontier、worker contract、非重複write scopeを記録する。
- **Checkpoint review** — 各実装sliceでMainがdiff、tests、AC matrix、worker evidenceを確認する。
- **Final review** — 全task/AC、実transport、browser、format/build/test、ローカル受付を照合してから `Implemented` とする。

## Verification record

- 2026-09-15: `codegraph explore` で管理UI、API、Store、dispatcher、collectorの現行呼出経路を確認。
- 2026-09-15: read-only delegated discovery 2件をMainがソースと照合。現行Backfillが既存stateを省略するため再取得要件を満たさないこと、旧単日ジョブがcutover済みであることを確認。
- 実装検証は承認後に記録する。

## Deviations and follow-up

- 承認前のため実装・ローカル再取得依頼は未実施。

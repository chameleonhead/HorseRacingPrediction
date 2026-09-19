# JRAサイト収集契約とCard限定補正

- Status: Proposed
- Owner: Main
- Created: 2026-09-19
- Updated: 2026-09-19

## Goal

JRA公式サイトの画面、取得可能項目、画面遷移、公開期間、代用禁止条件を保守可能な正本へまとめる。
あわせて、Race entryの馬主はRaceCard取得時だけ取得可能という制約に反する全期間owner補正を、
Card再取得可能な対象だけに限定する。

## Context and correction

先行変更は全owner欠損Raceへ`race-detail` revision 2を要求し、公式Cardを再取得できる前提を置いた。
しかし過去RaceはRaceResultからentry情報を復元できても、ownerはRaceResultに存在しない。
Horse profileの現在ownerもRace時点ownerの代用にならない。現行Navigatorも5日より古いRaceCard探索を
意図的にスキップするため、全期間migrationは実行不能な要求を大量に作る。

このため、先行実装はpush/deploy/migration applyせず、本変更の承認後に補正対象分類を修正する。

## Canonical documentation

[JRAサイト収集契約](../../27-jra-site-collection-contract.md)を新設し、以下を正本化した。

- RaceCard、RaceResult、過去検索、Horse/Jockey/Trainer profileの取得元境界
- ownerはRaceCardだけ、過去Resultや現在Horse profileから代用しないという禁止条件
- RaceCardとRaceResultの独立した画面遷移とidentity検証
- 5日のCard探索期間はアプリの事前フィルタでありJRAの公開保証ではないこと
- JRA変更の検知条件、証拠採取、文書・fixture・実装・データ影響評価の更新順序
- JRA関連変更ごとの`JRA site contract impact`レビュー記載と将来のCI gate

## Proposed implementation

1. race-detail migration previewはowner欠損Raceを次に分類する。
   - `Card再取得候補`: RaceCard探索期間内。実在確認前なので補正成功を保証しない。
   - `処理中/既要求`: 同じresource/revisionの要求またはtaskが存在する。
   - `Card取得期間外・補正不能`: 探索期間外。要求を作らない。
2. applyは`Card再取得候補`だけへrevision 2要求を冪等登録する。
3. 実行時にCardが存在しなければ無期限retryせず、取得元制約による`公式Cardなし`として進捗へ残す。
4. Horse profile、RaceResult、別RaceのownerをRace entryへコピーしない。
5. 新規Card収集では、Cardが取得可能な間にparser出力と保存後owner件数の不一致を観測し、後日補正依存を減らす。
6. JRA関連change recordへcontract impact記載を要求するvalidator/CI gateを追加する。

## Non-goals

- PDFや非公開APIを新しいowner取得元にすること。
- 過去Raceのownerを推測すること。
- 既存error jobの一括自動復旧、priority変更、notification解決。
- JRA画面の内部実装や通信完了条件を待機契約にすること。

## Acceptance criteria

| ID | Observable criterion | Verification | State |
| --- | --- | --- | --- |
| AC1 | canonical documentに各画面の入口、取得情報、取得不可情報、遷移、公開上の注意が記載される。 | documentation review | Verified |
| AC2 | ownerはRaceCard取得時だけ取得し、RaceResult/Horse profile/別Raceから代用しない契約が明記される。 | documentation and source matrix review | Verified |
| AC3 | JRA仕様変更を検知する条件と、証拠採取→文書→change record→fixture/test→データ影響評価の更新順序が定義される。 | workflow review | Verified |
| AC4 | migration previewがowner欠損をCard再取得候補、既要求、期間外・補正不能へ分類し、期間外へ要求を作らない。 | API/store integration tests | Not started |
| AC5 | migration applyはCard再取得候補だけに冪等要求を作り、Horse profileやResult値を直接コピーしない。 | DB invariant and migration tests | Not started |
| AC6 | 期間内でもCardが実在しない場合は無期限retryせず`公式Cardなし`として観測できる。 | workflow/progress tests | Not started |
| AC7 | 新規Card収集のowner保存不一致を構造化outcomeで検知できる。 | collector/API integration tests | Not started |
| AC8 | JRA parser/navigation/workflow/migration変更のchange recordがcontract impactを宣言しない場合、validatorが検出する。 | validator tests | Not started |
| AC9 | 既存error jobの自動復旧、priority変更、notification解決を行わない。 | DB invariant tests | Not started |

## Task split

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | canonical JRA site contract | Main | Lead | none | docs/27, docs/24 | doc review | AC1-AC3 | Verified |
| T2 | migration eligibility correction | Main | Lead | Approval | API/store/UI/tests | AC4-AC6, AC9 | integration tests | Proposed |
| T3 | Card completeness guard | Main | Lead | Approval | Collector/API/tests | AC7 | integration tests | Proposed |
| T4 | contract-impact validator | Main | Lead | Approval | DDD validator/CI/tests | AC8 | validator tests | Proposed |
| T5 | final review and deployment gate | Main | Lead | T2-T4 | change records | traceability audit | review evidence | Dependent |

## Review gates

### Design and task-split review

- The current global migration is not deployable because it assumes unavailable historical Cards.
- Owner source, repair eligibility, and error-job recovery are separate concerns.
- Production code remains unchanged until this record is explicitly approved.

### Pre-implementation review

- Pending user approval of AC4-AC9.
- No push/deploy/migration apply is permitted before the correction is implemented and verified.

## Verification record

- Repository CodeGraph and source inspection confirmed separate Card/Result parsers and navigation.
- `JraNavigator.IsWithinRaceCardLookupPeriod` currently uses today minus 5 days as a prefilter.
- `RaceCardPageParser` parses owner; `RaceResultPageParser` has no owner extraction.
- JRA official FAQ was checked on 2026-09-19 for historical result availability and navigation scope.

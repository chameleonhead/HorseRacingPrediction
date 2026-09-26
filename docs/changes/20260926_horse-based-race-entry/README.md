# 馬を基準とする出走識別

- Status: Approved
- Change record schema: 2
- Owner: Main
- Created: 2026-09-26
- Updated: 2026-09-26

## Context / approval

利用者は「キーを馬にし、馬番・枠番をキーにしない」方針を提示。回答でレースID＋馬ID、未確定番号null、参照の安定化、番号入力の照合を説明した。その後「収集済みデータは削除する、移行不要」「プログラムの改修のみ検討し、実装」を明示。これを当該設計の実装承認とする。本番変更・データ削除・移行・配備・再開は対象外。

## Goals and frozen decisions

- 出走の論理キーは RaceId＋HorseId。生成EntryIdもこの組から決定し、番号変更で不変。馬そのものの取り違え・重複馬・確定馬番重複は引き続き拒否。
- HorseNumber/GateNumberはnullable属性。木曜の未確定を0や仮連番に変換しない。null入力で既知の確定番号を消さない。
- 全頭の番号交換を一括検証し更新。途中の一時重複で誤拒否せず、不正envelopeは副作用前に拒否する。
- 結果/予想/履歴は安定EntryIdで参照する。番号で受信する入力は確定した対応で解決し、不明/古い対応は書かない。未知の整合性障害を一律無視する変更はしない。
- 未確定出馬表は保存し、確定後の再取得を待つ。局所的な入力不整合は対象収集だけを失敗/保留とし、全体停止へ拡大しない。投影障害など共通基盤の安全停止は維持する。
- 新規・空データ環境のみを対象とし、旧entry-{number}互換・既存補正・本番削除を実装しない。新旧プログラム/データ混在をサポートしない。

## Hypothesis evidence

旧BuildRaceEntryIdはraceIdと馬番で生成。RaceRefreshは馬番で旧出走を探し、CollectionIdentityは同HorseIdの番号変更を拒否。RegisterEntryイベントと結果参照はEntryIdを使用しており安定化の接続点となる。原障害は木曜保存→土曜番号不一致で再停止（旧調査記録参照）。CodeGraphは新worktreeでindex未生成のため利用不可、通常検索に切替。index作成は行わない。

## Concern and agreement ledger

| ID | Concern and evidence | Impact / disposition | AC/task/test | Agent position | User disposition | State |
| --- | --- | --- | --- | --- | --- | --- |
| C1 | 出走ID以外にも番号を使う照合・オッズがある | 全入口棚卸し、確定対応・古い取得の検査を維持。単なるガード削除は却下 | AC1–4/T1–T4 | Agree | 番号を非キーとし参照も変更する方針で実装依頼 | Resolved in design |
| C2 | 旧データは新IDと不互換 | 空環境のみ、移行/削除/配備はしない | AC5/T4 | Agree | 移行不要・プログラムのみと明示 | Resolved in design |
| C3 | 馬のsource IDなしの名前推測は別馬混同のおそれ | JRA出馬表は公式sourceで識別。曖昧な照合を番号で補わない | AC2–4/T2–T4 | Agree | 馬ID基準の承認済み方針を保持 | Resolved in design |

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | RaceId＋HorseIdに対し一意で不変なEntryId。番号交換後も結果・参照は同馬を指す | T1,T2,T4,T5,T6 | domain/API往復・交換回帰 | Verified |
| AC2 | 未確定番号nullを複数頭保存でき、確定後同じ出走を更新。仮連番/枠番推測なし | T1,T2,T3,T4,T5,T6 | 木曜→確定出馬表→再取得試験 | Verified |
| AC3 | 重複馬/番号や別馬へのID流用、古い番号依存入力を拒否し、不正保存なし | T1,T2,T3,T4,T5,T6 | 反例・API実DB・既存fence回帰 | Verified |
| AC4 | 収集/手動/履歴/予想/オッズの入口が新識別に整合。局所不整合で他収集が停止しない | T2,T3,T4,T6 | 入口棚卸し、対象回帰・全suite | Verified |
| AC5 | ローカル空DBでビルド・test・formatter成功、文書反映済み。本番・既存データ変更なし | T4 | CI相当コマンド、diff/status | Not started |

## Task plan

| ID | Task | Owner / tier | Depends on | Write scope | Verification | Completion evidence | State | Routing | Audit | Result metrics |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | Domain/nullable型/永続モデル | Main/Lead | - | Domain,Application,Contracts,Infrastructure | domain/build/model試験 | Domain118成功、投影58成功、EF差分なし | Verified | 永続化と不変条件はLead保持 | none | unavailable |
| T2 | API/安定ID/参照接続 | Main/Lead | T1契約 | Api,ApiClient,ML/Agents | API・全入口回帰 | API298成功、個別Http5成功 | Verified | 公開契約・統合判断はLead保持 | none | unavailable |
| T3 | 未確定収集経路 | revision_reacquisition/Worker | T1 nullable契約凍結済み | Scraping,Collector（HttpDataCollectionWriteService除外）と関連tests | parser/collector回帰 | Scraping317/Collector353・T3-A1 | Verified | 契約判断はMain固定、独立adapter経路を分離 | [T3-A1](agent-audits/T3-A1.json) | unavailable; retries0; corrections0; reviews0 |
| T4 | 統合・独立review・文書 | Main/Lead | T1–3 | tests,docs | format/build/full tests/空DB | 文書反映、fullsuite反例確認 | In progress | 最終適合・本番境界はLead保持 | none | unavailable |
| T5 | Domain反例test | horse_entry_domain_tests/Worker | T1契約 | HorseBasedEntryIdentityTests.csのみ | Domain118成功（追加反例含む） | T5-A1 | Verified | 凍結された局所testをcost-sensitive workerへ分離 | [T5-A1](agent-audits/T5-A1.json) | unavailable; retries1; corrections0; reviews2 |
| T6 | API/odds反例test・既存fixture | entry_reference_inventory/Worker | T2契約 | CollectedRaceIdentityGuardTests.cs, RaceOddsSnapshotApiTests.cs, RaceEndpointsTests.cs, HorseEndpointsTests.cs, WeekendRacePredictionScenarioTests.cs, RaceAssignmentRepairTests.cs | focused17・closure53・API298成功 | T6-A1 | Verified | 凍結API契約への独立counterexample | [T6-A1](agent-audits/T6-A1.json) | unavailable; retries0; corrections0; reviews1 |

## Review gates

Design/task-split: 主担当が永続化・公開契約を保持。番号依存参照と収集経路の2本をread-only探索へ分離、書込なし。Pre-implementation: 空データ前提とprogram-onlyを固定し、新worktree/branchで旧作業を保全。小さな独立実装候補は契約凍結後に再評価。全体のnullable変更は型が横断するため共有formatter/スキーマはMain専有。

## Documentation updates

`docs/27-jra-site-collection-contract.md` と `docs/26-collection-platform-design.md` に新しい出走識別/未確定収集の正本リンクと適用境界を反映する。既存修正の履歴は書き換えない。

## Verification record / next action

- nullable変更中の初回buildは未適合callerで失敗。Domain/DTO/Main callerとworker fakeを修正。次の並行buildはCS2012共有出力ロックで失敗し、以後build/testの所有を直列化。
- Main API buildとsolution Release buildはその後成功。worker追加test由来warning4件も修正し、最終Release buildはwarning0/error0。
- 未確定Cardは保存stageの後にAwaitingPublication stageを記録して15分再取得を維持。Resultは進めず予想も発火しない。確認済み馬番に対するnullは既知値保持。
- 単独結果登録のtool契約も馬番ではなくHorseIdへ変更。数値オッズは全Raceで取得時fingerprintを照合し、snapshotに観測時の番号/HorseId/EntryId/枠対応を保存する。新旧API互換は対象外。
- Final-review closure: 個別Http登録でrunning styleを保持し、結果登録の409を成功扱いしない。手動APIの範囲検査は関連主体作成より前に実施する。履歴のSourceHorseIdで矛盾する公式Horse identityを上書きしない。既知市場のオッズ選択を確定馬番/枠へ照合する（未知市場の選択原文を推測して解釈しない）。
- `horse-key-final.trx`は1319成功/1skip、追加反例後の`horse-key-verified.trx`は1323成功/1skip。既知市場検査追加後のodds focused11成功。さらに行った`horse-key-complete.trx`では既存Playwright時間条件2件が5秒上限を超過（CalendarWaitsForVisibleRacecourseBeyondThreeSeconds:6.273秒、CalendarTimeoutDoesNotCaptureIncompletePage:6.457秒）。ブラウザー実装/testの時間上限は変更せず、直列回帰で再確認する。負荷競合は推測であり、未確認の原因を断定しない。

Checkpoint: 初回fullsuiteはDomain1/Application1/API12失敗。Domainは例外型期待、Applicationは未登録出走への結果fixture、APIは旧任意/番号EntryId・新gate範囲・旧エラーcode・odds fence未指定という契約追従を修正済み。従来の自動Horse付替え候補生成は新契約では到達不能なので除去し、既存補正APIのテストは保存済み証拠fixtureを明示投入する。補正機能の新規追加や本番実行はしない。

次操作: T6 fixture回帰後、Mainが `dotnet format HorseRacingPrediction.sln --no-restore`、Release build、CI同等非External fullsuite、EF pending-model/空DB migration、isolated localhost smokeを実行。未コミットは本変更のsrc/tests/docs一式を意図的に保持し、検証後に目的単位でcommitする。新branchはcodex/horse-based-race-entry、base8bbee4b0。旧branchのpolicy commitと未コミットskill変更は別worktreeに保全し、本変更に混ぜない。PRマージ後コミット禁止・両branch削除規則は本作業にも適用する。

## Implementation / final review

- EntryIdは `{RaceId}-entry-{HorseId}`。公式sourceによるHorseIdを優先し、手動登録も同じ生成規則とする。API clientの個別結果登録引数はHorseIdへ変更。番号で既存Entryを探す能動経路を除去し、bulkは完成後の全割当を検証して番号交換を許可する。
- HorseNumberはDomain/event/DTO/read modelでnullable。既知番号・枠へのnull再取得は保持する。仮採番・馬番から枠の推定は行わない。予想は番号未確定では実行しない。
- 結果投影の表示番号はEntryRegisteredに追従し、HorseId/EntryId/着順を保持。予想markのEntryId長制限を128へ調整。履歴sourceが公式馬IDと衝突する場合は副作用前に拒否する。
- オッズsnapshotは観測時の全番号/馬/出走/枠を保持。全Raceでfence必須とし、frame変更もfingerprintに含む。既知市場（Win/Place/Quinella/Exacta/Wide/Trio/Trifecta/BracketQuinella）は確定割当へ解決できないselectionを拒否する。未知市場は原文として保存し、番号対応を推測しない。
- 局所エラーは既知のtyped error/statusだけに限定。未確定Cardは保存後も15分後再取得、未確定oddsも局所待機。未知400/409/500や投影障害は成功/無視へ丸めない。
- UI markup変更なし。現在のRace detail APIはoddsをunavailableとして返すため未接続表示へ新しい解釈を追加しない。保存snapshotの観測時対応を将来の表示でも利用する。
- 設計からの外部スコープ変更なし。追加closureは承認済み不変条件の実現に必要なAPI preflight、response CorePersisted、参照投影、既知エラー伝播と反例テストである。モデル/料金telemetryは取得不能と記録し、費用削減を推測しない。単発assertion修正と共有buildロックは本変更内で修正し、永続的なrouting/skill変更の根拠にはしない。
- コードはnullable公開契約が全projectを横断するため、旧callerだけ残る非ビルド可能な途中commitに分割しない。実装+回帰を一つの検証可能なコードcheckpoint、設計/検証文書を別commitにする。

## Verification evidence

| Check | Result |
| --- | --- |
| Release solution build | warning0 / error0 |
| `dotnet format HorseRacingPrediction.sln --no-restore --verify-no-changes` | pass |
| `dotnet ef migrations has-pending-model-changes --project src/HorseRacingPrediction.Infrastructure/HorseRacingPrediction.Infrastructure.csproj --no-build --configuration Release` | no pending model changes |
| Empty temporary SQLite migrations | all12 applied; existing DB untouched |
| `tests/scripts/test-entry-repair-local-host.ps1 -Configuration Release` | isolated2 processes; 14entries/owners, preview read-only, stable repair/retry, cross-process lock, backup, stale odds rejection/current worker write passed |
| vulnerable dependencies scan | no vulnerable packages reported by configured NuGet sources |
| DDD / agent-audit validators | pass |
| Final non-External suite | serial verification in progress; prior1323 pass/1skip and odds closure11 pass |

EF tool8.0.11/runtime10.0.11の既存版差warningはあるが、model検査/空DB適用はexit0。版更新は本変更に混ぜない。スキップは既存の `RaceDiscovery_IsRegisteredByNewScheduler`（RUN_15_MINUTE_CADENCE_TEST=1 の明示実時間検証用）。External/live本番検証・Linux CI・配備は今回の完了条件外。本番データ削除、旧データ移行、停止解除、deploy、PR作成/mergeは行っていない。

# 4主体の要対応データ補正ジョブを管理画面から実行する

- Status: Approved
- Owner: HorseRacingPrediction maintainers
- Created: 2026-09-14
- Updated: 2026-09-14

## Context

`/settings` では、今回のJRA競走馬識別子不具合で記録されたHorse専用repairだけを実行できる。運用者は、競走馬・騎手・調教師・馬主の収集で `SubjectNotIdentified` になったジョブを起点に、識別情報の補正、必要な場合だけの名寄せ、再収集を実行したい。`SubjectNotIdentified` は検索結果0件でも発生するため、失敗ジョブの存在を重複レコードや名寄せ候補の存在と同一視してはならない。

本変更では、Collection Platformのfailure notificationを `/settings` に「要対応データ補正」として投影する。別の永続キューは作らず、データを自動変更せず、運用者が確認して実行するまで元failureを `Open` として残す。

## Goals

- activeな収集failure notificationのうち、error codeが完全一致で `SubjectNotIdentified` のジョブを、競走馬・騎手・調教師・馬主の要対応データ補正として表示する。
- 各失敗を「識別情報を補正して再収集」「安全な名寄せ後に再収集」「根拠不足で要確認」に分類し、名寄せ候補0件でも一覧と補正操作を失わない。
- `/settings` で要対応ジョブを主体別に絞り込み、安全な候補だけを選択して実行できる。
- 同一JRA識別子、または同一RaceEntryの再取得による置換を確認できる候補だけを作り、名前一致だけでは候補化しない。
- apply直前に根拠・参照・競合を再検証し、冪等なledgerと監査を残す。
- 競走馬・騎手・調教師の統合元について既存・将来のプロフィール収集を停止し、馬主は既存alias mapping／merge auditを使用する。

## Non-goals

- 収集失敗通知や再取得操作をデータ補正ジョブへ移すこと。
- 名前だけを根拠に候補を自動作成・自動適用すること。
- 任意のsource/target IDを画面から入力する汎用merge。
- Horse/Jockey/Trainer/Owner以外の主体、JRA以外のProvider。
- 馬主プロフィール本文の収集。
- 適用済み補正の取消。

## Experience and interaction design

### 対象ジョブの発生と評価

Collection Platformのactive failure notificationを正本とし、error codeが完全一致で `SubjectNotIdentified` のものを主体別に投影する。同一failure notificationを重複表示せず、Recovery中は処理中、解決済みまたはsupersededは要対応一覧から除外する。

評価時に既存subject、JRA source identity、同一RaceEntryの置換履歴を調べ、次のいずれかへ分類する。

- `RetryReady`: 重複レコードは確認されず（候補0件を含む）、有効なJRA URL／source identityを自動解決または運用者入力から検証できた。識別情報を補正して元failureのRecovery taskを作る。
- `MergeReady`: 同一JRA識別子または同一RaceEntry置換からsource→targetが一意に確認できた。名寄せと旧sourceの収集抑止後、targetでRecovery taskを作る。
- `Blocked`: 有効なURL／identityがない、候補が複数、根拠が不足、または参照が残る。理由を表示し、条件が解消するまで実行できない。

名寄せ候補が0件であること自体はエラーにも `Blocked` の理由にもしない。入力されたURLはHTTP(S)・JRA host・主体ページ種別・terminal identityを検証し、パラメーターのない `accessS` / `accessD` 等を保存・再実行しない。

- 競走馬: 現行のJRA `accessU` identity由来IDを優先し、既存Horse repair候補を新基盤へ引き継ぐ。
- 騎手・調教師: RaceCard上のプロフィールリンクから正規化したJRA source identityを保存する。リンクが得られない場合でも同一RaceEntryの旧ID→新ID置換は根拠として記録するが、表示名だけの横断検索では候補を作らない。
- 馬主: `ResourceType.Owner` と `owner-identity` definitionを追加するが、プロフィール本文は収集しない。RaceCardで得た馬主名とリンク／同一RaceEntry根拠をcanonical ownerへ解決する論理収集とし、識別できなければ `SubjectNotIdentified` を記録する。名寄せは同一RaceEntryの旧Owner対応先→新Owner対応先が一意の場合だけ行う。

同一sourceに複数targetがある場合は全候補を `要確認` とし、一括実行できない。対象外のerror codeや、通知のない単なる重複候補はこの一覧へ追加しない。

### `/settings` の表示と実行

既存の「データ補正」を「要対応データ補正」へ拡張する。上部に `すべて / 競走馬 / 騎手 / 調教師 / 馬主` の絞り込みと、要対応・要確認件数を表示する。各行・狭幅カードには次を表示する。

- 主体種別と状態（`再収集可能` / `名寄せ後に再収集` / `要確認` / `処理中`）
- 元の収集ジョブと `SubjectNotIdentified` のエラー内容
- 統合元・統合先の名称とID
- 根拠種別（JRA識別子 / 同一RaceEntry）と根拠レースへのリンク
- 影響する参照件数、停止する収集タスク件数
- 実行可能またはblockedとなる理由

実行可能候補だけを選択できる。`RetryReady` では必要に応じて補正URLを入力し、preview検証後に再収集する。`MergeReady` の確認Dialogは主体別のredirectまたはalias mapping、source収集停止、targetでの再収集、履歴保持、取消不可を表示する。成功後は適用・skip・収集停止・Recovery task件数を示して一覧を再読込する。部分失敗または応答不明時はジョブを消さず、再読込と冪等再実行を案内する。

### 状態と監査

補正側はfailure notification IDを冪等keyとするledgerだけを保持し、要対応状態はCollection Platformの `Open` / `RecoveryInProgress` / `Resolved` / `Superseded` を正本とする。評価結果は `RetryReady` / `MergeReady` / `Blocked` として都度算出する。通信失敗後は再読込でfailureとledgerを確認する。

## Navigation and relationships

- 導線は引き続き `運用 > その他設定 > 要対応データ補正` (`/settings`) とする。
- 収集の要対応は `/jobs`、名寄せ・参照補正の要対応は `/settings` とする。
- Horse/Jockey/Trainerのsource/targetは各詳細、OwnerはOwner詳細、RaceEntry根拠はRace詳細へリンクする。

## Mocks

- [デスクトップ／狭幅ワイヤーフレーム](mocks/actionable-repair-jobs-wireframe.md)

## Documentation updates

- `docs/20-admin-ui-design.md`: `/settings` のデータ補正を4主体の要対応ジョブへ拡張し、`/jobs`との責務分離を管理UIの正本へ反映する。
- 既存 [Horse専用repair change record](../20260914_horse-identity-repair-admin-ui/README.md) は実装履歴として保持し、本変更が後継の共通運用基盤であることを追記する。

## Technical impact

- Event Storeに主体種別付き `IdentityRepairLedger` と `IdentityRedirect` を追加する。ledgerはfailure notification IDを一意キーとして、評価根拠、補正location、merge結果、Recovery task IDを記録する。要対応キューを二重管理しない。
- RaceEntry収集契約へJockey/Trainer/OwnerのJRA source identityまたはraw linkと、以前の対応先を比較できる情報を追加する。Ownerにはidentity解決専用definitionを追加する。
- preview/apply APIを主体共通化し、現行Horse candidate/redirectは互換読取またはmigrationにより新APIへ表示する。
- applyは主体別strategyで処理する。Horse/Jockey/Trainerはredirect ledger、OwnerはOwnerAliasMappingとOwnerMergeAuditを更新する。すべてapply直前に同一性根拠とsource→target一意性を再検証する。
- Horse/Jockey/Trainerは、統合元IDを参照するRaceEntryが残っている間は `Blocked` とする。根拠となったRaceEntryを含め、再取得によって参照が統合先へ置換済みであることをapply直前に確認する。Ownerはsnapshot上の名称を履歴として保持し、alias mappingでcanonical ownerへ解決する。
- merge時は4主体ともCollection Platformのsource resourceを抑止し、target resourceでRecoveryを作る。RetryReadyではsourceを抑止せず、補正locationを追加して同じresourceをRecoveryする。
- redirect解決をHorseだけでなくJockey/Trainerの詳細取得と参照表示へ接続する。過去イベント・request/task/attempt履歴は書き換えない。
- 複数DBのpartial failureは、適用済みledgerを確認した再実行で不足したsuppressionを完了する。

## Decisions

1. 「要対応」の正本はCollection Platformのactive `SubjectNotIdentified` failure notificationとし、補正側に別キューを作らない。
2. 候補作成は同一JRA識別子または同一RaceEntry置換に限定し、名前一致だけを根拠にしない。
3. 運用者の明示実行を必須とし、自動applyしない。
4. 同一sourceのtargetが複数ならblockedとし、UIから安全判定を上書きできない。
5. Ownerはidentity解決専用の収集resourceを追加するがプロフィール本文は収集せず、既存alias mappingとmerge auditを名寄せの正本とする。
6. 既存Horse repairの監査履歴とredirectを保持し、新一覧から同じ安全境界で操作できるようにする。
7. 名寄せ候補0件は正常な評価結果であり、検証済みlocationがあれば名寄せなしでRecoveryできる。

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
|---|---|---|---|---|
| AC1 | active failure notificationのerror codeが完全一致で`SubjectNotIdentified`の4主体だけが、通知ごとに重複なく要対応一覧へ表示される。 | T1,T2,T3 | failure projection integration tests | Proposed |
| AC2 | `/settings`で4主体を絞り込み、元ジョブ、エラー、名称・ID・根拠・影響・評価結果を確認できる。 | T4 | bUnit/browser | Proposed |
| AC3 | 実行可能候補だけを選択でき、blocked、同一source複数target、または統合元RaceEntry参照が残るHorse/Jockey/Trainerを実行できない。 | T2,T4 | API/bUnit conflict tests | Proposed |
| AC4 | 実行直前にfailure状態、URL/identity、根拠、参照、redirect/alias競合を再検証し、failure notification単位で冪等に処理する。 | T2,T3 | transport+persistence integration tests | Proposed |
| AC5 | MergeReadyでは4主体の統合元収集を取消・抑止してtargetでRecoveryし、RetryReadyではsourceを抑止せず補正locationでRecoveryする。 | T3 | Collection Platform integration tests | Proposed |
| AC6 | Horse/Jockey/Trainerの旧ID詳細参照はcanonicalへ解決され、Ownerはalias mappingとmerge auditで統合を確認できる。 | T3 | subject endpoint tests | Proposed |
| AC7 | 名寄せ候補0件でもエラーにせず、有効な補正locationがあれば名寄せなしでRecoveryできる。名寄せ・suppression・Recovery間の失敗も再実行で不足分だけ完了できる。 | T3 | zero-candidate + partial-failure/retry tests | Proposed |
| AC8 | loading/empty/error/successを区別し、狭幅・keyboard操作でも情報と主操作を失わない。 | T4 | bUnit + real browser | Proposed |
| AC9 | migration後も既存Horse repair候補・redirect・監査履歴を参照でき、パラメーターのない`accessS`/`accessD`等は補正locationとして受理されない。 | T1,T3 | migration + URL validation tests | Proposed |
| AC10 | 収集の要対応は`/jobs`、データ補正の要対応は`/settings`に分離される。 | T4 | navigation/browser test | Proposed |

## Delivery plan

1. failure notification連携ledger/redirect schemaと互換migrationを確定する。
2. 4主体のsource identityとOwner identity解決をRaceEntry・収集経路へ接続する。
3. 主体別preview/apply、redirect/alias、suppression、retryを実装する。
4. `/settings`を主体横断一覧へ変更する。
5. transport+persistence、component、browser、全回帰テストを実行する。

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
|---|---|---|---|---|---|---|---|---|
| T1 | failure連携ledger/redirect schema・migration・互換読取 | Main | Lead tier | - | Application read models, Infrastructure persistence/migrations | migration tests | old/new DB compatibility | Runnable |
| T2 | 4主体failure投影、Owner identity収集、補正・名寄せ安全性判定 | Main | Lead tier | T1 | collection write/API contracts/detection tests | failure/candidate integration tests | AC1,AC3 evidence | Dependent |
| T3 | 主体別apply、redirect/alias、suppression、retry | Main | Lead tier | T1,T2 | repair API, Collection Platform, endpoint tests | transport+persistence tests | AC4-AC7 evidence | Dependent |
| T4 | `/settings`主体横断UIと操作 | Worker candidate; Main review | Worker tier | T2,T3 contract freeze | Settings/AdminApiClient/component tests | bUnit/browser | AC2,AC3,AC8,AC10 evidence | Dependent |
| T5 | 文書同期、全回帰、最終監査 | Main | Lead tier | T1-T4 | docs/change record only | format/build/test/diff/status/CodeGraph | all AC/task reconciliation | Dependent |

## Review gates

- **Design and task-split review** — Reviewer: Main。Inputs: CodeGraph、現行Horse repair、Collection Platform failure/recovery、4主体識別・merge棚卸し、2件のread-only worker調査。Decision: active `SubjectNotIdentified` failureを正本とし、候補0件をRetryReadyとして扱う。Ownerはprofile収集ではなくidentity解決definitionを追加する。schema/API/データ整合性はMainが直列実装し、契約freeze後のUIだけを独立委譲候補とする。T1→T2→T3→T4→T5の依存とAC coverageを確認。Follow-up: 改訂設計のユーザー承認後にPre-implementation reviewを記録する。
- **Pre-implementation review** — Reviewer: Main。Approval: 2026-09-14、ユーザーがAC1〜AC10、候補0件のRetryReady、Owner identity definitionを含む改訂設計を承認。T1を`Runnable`、T2〜T5を依存順に`Dependent`とする。共有schema、migration、API contract、generated snapshotはMainのみが変更する。T1〜T3の契約をfreezeするまでUI workerを開始しない。Worker inputsは承認済みchange record、確定API contract、UI skill、対象component/testに限定し、契約変更・migration・外部操作を禁止する。契約の曖昧さ、テスト失敗1回後の範囲拡大、共有ファイル変更が必要ならMainへescalateする。
- **Checkpoint review** — 各checkpointで記録する。
- **Final review** — 全task/ACをVerifiedへ照合後に記録する。

## Verification record

- 2026-09-14: `codegraph explore`で現行Horse候補のトリガー→candidate→preview/apply→suppression→UIを確認した。
- 2026-09-14: read-only worker 2件で、4主体の識別子・merge・参照関係と、repair UI／収集failureとの責務差を並行棚卸しした。両成果をMainが実コードとCodeGraphで照合し、Ownerに安定外部IDとCollection resourceがないこと、Jockey/Trainerにsource identity伝播がないことを確認した。
- Delegation usage/cost: measured token/cost unavailable。read-only調査2件、再試行0、escalation0。書込なし。Lead decision: 設計入力として採用。
- 2026-09-14: 最初の承認依頼がchange recordへのリンクと主要仕様だけを示し、AC1〜AC10の観測可能な受け入れ条件を承認判断用に説明していなかった。原因はDocument Driven Developmentスキルに承認依頼本文のacceptance-summary gateがなかったことと確認し、同スキルへ全AC IDを含む概要説明を必須化した。`quick_validate.py`（`Skill is valid!`）と`git diff --check`を実行した。
- 2026-09-14: CodeGraphで `SubjectNotIdentified` がsubject collection handlerから`ResourceNotFound`としてfailure notificationへ記録され、既存のRecovery APIが通知を正本に再収集taskを作ることを確認した。また現行`ResourceType`にはOwnerがなく、馬主名はRaceCardから直接保存されるため、Ownerはidentity解決専用resource/definitionが必要と確認した。
- 2026-09-14: ユーザー補足により、検索結果0件でも`SubjectNotIdentified`が発生することを要件化した。失敗通知とmerge candidateを分離し、0件では検証済みlocationによるRetryReady、重複が安全に確認できる場合だけMergeReadyとする設計へ改訂した。

## Deviations and follow-up

- 本番データへの補正実行は実装・ローカル検証に含めない。
- 2026-09-14に改訂設計が承認された。production codeはPre-implementation review記録後に変更する。

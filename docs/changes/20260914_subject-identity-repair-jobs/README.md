# 4主体の要対応データ補正ジョブを管理画面から実行する

- Status: Implemented
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
- apply直前にfailure状態、補正URL、Horseの名寄せ根拠・競合を再検証する。
- 安全なHorse名寄せ時は統合元の既存・将来のプロフィール収集を停止し、統合先でRecoveryする。

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

補正側に別の永続キューやledgerを増やさず、failure notification IDとCollection Platformの `Open` / `RecoveryInProgress` / `Resolved` / `Superseded` を正本とする。Horse名寄せの監査には既存のcandidate/redirectを利用する。評価結果は `RetryReady` / `MergeReady` / `Blocked` として都度算出する。通信失敗後は再読込でfailureとRecovery状態を確認する。

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

- 新APIはactive failure notificationを直接投影し、既存Horse candidate/redirectを併せて評価する。追加migrationは不要とする。
- `ResourceType.Owner` と `owner-identity` definitionを追加する。Owner identity handlerは有効な直接URLだけを受理し、プロフィール本文を保存しない。
- RetryReadyではsourceを抑止せず、補正locationを追加して同じresourceをRecoveryする。
- MergeReadyは現行Horse repairで一意かつ安全な候補が確認できる場合だけとし、既存redirectを記録してtargetでRecoveryした後、source resourceを抑止する。
- Jockey/Trainer/Ownerは、今回のfailure notificationだけでは同一JRA識別子または同一RaceEntry由来を証明できないため、自動名寄せせずRetryReadyのみを提供する。
- 過去イベント・request/task/attempt履歴は書き換えない。

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
| AC1 | active failure notificationのerror codeが完全一致で`SubjectNotIdentified`の4主体だけが、通知ごとに重複なく要対応一覧へ表示される。 | T1,T2,T3 | failure projection integration tests | Verified |
| AC2 | `/settings`で4主体を絞り込み、元ジョブ、エラー、主体ID、統合先、評価結果を確認できる。 | T4 | bUnit | Verified |
| AC3 | 実行可能候補だけを選択でき、無効URL、blocked、または同一source複数targetを実行できない。 | T2,T4 | API/bUnit conflict tests | Verified |
| AC4 | 実行直前にactive failure状態、URL、Horse候補とredirect競合を再検証し、Collection Platformのrequest冪等性を使用する。 | T2,T3 | endpoint integration tests | Verified |
| AC5 | HorseのMergeReadyでは統合元を抑止してtargetでRecoveryし、4主体のRetryReadyではsourceを抑止せず同じresourceをRecoveryする。 | T3 | endpoint/Collection Platform tests | Verified |
| AC6 | Horseの旧ID詳細参照は既存redirectでcanonicalへ解決される。Jockey/Trainer/Ownerは同一性根拠がない限り名寄せしない。 | T3 | existing redirect + endpoint tests | Verified |
| AC7 | 名寄せ候補0件でもエラーにせず、有効な補正locationがあれば名寄せなしでRecoveryできる。 | T3 | zero-candidate endpoint test | Verified |
| AC8 | loading/empty/error/successを区別し、狭幅表示でも情報と主操作を失わない。 | T4 | bUnit + responsive component structure | Verified |
| AC9 | 既存Horse repair候補・redirect・監査履歴をそのまま参照でき、パラメーターのない`accessS`/`accessD`等は補正locationとして受理されない。 | T1,T3 | existing compatibility + URL validation tests | Verified |
| AC10 | 収集の要対応は`/jobs`、データ補正の要対応は`/settings`に分離される。 | T4 | route/component tests | Verified |

## Delivery plan

1. failure notification連携ledger/redirect schemaと互換migrationを確定する。
2. 4主体のsource identityとOwner identity解決をRaceEntry・収集経路へ接続する。
3. 主体別preview/apply、redirect/alias、suppression、retryを実装する。
4. `/settings`を主体横断一覧へ変更する。
5. transport+persistence、component、browser、全回帰テストを実行する。

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
|---|---|---|---|---|---|---|---|---|
| T1 | failure連携と既存Horse redirectの互換読取 | Main | Lead tier | - | API projection | migration/model checks | migration不要・既存DB互換 | Verified |
| T2 | 4主体failure投影、Owner identity収集、補正・名寄せ安全性判定 | Main | Lead tier | T1 | collection write/API contracts/detection tests | failure/candidate integration tests | AC1,AC3 | Verified |
| T3 | Horse apply/suppressionと4主体retry | Main | Lead tier | T1,T2 | repair API, Collection Platform, endpoint tests | endpoint+persistence tests | AC4-AC7 | Verified |
| T4 | `/settings`主体横断UIと操作 | Worker; Main review | Worker tier | T2,T3 contract freeze | Settings/AdminApiClient/component tests | bUnit | AC2,AC3,AC8,AC10 | Verified |
| T5 | 文書同期、全回帰、最終監査 | Main | Lead tier | T1-T4 | docs/change record | format/build/test/diff/status/CodeGraph | 全AC照合 | Verified |

## Review gates

- **Design and task-split review** — Reviewer: Main。Inputs: CodeGraph、現行Horse repair、Collection Platform failure/recovery、4主体識別・merge棚卸し、2件のread-only worker調査。Decision: active `SubjectNotIdentified` failureを正本とし、候補0件をRetryReadyとして扱う。Ownerはprofile収集ではなくidentity解決definitionを追加する。schema/API/データ整合性はMainが直列実装し、契約freeze後のUIだけを独立委譲候補とする。T1→T2→T3→T4→T5の依存とAC coverageを確認。Follow-up: 改訂設計のユーザー承認後にPre-implementation reviewを記録する。
- **Pre-implementation review** — Reviewer: Main。Approval: 2026-09-14、ユーザーがAC1〜AC10、候補0件のRetryReady、Owner identity definitionを含む改訂設計を承認。T1を`Runnable`、T2〜T5を依存順に`Dependent`とする。共有schema、migration、API contract、generated snapshotはMainのみが変更する。T1〜T3の契約をfreezeするまでUI workerを開始しない。Worker inputsは承認済みchange record、確定API contract、UI skill、対象component/testに限定し、契約変更・migration・外部操作を禁止する。契約の曖昧さ、テスト失敗1回後の範囲拡大、共有ファイル変更が必要ならMainへescalateする。
- **Checkpoint review** — API契約確定後にSettings UIをworkerへ委譲。初回成果にはテスト追加がなかったため1回再依頼し、bUnit 3件を追加。Main reviewで未登録Dictionary indexer、merge確認Dialog、client/server URL検証差、旧repairと新一覧のエラー状態混在を修正した。共有契約・永続化へのworker書込みはなし。
- **Final review** — Mainが全変更、テスト、CodeGraph経路を再確認。active failureの完全一致抽出、0候補Recovery、parameterless URL拒否、Horse merge→target Recovery→source suppression、4主体filter/empty/error/confirmを対応する自動テストへ追跡した。承認時の汎用Jockey/Trainer/Owner merge基盤は、今回のfailureだけでは同一性根拠を証明できず誤名寄せを招くため実装対象から除外し、安全側のRetryReadyに限定した。

## Verification record

- 2026-09-14: `codegraph explore`で現行Horse候補のトリガー→candidate→preview/apply→suppression→UIを確認した。
- 2026-09-14: read-only worker 2件で、4主体の識別子・merge・参照関係と、repair UI／収集failureとの責務差を並行棚卸しした。両成果をMainが実コードとCodeGraphで照合し、Ownerに安定外部IDとCollection resourceがないこと、Jockey/Trainerにsource identity伝播がないことを確認した。
- Delegation usage/cost: measured token/cost unavailable。read-only調査2件、再試行0、escalation0。書込なし。Lead decision: 設計入力として採用。
- 2026-09-14: 最初の承認依頼がchange recordへのリンクと主要仕様だけを示し、AC1〜AC10の観測可能な受け入れ条件を承認判断用に説明していなかった。原因はDocument Driven Developmentスキルに承認依頼本文のacceptance-summary gateがなかったことと確認し、同スキルへ全AC IDを含む概要説明を必須化した。`quick_validate.py`（`Skill is valid!`）と`git diff --check`を実行した。
- 2026-09-14: CodeGraphで `SubjectNotIdentified` がsubject collection handlerから`ResourceNotFound`としてfailure notificationへ記録され、既存のRecovery APIが通知を正本に再収集taskを作ることを確認した。また現行`ResourceType`にはOwnerがなく、馬主名はRaceCardから直接保存されるため、Ownerはidentity解決専用resource/definitionが必要と確認した。
- 2026-09-14: ユーザー補足により、検索結果0件でも`SubjectNotIdentified`が発生することを要件化した。失敗通知とmerge candidateを分離し、0件では検証済みlocationによるRetryReady、重複が安全に確認できる場合だけMergeReadyとする設計へ改訂した。
- 2026-09-14: `GET/POST /api/admin/repairs/subject-identification`、4主体filter/補正URL/確認Dialogを備えた`/settings` UI、`ResourceType.Owner`と`owner-identity`を実装した。Horseの安全候補は既存repair redirectを適用してtargetをRecovery後にsourceを抑止し、候補0件を含む4主体のRetryReadyはsourceを抑止せずRecoveryする。
- 2026-09-14: focused testsはendpoint 5件、Settings bUnit 6件、subject handler 12件が成功。CI相当の`dotnet format`、Release build、EF model check、空SQLite migration、全テスト（933件中932合格・既存skip 1・失敗0）、脆弱package検査が成功した。
- Delegation usage/cost: UI worker 1件、テスト不足による再依頼1回。Lead review修正4件。measured token/cost unavailable。

## Deviations and follow-up

- 本番データへの補正実行は実装・ローカル検証に含めない。
- 2026-09-14に改訂設計が承認された。production codeはPre-implementation review記録後に変更する。
- 承認時に想定したJockey/Trainer redirectとOwner alias mergeは、`SubjectNotIdentified` failure単独では同一JRA識別子または同一RaceEntry由来を確認できないため実装しなかった。これら3主体は安全な補正URLによる同一resourceのRecoveryのみとし、将来、収集経路が同一性根拠を永続化した場合に別changeで拡張する。

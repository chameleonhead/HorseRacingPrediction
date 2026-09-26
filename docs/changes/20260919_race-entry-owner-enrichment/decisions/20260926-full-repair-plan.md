# 取得修正から本番補正までの一括設計案

> **運用案撤回:** 利用者が全体停止を不要と指定したため、本書のoffline補正・全体メンテナンス案は採用しない。最新の運用設計は[取得revision更新による再取得](20260926-revision-recollection.md)。馬番確定前後の公開段階と、誤帰属を防ぐ検証条件は維持する。

- Governing record: [README](../README.md)
- Status: Proposed
- Updated: 2026-09-26
- User direction: 「対応をお願いします」の後、段階先行案ではなく「本番補正まで含めた一括の設計を先に確認する」を選択。これは本設計の実装・停止・applyの承認ではない。
- 本書は[調査補足](20260926-race-integrity.md)の未確定だった実行方式を具体化する。過去の観測を上書きしない。

## 到達点と対象

通常取得・明示再取得で正しい馬主を保存できるよう修正し、対象 `race-fca5d100-9e2f-5074-a74c-bad8cdb4705f` の馬番・馬主・格付けを公式情報に合わせ、本番再取得と保存後照合まで確認する。他レースの一括補正、予想の意図変更、過去イベント削除、直接SQLによる値の置換、新しい自動RecoveryやWorkflowは含まない。

本番操作は一時的な全体メンテナンスを伴う。対象Race限定のオンラインholdを新設する案は今回採用せず、既存のAPI停止運用を使い、公開アプリとbackground writerを止めた状態で専用のオフライン補正を行う。停止時間は検証環境で測定して操作前に提示する。停止・補正・復旧の実行は本設計と影響への明示承認、環境のアクセス回復、事前条件の検証に依存する。

## 根拠と直近観測

- 2026-09-26 04:26:04 JST、認証済みGETでcontext entries=16、raw owner=0、comparison predictionTickets/entryResults=[]、payoutResult=nullを再確認。contextはOddsSnapshotsフィールドを公開していないため、オッズが0件とは判定しない。初回の認証なしGETは401であり、データ証拠から除外する。
- read-only explorer（requested model `gpt-6-sol`、実モデル・token使用量は未確認）によるIT4参照調査を主担当が統合。編集・テストなし。CodeGraphとコード引用が成果であり、テスト成功の主張ではない。
- `EndpointExtensions.RaceResultBulk.cs` は既存entryをdomainへの入力から除外する。`EndpointExtensions.RaceRefresh.cs` は既存馬番からEntryIdを選び、関連主体を書いた後にRaceを更新する。
- `RaceResultViewReadModel.cs` はEntryRegisteredだけでは既存結果snapshotのHorseIdを更新しない。`HorseRaceHistoryReadModel.cs` 等は旧馬のentryを除去するが、結果はEntryResultDeclaredで反映する。`PredictionTicketReadModel.cs` のmarkはEntryId参照、`TrainingDataBuilder.cs` は現在のentryと結果をEntryIdで結合する。単純な馬入替は予想・結果・学習データの意味を変える。
- `deploy/docker-compose.yml` はapiが `/data/eventstore.db` とcollection/prediction DBを持つ構成。`.github/workflows/app-deploy.yml` にapi停止の既存運用がある。ただし配備中の実構成・追加writerは未確認であり、現地preflight必須。

## 取得・保存の設計

### 馬番確定前と確定後を分ける（利用者指摘を反映）

木曜日時点の馬番未確定は正常な公開段階として扱う。前案の「不完全Cardを拒否する」だけでは、確定前の正常入力を障害扱いしたり、発走後まで再取得されなかったりするため、以下の遷移を必須にする。曜日は取得時刻の目安にとどめ、特別な公開日程も含め公式ページの明示馬番・公開状態を正本とする。

| 公開段階 | 保存と収集状態 | 次の動作 |
| --- | --- | --- |
| 出走馬一覧あり・馬番未確定 | `馬番確定待ち`。仮採番せず、確定RaceEntry・結果を作らない。CardをCurrent/補正完了にしない | Race identity・公式発走時刻・公開状態の診断evidenceは保持し、既存schedulerで次回確認を予約 |
| 明示馬番・公式馬識別子が揃う | 全頭対応とCard必須情報を検証して初めて確定entryを保存 | raw保存値を確認しCardをCurrentへ。owner未公開は取得待ち、公開済みownerの解析失敗はvalidation failureに分類 |
| 確定後に古い未確定ページが返る | 確定済みentryやCard状態を未確定に戻さず、再取得の観測として記録 | 既存値を保全。古い入力で上書き・新規entry追加をしない |

- 確定待ちは正常な待機であり、通常の処理失敗と同じretry予算・dead-letter・全体停止・障害通知に載せない。待機中でも最終的なCard取得義務を残す。
- 再確認は公式公開evidenceと既存の公開状態駆動schedulerに接続する。発走時刻の予約はResult用であり、Cardの次回確認を発走後へ押し出さない。固定の「毎週金曜なら必ず確定」という判定や別schedulerは追加しない。
- 過度なアクセスを避ける既存probe間隔を尊重し、確定後は発走前にCardが保存されることを時刻制御テストで確認する。公開終了まで取得できなければ欠損を明示し、取得成功にしない。
- 確定前の名簿を仮のRaceEntryや新しい利用者向け出走表として永続化する機能は今回追加しない。診断snapshotに含まれる馬名・公式identityは証拠として保全できるが、確定後は最新Cardの全出走馬を取り直す。除外・追加・順序変更を想定し、当初名簿の頭数・位置に固定しない。
- 既に誤った仮馬番entryがある対象Raceは、確定待ちの導入だけでは修復されない。今回の専用補正契約を適用し、通常再取得で自動付替えしない。
- 今回の初回取得日は木曜日だったため、この公開段階を取り違えた可能性がある。ただし当時snapshot未取得のため、具体的な原因の確定とは区別する。

1. 出走馬番は明示された公式値のみ採用する。未公表・解析不能・重複・不正な馬番を出現順の連番で代用しない。枠順未確定は未公開状態、公開済み構造の解析不能は構造化validation failureとして区別する。
2. 馬主はCard由来に限定する。不完全Cardからentryを部分保存しない。Resultに馬主必須条件を課さず、既存のCard由来値をnullで消さない。Cardの件数は入力行数から自己正当化せず、対象テーブルと公式出走情報の取得完全性を検証する。取消行も明示馬番とidentityがあれば保持する。
3. 全行のRace identity、馬番、公式Horse source identityの正規化・一意性・既存対応を、関連主体作成・プロフィール更新・引用保存・収集要求登録より先に検証する。通常/refreshとも不一致を自動修復せず対象Race全体を拒否する。
4. 既存HorseIdが公式identityに一致しない、または既存entryを同じ馬と証明できない場合、名前だけで結合せず明示的な識別未解決にする。既存の手入力IDやsource linkのない過去Resultの取り扱いも回帰試験対象とし、既存entryへの更新を安全確認なしで許可しない。新規の過去Result取込みは既存契約の名前由来IDを維持できるが、馬番入替補正には利用しない。
5. 同じ馬・同じ馬番の既存entryもdomainへ渡し、非空の後着値だけをマージする。bulk/refreshの両方で同一内容の再送はRace event/versionを増やさない。domain側でもHorseId/馬番一致を再検証して、古いread modelに基づく更新を拒否する。
6. API受付成功やparse owner数だけで成功判定しない。保存後のraw entryと期待したidentity・馬主を照合し、未反映なら成功扱いにしない。開始時刻待ちによって整合性エラーを隠さない。副作用前拒否の試験と、競合によるcommit拒否の試験を分ける。すべての主体更新を単一transactionと誤称しない。
7. 格付けは対象レースの見出し・その領域の画像から取得し、出走馬の過去戦績や別レースのG表記を採用しない。読めない場合は不明として分類し、推測で補正しない。Card/Result双方の回帰を行う。

## 対象限定の補正契約

### Read-only preview

専用オフラインtoolは既定をpreviewとし、公開HTTP・scheduler・collector・自動migrationを起動しない。明示された対象DBとRaceだけを読み、公式Cardの取得URL・日時・race identity・内容hash、16頭のsource identity、旧新のEntryId/HorseId/馬番/馬主/枠/格付け、参照棚卸し、aggregate version/fingerprintを出力する。秘密情報を出力しない。資料取得は通常収集の書込みworkflowを使わない。

補正payloadは公式Cardに根拠のある項目と同一HorseIdに属する既存値のみで構築する。旧「馬番の占有馬」のowner・体重・騎手等をnull coalesceで引き継がない。公式の欠落値を他の馬や現在プロフィールから補わない。

### Applyの許可条件

- 公式と保存済みHorseIdの集合が一致し、全頭の1対1対応を解決できる。重複、未解決、出走頭数変更、未知の追加馬は停止して再設計する。
- 対象の予想ticket（draft/withdrawnを含む）、mark、買い目、評価、結果・払戻、結果由来履歴、オッズsnapshot、学習用保存データが存在しないことを全投影とイベントから確認する。存在する場合は**変更せず中止**する。4:26のcomparisonが空であることだけではこの条件を満たさない。
- Card由来のentry・体重履歴は許容するが、旧新対応に従って各projectionの旧所属を除去し正しいHorseIdへ再生成することを検証する。Race/Horse主体のmemoとcitationはID自体を変えず保持する。未知の参照形式は拒否する。
- 停止中のDBがpreviewのversion/fingerprintと一致し、復元試験済みbackupが存在し、補正tool以外のwriterがない。preview以降の変化は再previewし、古い差分をapplyしない。

専用の監査可能なdomain correction command/eventで16頭の割当とCard値・gradeを一括確定する。過去イベントは削除しない。operation ID、根拠hash、旧新対応、期待versionを記録し、同じoperation IDの再送でeventを重複させない。projection失敗時は停止状態を保って再構築・照合し、正常確認前に公開しない。

新しい予想や結果等が見つかった場合に自動で捨てたり別馬へ付け替えることはしない。この場合はIAC5未完了のまま、実データを示して追加の移行設計を求める。これは成功扱いの除外ではなく、データ消失を防ぐ実行停止条件である。

## 本番実行順序と復旧

| 順序 | 実施内容 | 必須証拠・停止条件 |
| --- | --- | --- |
| 1 | 当時log/snapshotと配備版を照合し、公式Cardを安全に保存。修正版を隔離した検証DBで試験 | 当時資料がなければ初回原因の断定を留保。公式Card取得不能ならプロフィールで代用せず停止 |
| 2 | 本番の実ホスト・image digest・DB絶対パス・全writer・queue/lease・デプロイ競合を棚卸し | AWS/SSH等の権限不足、未知writer、別deploy進行中なら停止 |
| 3 | 承認された時間帯にdispatchをpauseし既存実行をdrain。公開apiを停止 | cancel単独やpause単独を隔離保証にしない。Lambda残存・配信中payload・再送の排除を確認し、queueは削除しない |
| 4 | 停止中のEventStore、collection、predictionの整合したbackupを取得し、別コピーで復元検証 | SQLite WAL/SHMを含む整合性をbackup APIまたは検証済み手順で保証。稼働DBファイルの単純copyだけを証拠にしない |
| 5 | 最新公式資料と停止DBでpreview、参照ゼロと差分/versionを照合 | 想定と異なる対象/参照/値ならapplyせず、利用者へ提示。手動の影響容認なしに拡張しない |
| 6 | ネットワーク公開なし・hosted servicesなしの補正toolを唯一のwriterとして実行 | 対象Raceを固定し、operation IDとevent差分を監査。他Raceや予想・結果を変更しない |
| 7 | 停止状態でraw entry、16頭対応、owner16/16、G3、関連projection、他Race不変を照合 | 失敗なら再開せず原因究明。必要なら停止中の同一時点backupへ復元して整合性確認。復元は明示承認対象 |
| 8 | 修正版APIとCollectorのみを起動。古いworker/古いpayloadが適用されないことを確認 | ロールバック先の旧不具合コードで収集を再開しない。補正後も未公開検証を優先 |
| 9 | 対象の検証済みCard取得を1回実施しraw一致・冪等性を確認、dispatchをresume | 既存要求との重複を避け対象を限定。対象Result公開後の通常経路も監視し、誤帰属なしを確認してIAC5完了 |

メンテナンス中はアプリの閲覧・予想等が利用できない。再開後の新規データが発生した後は全DB巻戻しを自動実行しない。障害時は再度停止して差分を保全し、前進修復または別途承認された復旧を選ぶ。

## Concern dispositions（今回の承認候補）

| ID | 提案する処置 | 残存risk・再検討条件 | State |
| --- | --- | --- | --- |
| IC1 | 全体メンテナンスで全writer停止、復元可能backup、唯一のoffline writerによる補正を採用 | 停止時間が発生。実環境の排他確認不可なら実行しない。時間帯・停止容認は利用者の判断が必要 | Open decision |
| IC2 | 当時証拠調査を継続し、未保存なら初回原因を断定せず、実証済み通常bulk欠陥と同型の反例を直す | 9/24の実行そのものを再現したとは主張しない。新証拠が設計と矛盾したらProposedへ戻す | Resolved in design |
| IC3 | 予想・結果・オッズ等の参照があればapply拒否。参照なしのCard割当だけ監査eventで補正 | 実行直前に参照が増えていれば再設計が必要。利用者データを捨てて条件を満たさない | Resolved in design |
| IC4 | raw値・event数・通常/refresh/実HTTP・保存境界・復旧再送を統合試験 | 表示補完を検証根拠にしない。projection失敗は未完了 | Resolved in design |

IC1の停止容認が未決のため、まだApprovedにしない。これは操作許可の依頼前の設計提示であり、必要なアクセスがないまま実行は開始しない。

## 実装分担・検証と完了条件

元のIAC1–IAC5/IT1–IT5を維持し、本書で具体化する。追加のIAC6はIT3が担当し、確定前→確定後の実scheduler/handler/persistence遷移を検証する。IT2はオンラインholdの実装ではなく、上記の隔離手順・排他実証を担当する。

- IT1 Main/Lead: 当時証拠と実環境確認。過去証拠・権限・原因の解釈が必要で委譲しない。IT4参照inventoryのみread-only explorerへ分離済み（成果受領、コード根拠あり、テストなし）。
- IT2 Main/Lead: maintenance、backup/drain、復旧試験。運用安全性・共有DB判断のため専有。Write scopeは既存配備手順に必要な最小修正と本record、操作runbook。新Workflowは作らない。
- IT3 parser slice: 設計承認後、cost-sensitive coding workerが `RaceCardPageParser.cs`、`RaceGrade.cs` と対応parser testsを専有。判断固定は仮馬番禁止、対象レースだけのgrade、公開状態分類。初回focused correctionでも回帰失敗ならMainへ戻す。依頼時に正確なtest filterを確定。
- IT3 persistence slice Main/Lead: API通常/refresh、Card workflow、domain、共有契約と関連integration testsを直列専有。identity・副作用境界・互換性・競合が結合しているためLead保持。parser成果は独立した16頭入替反例で統合確認する。
- IT4 Main/Lead: 専用offline preview/apply command、必要なevent/projection、toolとtestsを専有。migration・data integrityのためLead保持。IT3契約確定後に実装し、書込み所有を重ねない。
- IT5 Main/Lead: 検証済みversionの配備、承認済みmaintenance、限定補正、通常Card/Resultの本番照合。アクセス・停止承認とIT2–IT4の検証に依存。未完了を別記録へ移して完了にしない。

主な反例: 馬番なし/重複、owner0/16・15/16、公式HorseIdと既存馬番の入替、末尾行で不一致、関連主体未作成状態での拒否、過去戦績のG2が混ざるG3 Card、source linkのないResult、古いpreview、同一operation再送、途中projection失敗、予想/結果/オッズが1件あるDB、API再起動・旧payload再送、他Race不変。

公開段階の必須反例: 木曜日の未確定名簿→確定Card（並び順・出走頭数も変化）、例外日程で早く確定するCard、発走時刻を取得済みでもCard probeを継続、待機反復・再起動後も要求を失わない、確定後に遅着した未確定入力が戻らない、馬番セルがあるのに解析不能な入力を正常待ちと誤分類しない。parser単体だけでなくStoreの次回予定とhandlerの結果分類まで検証する。

検証コマンドは各 `Scraping.Tests`、`Domain.Tests`、`Application.Tests`、`Collector.Tests`、`Api.Tests`、`Infrastructure.Tests` の関連testを最小単位から実行し、最終的に `.github/workflows/app-ci.yml` と同じformat、Release build、非External全test、migration検証を実行する。通常取得とrefreshの実HTTP→永続化→raw読出し、EventFlow event数、停止DBコピーでのpreview/apply/replay/復元、対象公式Cardの外部試験を必須とする。

## Review / audit checkpoint

- Design/task-split: Main。判断とparser sliceを分離し、DB/identity/操作安全性はLeadに保持。read-only explorerのみdispatch済み、実装workerは未dispatch。
- Routing/Audit/Result metrics: IT4 inventoryはexplorer `entry_reference_inventory` / requested `gpt-6-sol`、観測model/token unavailable、retry0、書込0、主担当統合review1。その他はLead、audit none。delegated coding開始時には実行auditを作成する。
- Pre-implementation / Final implementation review: 未実施。StatusはProposed。完成済みなのは今回の設計具体化であり、問題解決ではない。
- Independent inventory/design review: 同explorerが本書をread-only reviewし、参照inventoryとの重大な不整合なし。comparisonだけでなくPredictionTicketとevent streamの双方を見る条件を再確認した。主担当が当該条件を本書に維持。review合計2、修正コードなし。文書validator issues=0、`git diff --check`成功。build/testは文書のみのため未実行。
- 次の操作: IC1の全体停止容認と時間帯を利用者へ確認し、懸念処置・全IAC・本番操作範囲をまとめて明示承認を得る。承認前はコード・本番状態を変更しない。
- Intentional uncommitted: governing README、調査補足、本書、正本契約の提案リンク。利用者の既存変更は保持する。

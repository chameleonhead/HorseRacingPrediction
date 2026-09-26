# 対象レースの収集保留による補正・再取得案

- Status: Proposed
- Owner: Main
- Updated: 2026-09-26
- JRA site contract impact: Updated — 取得元は変更せず、補正の保留・再取得境界をdocs/27へ接続する。
- Governing record: [出走割当・馬主取得](../README.md)
- Previous design: [配備済みの限定補正と本番preview](20260926-number-repair-impact.md)

## 結論・承認境界

**対象レースだけを永続的に保留し、待機要求を失わず補正、整合検証後に取得revision4以上で再取得する。** 全体停止を新設せず、既存の同一性チェックを緩めない。今回作成したのは対応案のみ。実装・配備の承認と、本番の対象一覧/保留/補正/解除/再開の承認は分離する。

利用者は本turnで「停止解除は私が手動で実行しました」と説明した。前記C207の「解除経路不明」はこの説明により解消し、AWS再認証・アクセスログ提供を設計の前提から外す。自動解除の不具合を確認したわけではない。15:55の中山7R保存拒否は、手動再開後にも既存の不正割当が残っていたことを示す。操作への非難ではなく、未補正対象を実行から分離する仕組みが必要という設計上の課題である。

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Not started | hold/世代/解除APIは未実装。既存の補正eventと排他は配備済み |
| Verification | Not started | sourceに基づく設計確認のみ。実SQLite/HTTP/配信/再起動試験が必要 |
| Deployment/operation | Not started | 今回本番API操作なし。hold/apply/release/resumeは別途承認 |

## 根拠・反証した仮説

| 仮説 | 観測と最小反証 | 結論 |
| --- | --- | --- |
| Running=0なら補正可能 | 既存本番previewは中山5R/阪神11RともActiveCollection拒否。HasActiveRaceMutationAsyncはActiveTasks全件を検査 | 棄却。Readyも実行可能性を持つ |
| pauseしてcancelすればタスクなしになる | CancelTaskAsync→MaterializeUnsatisfiedRevisionAsyncは高revisionを即生成。CollectionScheduleServiceは停止中もdue stateを登録 | 安定条件として棄却。scheduler tick/terminal生成を制御する必要がある |
| 配備が勝手に再開した | workflowは停止維持分岐、通常Acquireも停止検査。利用者が手動再開を説明 | 自動解除の欠陥という仮説は採用しない |
| 2Raceだけ直せば復旧する | 中山7Rにも同型の保存拒否。旧取得batchの他Raceは未照合 | 未証明。候補一覧を先に照合し、適用対象を個別決定 |
| 公式全頭照合だけで自動補正できる | 中山5R14頭/阪神11R16頭は一致したが、他対象の独立参照・現在版は未確認 | 将来の適用gateとし、未確認対象へ一般化しない |

参照source: CollectionPlatformStoreのHasActiveRaceMutationAsync/CancelTaskAsync/MaterializeUnsatisfiedRevisionAsync/AcquireAsync、RaceEntryRepairEndpointExtensions、RaceWriteCoordinator、RaceWriteEndpointFilter、CollectionScheduleService。前turnの本番GET/preview時刻・ID・fingerprintは前記設計に保持する。今回「現在も同じ状態」と再観測なしに断言しない。

## 対象と非対象

- 最初の照合対象は2026-09-26中山5R・中山7R・阪神11R。同じ旧Card取得batchに含まれるRace、および今回開催の旧revision/owner欠損/identity拒否の候補は**read-only棚卸し**に含める。候補一覧の確定まで全件補正・全件再要求はしない。
- 各Raceを「公式Card照合可能・独立参照なし」「結果/予想/オッズ/不明参照あり」「公式Card不足・馬集合不一致」に分ける。第1分類だけを補正候補にする。第2・第3分類は未解決のまま明示し、保留する場合も対象一覧の承認を得る。
- 過去の原障害（2026-04-04中山10R）は馬番補正対象と混同せず、修正版による要求の終端確認を継続する。
- 既存予想・結果・オッズの別馬への付替え、failure削除、リソース全体をUnavailableにする抑止、手動SQL更新、無条件再試行は対象外。馬主は公式Cardのみ。木曜の未確定は引き続き正常待機。
- UI新画面は作らず、認証付き管理APIと読取可能な状態/監査結果で運用する。

## 提案する状態と実行契約

`保留要求 → 実行中処理の排出待ち → 静止確認 → preview → 別途承認 → 補正/投影検証 → 明示解除・新版要求 → 収集成功`

異常・中断時は保留を維持する。保留はpipeline pauseと独立し、手動で全体を再開しても保留対象は実行されない。

### 1. 永続的な対象保留

- Collection DBにcanonical RaceId単位のrepair holdを追加する。hold ID、単調増加する世代、理由、作成日時、対応する補正operation ID、状態、解除日時、監査履歴を保存。自動期限切れ/自動解除はしない。DB再起動・別API processでも同じ保留を読む。
- canonical resource、domainRaceId属性、旧aliasを照合し、同じRaceを書き換える収集定義を覆う。race-detailだけを止めて別定義の書込みを通す穴は作らない。矛盾するalias/未知definitionは補正不可として返す。Horse等の無関係な収集は止めない。
- 既存Readyは消さず、ActiveTasksと要求/attemptの履歴を保持する。保留中の要求は記録してRequiredRevisionを単調増加させるが、新task/dispatchを生成しない。予定時刻の到来・手動request・bulk・revision更新・cancel/terminalの後続生成・legacy移行も同じgateを通す。
- outboxの選択・予約・送信再試行・Acquire/AcquireNext・既存envelopeの再送・execution開始で保留を検査する。既にqueueへ送った通知は削除せず、workerの取得時に世代/保留で拒否する。複数Raceのbatchでは保留対象だけを除外し、他Raceの通知や要求を失わない。
- 実装入口の必須棚卸しは`RequestCoreAsync`、独立実装の`ExecuteBulkRequestAsync`、`MaterializeUnsatisfiedRevisionAsync`、`MergeLegacyRaceDetailsAsync`、lease expiryのoutbox再作成、`GetPendingDispatchesAsync`→`TryReserveDispatchesWithinCapacityAsync`、旧`TryReserveDispatchesAsync`、`AcquireNextExecutionAsync`のStartPending replay、`BuildExecutionEnvelopeAsync`、`StartExecutionAsync`、最終`AcquireAsync`、domain保存filter。一覧取得後にholdされる競合を予約内再検査で防ぎ、混在envelopeから外した無関係taskは再配信可能に保つ。旧APIにproduction callerがない場合も安全化または明示廃止し、未分類の迂回路を残さない。
- 保留確定時点でRunningがあれば即「補正可能」としない。終端化/lease整理まで排出待ちを返す。ネットワーク中の収集は止められないが、保留後のdomain保存はRace共通lock内で拒否する。拒否は構造化された運用保留としてattemptを記録し、新たな不明障害・自動全体停止に変換しない。既存のidentity不一致等の安全停止は維持。

### 2. 静止の証明と補正

- hold作成・静止確認・補正・解除と通常writerは既存Race/関連主体lockを共有する。取得順はキーを整列したRace/主体lock→短いCollection DB transaction。DB transactionを持ったままRace lockを待たず、外部HTTPをtransaction内で実行しない。
- Collectionの取得/予約はSQLite transaction内で保留なしを確認して更新し、別processのhold開始と直列化する。process内Semaphoreだけを安全根拠にしない。先に取得済みならhold側でRunningとして検出する。
- 静止証明はhold ID/世代/対象Race/実行可能task0/Running0/未解決lease0を束縛する。補正APIはこれを検証する。単に「Running以外を無視する」変更ではない。**対応する保留によって取得・保存不能と証明したReadyだけ**をActiveCollection blockerから分類し直す。無保留・別世代・別Race・未知aliasは従来どおり拒否する。
- hold中の対象Raceへの通常domain mutationと予想生成を拒否する。調査用GETは許可し保留理由を表示。共有主体については既存lockとpreview/apply時の参照再走査を維持し、他Raceを一律に保留しない。
- RaceOddsも同一Raceへ正規化して保留対象にする。現行odds-snapshotsは数値馬番を保存し、予想作成と異なりassignment fingerprintを要求していない。したがってhold中だけでなく**解除後に到着する古いオッズ**も拒否する。収集のtask lease/hold世代を取得時点で束縛し、馬番依存の入力には取得開始時の割当fingerprintを伝播、保存時に両方を検査する。保存直前に現在fingerprintを後付けして旧payloadを正当化しない。hold履歴のあるRaceへの世代/fingerprintなしの旧client入力も拒否し、新clientで再取得する。手動の数値入力も期待割当版を必須とし、既存結果やオッズを自動再帰属しない。
- 保留状態と世代をpreview fingerprint/apply条件へ追加する。既存の全頭公式identity、version、独立参照、単一event、projection検証は維持。preview以降の差分は再preview/再承認。補正後もholdは勝手に解除しない。

### 3. 中断・backup・解除

- Event DBとCollection DBを横断する単一transactionは前提にしない。holdを先にdurable commitし、operation IDで補正event/既存repair barrierへ結び付ける。途中停止は保留を残し、再送でeventを二重追加せず投影を検証する。DB/sidecar/hold世代の不一致はfail-closed。
- 適用直前に対象Race/主体をlockし、SQLiteの整合したbackup方式でEvent DB、Collection DB、対象repair sidecar、manifest/hashと版/hold世代を保存して識別可能な復旧packageにする。backup/hash/読出し検査失敗ならapply不可。稼働中のdbファイル単純コピーはしない。
- このpackageは対象の整合検証用であり、他Raceが進行する複数DB全体の同一時点を保証しない。全DB一括restoreで他Raceの後続データを失う復旧は自動実行しない。隔離環境で対象の復元検証を行い、本番restoreは影響一覧と別承認を要する。旧binaryへの単純rollbackも不可。
- 解除は補正eventと全投影の一致を再確認し、同じRace lock内でCollection DBの解除と後続要求を同一transactionにする。旧Readyは履歴付きの取消し/置換とし、既存failureは未解決のまま新taskとの関係を保存する。高revisionを低下させず、`max(4, 登録済みrevision, RequiredRevision)`のrace-detail要求を一意に実体化する。不要な新revision5は作らない。
- 他の収集定義の保留要求も失わず、旧envelope/leaseでは書けない新世代で必要分を再実体化する。解除の再送は同じ結果を返す。失敗通知の解決は収集の実成功時のみ。**保留解除とpipeline resumeは別操作**。
- 補正を取りやめる場合も自動解除しない。現状の保存整合・参照を再previewし、明示された中止/解除承認だけで待機要求を戻す。不整合や補正途中なら解除拒否。

### 4. 管理契約（新設予定）

- 既存entry-repair配下にhold取得/状態GET/解除を追加する。全mutationは既存APIキー認証、操作ID、期待世代が必須。
- Collector/ApiClientと対応DTOを同時に更新し、数値馬番を使う保存の入力取得時fingerprint/lease世代を実HTTPで転送する。保留履歴のある対象に旧clientの書込み互換を残すことはしない。無関係Raceの既存契約は維持する。APIとworkerの対応版を配備・確認してから最初のholdを作成する。
- 状態には保留理由、静止可否、Ready/Running/lease件数、対象alias/definition、保留中の最大要求revision、補正operation/fingerprint、解除条件、阻害理由を返す。secretやlease tokenは表示しない。
- previewは常に読取のみ。holdの不在・競合・未排出、旧世代、未知参照は構造化された409相当の拒否。hold解除不能でも管理GETと調査は可能にする。

## 本番実行順（承認後）

1. ローカル/CIを通して新APIを配備。既存の配備時短時間停止許容のみ利用し、pipeline状態は変更しない。
2. 最新のpipeline/Running/対象/公式Cardをread-onlyで棚卸し。中山7Rも全頭sourceと参照を検査する。旧previewを使い回さず、対象・保留対象・補正不可理由・旧新差分を提示する。
3. **対象一覧への別承認後**に対象holdを設定、静止確認、backupを保存、authoritative previewを再取得。差分や対象に変化があればapplyせず再提示する。
4. **具体的なpreviewへの別承認後**、1Raceずつ補正・raw/全関連履歴を検証する。中山5R owner14/14、阪神11R16/16とG3、中山7Rはその時点で検証済みの全頭数を基準にする。
5. 承認対象を新版要求付きで解除。未補正・参照あり対象は保留維持。再開前に他の既知停止原因の扱いを一覧化し、**別途承認された範囲で**pipelineを再開する。
6. 元中山10Rを含む対象要求の終端成功、raw/履歴整合、無関係Raceの進行、10分以上再停止なしを確認。隔離した未解決対象が残れば「部分復旧」であり全課題解消とは呼ばない。

## Concern and agreement ledger

| ID | Evidence / impact | Disposition・代替・残存risk | AC/task | Agent position | User disposition | State |
| --- | --- | --- | --- | --- | --- | --- |
| C211 | Ready再生成により従来previewが拒否 | 永続holdで全生成/取得境界を覆う。cancel連打/Runningだけ除外は不採用。未知入口は拒否して配備gateで検出 | AC211/H1 | Agree | 承認待ち | Resolved in design |
| C212 | pauseだけでは取得済worker/API writerを止めず、数値オッズの古い入力が解除後に来得る | Race lock＋DB原子判定＋世代/取得時fingerprint、Running排出後のみ補正。保留履歴対象の旧clientは拒否し対応版で再取得。旧batch/別process/遅延odds反例を必須化 | AC212/H1–2 | Agree | 承認待ち | Resolved in design |
| C213 | holdとeventは別DB、失敗時に自動解除すると破損を露出 | hold先行、idempotency、整合不明は保留維持。未検証restore/旧binary rollback禁止。保留の手動管理負担は状態GETで可視化 | AC213/H2 | Agree | 承認待ち | Resolved in design |
| C214 | 中山7R発見、他Race/公式Cardの可用性は未確認 | read-only候補棚卸し→個別適用承認。全件安全/即時全復旧を約束しない。独立参照/取得元不足は別設計または保留継続 | AC215/H3–4 | Agree | 承認待ち | Resolved in design |
| C215 | 保留解除で旧revision/二重要求、先行resumeで再停止 | 新世代・最新revisionへ一意置換、failure維持、解除はresumeしない。新版成功後に通知解決 | AC214–215/H2–4 | Agree | 承認待ち | Resolved in design |
| C216 | 既存quiet全suiteでScraping1件失敗の原因未採取 | ローカルTRX付き全suite/反復で特定・処置し元gateを閉じる。緑の再実行だけで未解明failureを削除しない | AC216/H3 | Agree | 承認待ち | Resolved in design |

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC211 | 対象hold中はReady/新要求を保持して実行せず、scheduler/後続revision/再送で復活しない。他Raceは進む | H1,H3 | 実SQLite・2 process、scheduler複数tick、bulk/cancel/terminal/alias/混在batch反例 | Not started |
| AC212 | Running/旧lease/旧世代/未知alias/独立参照があれば補正不可。静止した同一holdのみ既存単一eventで補正可能。解除後の旧オッズ/古い入力も保存不可 | H1,H2,H3 | 実Kestrel＋別process、hold開始/取得/保存の競合、無保留Ready拒否、参照追加拒否、旧client/遅延odds/取得時fingerprint実転送 | Not started |
| AC213 | 中断/再起動/二重操作/backup復元差分で誤解除や二重eventなし。backup検査失敗ならapplyなし | H2,H3 | event前後・投影途中・解除transaction前後のfault injection、隔離復元検証 | Not started |
| AC214 | 解除は最新revision>=4を1回だけ要求し旧task履歴/failureを保持、古いmessageは副作用なし。pipelineは自動再開しない | H2,H3 | release再送/並行新要求/高revision/手動resume/旧envelope試験 | Not started |
| AC215 | 別承認の個別対象で公式全頭/raw owner/番号/grade/関連履歴が一致し、対象と原障害が成功。他収集が進み10分再停止なし。未解決は明示 | H3,H4 | 本番一覧・preview・承認・要求/attempt・raw・観測時刻 | Not started |
| AC216 | 実transport/DB/配信を通るローカルとLinux CIが成功、既存の未解明test失敗を処置。秘密情報/不要な本番mutationなし | H3 | workflow同等format/build/TRX全suite、hold smoke、元failure gate、diff/status | Not started |

## Task plan / review gates

| ID | Task | Owner / tier | Depends on | Write scope | Verification / completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- |
| H0 | 根拠確認・運用設計・独立review | Main/Lead、既存readonly explorer | 利用者の案作成依頼 | docsのみ、explorer read-only | source照合・concern/AC/入口対応、validator成功、独立review反映 | Verified |
| H1 | durable hold/取得・生成境界/世代 | Main/Lead | 設計承認 | CollectionOperations/API writer/Collector/ApiClient/DTO/tests | AC211–212の実DB競合反例と実HTTP輸送 | Proposed |
| H2 | preview/apply/backup/解除の結合 | Main/Lead | H1契約 | repair API/coordinator/tests | AC212–214の再送・中断・復元 | Proposed |
| H3 | 統合検証・棚卸し・配備・文書 | Main/Lead | H1–2 | tests/scripts/workflows/docs | AC211–216、Windows/Linux/実HTTP・公式source | Proposed |
| H4 | 別承認後の本番保留/補正/再取得/観測 | Main/Lead | H3、対象・差分・操作承認 | 承認対象だけ | AC215、親AC204/IAC10へ反映 | Proposed |

Routing: Mainは永続化・並行性・本番権限を保持する。独立して分離できる入口棚卸しを既存explorerへread-only委譲し、設計判断・writeは委譲しない。H1–2は共有transaction/lockで結合が強いため並列codingしない。契約凍結後の局所test切出しは別途判断する。Audit: coding委譲なし。reviewer requested gpt-6-sol（既存agent）、observed/usage unavailable、費用推定なし。

- Design/task-split: Main。補正済コードを作り直さず不足する静止境界へ限定。保留/解除/再開の権限を分離し、検証が通るまで本番操作しない。
- Pre-implementation: 未実施、設計承認後にH1をRunnable、H2–4をDependentへ分類する。
- Checkpoint/Final implementation review: 未実施。H0完成は実装/復旧完成を意味しない。

## Documentation updates / verification

- `docs/26-collection-platform-design.md`: 現行pause/Readyの限界と未実装hold案への正本リンク。既存機能として記載しない。
- `docs/27-jra-site-collection-contract.md`: 補正本体の配備状況と追加保留案を区別。Card限定の取得契約は変更しない。
- 親README/前記補正設計のC206/C207/RP-T5を同時に更新し、解除ログアクセス待ちを取り消す。
- 今回はdocsのみ。実装test/buildや本番APIは実行していない。親/前設計/本書のDDD validator issues=0、agent audit valid、git diff --check成功。CodeGraphはsource未変更のためsync対象外。
- 独立reviewは入口の重複実装、StartPending replay/混在envelope、遅延oddsを指摘。Mainはodds endpoint/filterを独立に照合し、取得時fingerprint/世代と旧client拒否を反映した。再reviewで設計上の阻害懸念なし。reviewer書込0、モデル/usage観測不能、coding成果なし。AC211–216は実装未検証のまま維持。
- Design final review: Main。C211–216に未決の設計選択や未解決objectionなし。残るのは本案への利用者承認、実装/元test失敗の処置、対象別の本番承認。次操作は承認後のH1事前reviewとテスト計画具体化。前turnからのskill改善1行は今回の設計文書commitには混ぜず意図的に未コミットで保持する。進捗専用PR/pushは行わない。

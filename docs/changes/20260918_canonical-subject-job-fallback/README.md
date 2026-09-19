# 主体ジョブのcanonical ID整合と過去ジョブフォールバック

- Status: Approved
- Owner: Main
- Created: 2026-09-18
- Updated: 2026-09-19

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Verified | bulk DTO/API、全producerのcanonical ID、閉鎖session交換、行別失敗、既存repair preview/execute/dismissを接続した。 |
| Verification | Verified | format、Release build、non-External全体試験とfocused回帰を実行した。既存Playwright時間上限試験2件は全体実行時だけ負荷超過し、単独再実行は成功した。 |
| Deployment/operation | In progress | commit/push、CI/CD、本番previewと新規同型エラーの確認を残す。 |

## Context

2026-09-18、本番の競走馬プロフィールTask `horse-96d988e0-373f-5d6f-a084-83bb8478b7d5` は、JRAの強い識別子を持つ有効な対象であるにもかかわらず、プロフィール保存先APIが同じIDの業務主体を見つけられず404になった。Collectorはこれを一時的なprojection遅延として扱い、成功しない再試行を繰り返した。

調査で、旧個別保存経路は競走馬のJRA識別子を保持していた一方、高速化した一括保存経路の契約から識別子が欠落し、RaceEntry側は名前由来ID、子Task側はJRA識別子由来IDになっていたことを確認した。さらに、騎手・調教師ではproducerと保存経路が異なる名前正規化を使い、馬主は主体登録境界を通らず、別のID生成・別名対応を使っていた。

これは[未完了の収集基盤変更を一括完了する](../20260915_complete-pending-collection-changes/README.md)のUAC2「登録・解決済みcanonical IDだけを使う」を、新しい高速経路の実際の入口から検証できていなかった回帰である。高速化そのものは維持し、各行を共通の一括解決境界へ通す。

## Goals

- Horse/Jockey/Trainer/Ownerの全producerと保存経路が、同じ共通解決境界から返されたcanonical IDを使う。
- 過去ジョブを一律削除・再試行せず、主体種別、強い識別子、RaceEntryとの関係、既存主体・redirectの状態を使って安全に分類・復旧する。
- 真のread-model遅延と、永続的なID不一致を区別し、成功しない無期限再試行を止める。
- 一括保存によるブラウザ回数・HTTP回数削減と既存の優先度/lane規則を維持する。
- 修復前後のTask、Attempt、Request、通知履歴を保持し、何を根拠に収束させたか監査可能にする。
- 一括登録の行別拒否を親Attemptへ残し、部分失敗を未分類例外へ潰さない。
- 共有ブラウザーが閉じた場合はセッションを一度だけ交換し、同じ閉じたセッションで後続Taskを失敗させない。
- `産駒`など関係ラベルやJRAプロフィール対象外の海外馬を、国内競走馬プロフィールTaskとして無期限再試行しない。

## Non-goals

- 表示名だけが同じ主体を全データベース横断で自動統合しない。
- 過去Task、Attempt、通知を削除して見かけ上エラーを消さない。
- Running/lease中のTaskを強制中断して書き換えない。
- JRAが提供しない馬主プロフィールを外部ページから新設しない。
- 一括保存を個別HTTP保存へ戻さない。

## Experience and interaction design

`/settings`の低頻度管理操作に「主体IDマイグレーション」を独立sectionとして追加する。初回表示ではデータを変更せず、`候補を確認`でpreviewを作成する。previewは修復可能、既存Task再利用、対象外、曖昧、証拠不足、Running、衝突の分類件数を先に示し、各対象の主体名、現在ID、canonical候補、根拠、予定操作、除外理由を確認できるようにする。

安全対象だけを選択でき、実行はmanifest token付きの確認Dialogを経由する。Dialogには選択件数、実行内容、履歴を削除しないこと、preview後に変化した対象はskipされることを明示する。実行中は二重送信を防ぎ、完了後は適用、再利用、skip、失敗を対象別理由とともに表示して再previewする。大量候補の詳細調査は既存ジョブ詳細へ遷移し、Dialogへ詰め込まない。

Loading、候補なし、preview失敗、manifest期限切れ、部分成功をそれぞれ区別する。narrow viewportでは分類と操作を縦積みにし、候補表を対象単位のcardへ切り替える。状態を色だけで表現せず、keyboard操作とfocus表示を維持する。

## Navigation and relationships

- 入口: `/settings`の既存「その他設定」。新しいトップレベルナビゲーションは追加しない。
- 調査: 各候補から既存のジョブ詳細へ移動でき、戻った際は再previewで最新状態を確認する。
- 実行: preview結果内のPrimary Actionから確認Dialogを開く。通常の補正や通知の「対応不要」操作とは明確に分ける。

## Mocks

- [設定画面: 主体IDマイグレーション](mocks/settings-subject-migration.md) — desktop/narrowの情報階層、preview、選択、確認、結果表示の契約。

## Fallback classification

共通順序は `Taskの不変metadata → Resource属性 → 起点RaceEntry/レース → 既存canonical主体/redirect → 過去Attempt診断` とする。後段へ進むほど証拠を追加し、表示名だけで候補を確定しない。

| 主体 | 状況 | 自動処置 | 自動処置しない条件 |
| --- | --- | --- | --- |
| Horse | Taskに正規JRA識別子があり、同じRaceEntryが名前由来Horseを参照 | 識別子由来canonical Horseを登録または再利用し、当該RaceEntryを付け替え、旧IDへredirect/ledgerを残してRecovery Taskを作る | 同じ名前でも別JRA識別子、またはRaceEntryとの関係を証明できない |
| Horse | canonical Horseは存在するがTaskだけ旧ID | redirectまたはcanonical Resourceで冪等なRecovery Taskを作り、元履歴を保持する | 複数canonical候補がある |
| Horse | JRA識別子がなく、起点RaceEntryが一意 | RaceEntryに結び付く既存Horseを使う。必要なら名称Discoveryで識別子を取得してから確定する | 名前一致しかない、同名候補、起点不明 |
| Horse | JRAで対象外・引退等が確認できる | 理由付き`NotApplicable`として終端し、再試行しない | 一時障害や構造エラーで対象外を証明できない |
| Jockey / Trainer | 表記差だけで、同じRaceEntryと正規化後名称が一意の既存主体を指す | 共通正規化後のcanonical IDへ収束しRecovery Taskを作る | 同名人物、改名・所属変更だけでは人物同一性を証明できない、複数候補 |
| Jockey / Trainer | canonical主体が未登録だが起点RaceEntryと名称がある | 共通境界で先に登録し、返されたIDだけでTaskを作る | 名称欠落、正規化後空文字、登録失敗 |
| Jockey / Trainer | 提供元一覧から対象外であることを確認 | 理由付き`NotApplicable`として終端する | ページ構造変更や一時アクセス失敗 |
| Owner | RaceEntryの馬主snapshotが既存aliasへ一意に対応 | alias resolverが返す内部canonical Owner IDへ収束する。プロフィール取得Taskは作らない | 名前だけで複数Ownerに一致、alias未確定 |
| Owner | alias未登録だが起点RaceEntryのsnapshotが有効で一意 | Owner解決境界でcanonical主体とaliasを原子的に登録し、参照だけを収束する | 曖昧、一意性根拠なし、名称欠落 |
| 全主体 | metadataが欠落した古いTask | Resource、起点RaceEntry、Attemptの順に証拠を補完しpreviewで分類する | 十分な証拠が復元できなければ要対応のまま保持 |
| 全主体 | TaskがRunning/lease中 | apply対象外として待機し、lease終了後に再previewする | 強制中断・二重実行はしない |
| 全主体 | 既存IDとcanonical IDの両方にdomain dataがある | 参照数、強い識別子、競走履歴をpreviewし、既存のversioned repair条件を満たす場合だけ統合する | 衝突、同名別主体、データ差異を安全に解決できない |

## Technical impact

1. `RaceResultEntryBulkDto`へ競走馬のJRA source identityを追加し、一括保存でも旧個別経路と同じ情報を失わない。
2. RaceEntry保存と子Task生成が同じ一括主体解決結果を共有する。producerは表示名からIDを再計算しない。
3. 共通解決境界は各入力について`Resolved`、`Registered`、`Ambiguous`、`Invalid`、`NotApplicable`を返し、canonical ID、判断根拠、正規化名を含める。一件の失敗で他の安全な行を巻き戻さないが、未解決主体の子Taskは作らず親Taskへ具体的な部分失敗を返す。
4. HorseはJRA識別子を最優先し、Jockey/Trainerは一つのJRA主体名normalizer、Ownerは既存alias resolverを正本とする。`NormalizeKey`や表示名を直接`BuildEntityId`へ渡すproducerを残さない。
5. プロフィール保存先の404は、aggregate/domain主体の存在も確認する。主体が存在してprojectionだけ未反映の場合に限り短い上限付き再試行とし、主体自体がない場合は`SubjectCanonicalIdMismatch`として互換修復候補へ送る。
6. 過去ジョブ修復はpreview/applyを分離する。preview manifestは、元Task/Resource、候補canonical ID、証拠、分類、予定操作、除外理由、衝突を固定する。applyはmanifest一致時のみ、冪等キーを使い、redirect/ledger/Recovery Taskを原子的に作る。設定画面とAPIのどちらから実行しても同じ契約を通る。
7. 元TaskのResource keyは履歴として変更せず、成功可能なcanonical Resourceに新しいRecovery Taskを作る。同じResource/Definitionの既存active taskがあれば既存の高いlane/priorityを採用して再利用する。
8. 一括APIがHTTP 200内に返す行別エラーも親workflowの失敗判定へ反映し、該当主体の子Task生成を抑止する。
9. 一括登録の拒否では、`ItemKey`、status、error code、messageを上限付きでAttemptへ保存する。canonical redirectで解決可能な`ResourceSuppressed`はredirect先へ再解決し、解決不能な行だけを隔離する。
10. Playwrightのpage/context/browser closedは共有sessionを無効化し、新しいsessionで当該Taskを一度だけ再実行する。再発時は一時失敗としてbackoffし、キャンセル・Lambda期限切れとは混同しない。
11. profile参照名は主体名と関係ラベルを分離する。`産駒`を含む表示文字列を馬名として登録せず、JRA国内プロフィール対象外と確認できる海外馬は`NotApplicable`で終端する。

## Decisions

- フォールバックは「別URLを試す」だけでなく、「Taskが参照する主体IDを信頼できる証拠からcanonical IDへ解決し直す」処理とする。
- 名前は候補検索に使えるが、単独では自動統合の根拠にしない。同一RaceEntry、JRA識別子、既存aliasなどの追加根拠を必須とする。
- 過去ジョブの元履歴は保持し、削除ではなく新しいRecovery Taskとredirect/ledgerで復旧する。
- 高速化したbulk境界を維持し、その返値を後続Task作成にも再利用する。
- 今回の本番対象だけを特例化せず、全主体・全生成入口をentry-point matrixで検証する。

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | RaceCard、RaceResult、refresh、profile内参照、手動/Recoveryの各入口で、保存された主体IDと子TaskのResource IDが共通解決結果と一致する。 | T2, T3, T6 | entry-point matrixとtransport/persistence E2E | Not started |
| AC2 | JRA識別子付きHorseはbulk経路でも識別子を失わず、同名別馬を分離し、識別子なしの場合だけ安全なfallbackへ進む。 | T2, T3 | identity有無・同名別identity・再送の回帰試験 | Not started |
| AC3 | Jockey/Trainerは空白、全半角、所属括弧、表記揺れを全経路で同じcanonical規則へ収束し、同名人物や曖昧候補を自動統合しない。 | T2, T3, T6 | 表記差と曖昧性fixtureによるE2E | Not started |
| AC4 | OwnerはRaceEntry snapshotとalias resolverから内部canonical IDへ解決され、外部プロフィールTaskや別体系のIDを作らない。曖昧なaliasは要対応に残る。 | T2, T3, T6 | alias一意・未登録・衝突・欠落テスト | Not started |
| AC5 | 過去ジョブpreviewは主体別に、安全な修復、既存Task再利用、対象外、曖昧、証拠不足、Running、衝突を件数・根拠付きで表示し、データを変更しない。 | T4, T5 | API/UI testsと本番preview manifest | Not started |
| AC6 | applyはmanifest一致時だけ冪等に参照・redirect・ledger・Recovery Taskを作り、元Task/Attempt/Request/通知履歴を保持する。二度目のapplyで結果が増えない。 | T4, T6, T7 | transaction/concurrency/idempotency試験と本番再preview | Not started |
| AC7 | 名前だけで一意性を証明できない対象、metadata欠落、同名候補、ID衝突は誤結合・削除・盲目的再試行されず、具体的理由付きで要対応または`NotApplicable`になる。 | T2, T4, T5 | failure classification tests | Not started |
| AC8 | bulkの行別登録失敗時、成功行は保存できる一方、未解決主体の子Taskは作られず、親Taskに対象と原因が残る。 | T2, T3, T6 | partial failure E2E | Not started |
| AC9 | 404は真のprojection遅延だけを上限付きで再試行し、主体不在・ID不一致は自動修復候補または終端分類となり、同じ失敗を無期限反復しない。 | T3, T4, T6 | aggregate/read-model組合せとretry上限試験 | Not started |
| AC10 | Recovery Taskは既存active taskの高いlane/priorityを保持し、Running attemptを中断せず、Realtime増殖抑止規則を変えない。 | T4, T6 | dispatcher/store統合試験 | Not started |
| AC11 | 一括化によるHTTP/ブラウザ効率を維持し、主体解決がN+1の外部遷移を導入しない。 | T2, T3, T6 | request/session count assertionと性能比較 | Not started |
| AC12 | 配備後、本番previewで対象を確認し、backup・pause/drain・apply・冪等確認・resume後に当該Horseを含む安全対象が成功し、queue/healthに新しい同型エラーがない。 | T7 | deployment run、job detail、health/queue evidence | Not started |
| AC13 | 設定画面で、変更なしのpreview、分類件数、対象ごとの根拠・予定操作・除外理由を確認し、安全対象だけを選択して確認Dialogからmigrationできる。 | T4, T5, T6 | component testとbrowser主要シナリオ | Not started |
| AC14 | 設定画面はLoading、候補なし、失敗、manifest期限切れ、部分成功、実行中を区別し、二重送信を防ぐ。desktop/narrowとkeyboardで重要情報・操作を失わない。 | T5, T6 | bUnit、browser desktop/narrow、keyboard確認 | Not started |
| AC15 | race subject bulkの行別拒否は対象ItemKeyとerror codeを親Attemptで確認でき、canonical redirectで解決可能な抑止済みResourceは正本へ収束する。解決不能な一行で正常行や収集全体を巻き戻さない。 | T2, T3, T6 | bulk API/handler partial-failure E2E、Attempt persistence test | Not started |
| AC16 | 共有Playwright page/context/browserが閉じた場合、新規sessionで当該Taskを一度だけ再実行し、後続Taskは閉じたsessionを再利用しない。再発はbackoff付き一時失敗、cancellation/timeoutは従来どおり伝播する。 | T3, T6 | session-scope/handler tests | Not started |
| AC17 | `産駒`等の関係ラベルをHorse名としてTask化せず、JRA国内profile対象外と証明できる海外参照は`NotApplicable`となる。曖昧な国内馬は自動統合されず修復previewへ残る。 | T3, T4, T6 | parsing/handler/repair classification tests | Not started |

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | 現行入口、ID生成、過去ジョブ状態を主体別matrixとして固定する（AC1–AC17）。 | Main | High capability | - | 本記録、canonical docs、UI mock | CodeGraph、production read-only evidence、UI design review | entry-point/fallback matrixと承認済み画面契約 | Verified |
| T2 | 共通主体解決契約とbulk DTO/API/persistenceを実装する（AC1–AC4, AC8, AC11, AC15）。 | Main | High capability | T1 | contracts、API、domain/persistence、tests | API/domain tests | 行別canonical結果 | Verified |
| T3 | 全producer、保存workflow、profile handler、session交換を共通結果へ接続する（AC1–AC4, AC8, AC9, AC11, AC15–AC17）。 | Main | High capability | T2 | Collector、scraping integration、tests | Collector transport/session E2E | canonical ID一致、閉鎖sessionの一回交換 | Verified |
| T4 | 過去ジョブpreview/apply、redirect/ledger、Recovery生成と対象外分類を実装する（AC5–AC7, AC9, AC10, AC17）。 | Main | High capability | T2 | store、repair service/API、tests | transaction/concurrency tests | notification/revision gate付き冪等apply | Verified |
| T5 | Settingsに独立したmigration sectionを実装し、preview、分類、選択、確認、apply結果を表示する（AC5, AC7, AC13, AC14）。 | Main | High capability | T4 | Blazor UI、component/browser tests | UI tests | 対応不要候補を含む安全な管理画面 | Verified |
| T6 | 主体別・入口別・失敗/再送/session交換/性能の回帰試験と旧UAC2の再検証を行う（AC1–AC11, AC13–AC17）。 | Main | High capability | T3, T4, T5 | tests、記録 | format/build/non-External tests、CodeGraph | 全体試験とfocused回帰 | Verified |
| T7 | commit/push、CI/CD、本番preview/apply・監視・文書同期を行う（AC6, AC12）。 | Main | High capability | T6 | deployment/production、docs | CI/CD、production evidence | 復旧と無再発確認 | In progress |

共有契約・migration・本番データ整合を跨ぐため、主担当が直列に保持する。承認後のテスト追加等を分離できる場合だけ、非重複write scopeを再確認して委譲する。

## Review gates

- **Design and task-split review** — Reviewer: Main。Inputs: 本番Task詳細、CodeGraph、bulk/個別保存、主体producer、Owner alias、既存Settings page、UI skills、既存change records。Decision: 単一Horse特例では不足し、主体別の証拠強度と過去状態を網羅するpreview/applyが必要。設定画面は既存の低頻度管理操作を再利用し、preview結果はpage、短い最終確認だけDialogとする。AC1–AC14はT1–T7へ追跡済み。共有契約とデータ整合のため現時点ではMainが保持する。Follow-up: ユーザー承認後、全Taskを再分類してT2から開始する。
- **Pre-implementation review** — Reviewer: Main。Inputs: 2026-09-19本番のrace bulk generic failure、Horse `SubjectNotIdentified` 20件、Trainer `TargetClosedException`、現行repair UI/API、承認済みAC1–AC17。Decision: ユーザーの実装指示を承認としてStatusを`Approved`へ移行した。共有contract・migration・本番データ整合はwrite scopeが重なるためMainが直列実装する。T2のみ`Runnable`、T3–T7は依存解消まで`Dependent`。外部変更はT6完了まで行わない。Escalation: schema/API契約または自動統合根拠を変更する場合だけ再承認へ戻す。
- **Checkpoint review — 2026-09-19, reviewer: Main.** Horse source identityをbulk DTOから保存まで保持し、Jockey/Trainer/Ownerを含む子Taskと個別upsertを共通表示名正規化 + `NormalizeKey`へ統一した。セルフレビューでHorse fallbackを一時的に表示名そのままへ変え、18頭時の集合照会が3回から57回へ退行する誤りを検出したため、従来の名称fallbackへ戻した。性能回帰試験は3集合照会・1transactionで成功した。
- **Checkpoint review — 2026-09-19, reviewer: Main.** 閉鎖browser sessionは同一Task内で一度だけ破棄・再生成する。bulkの部分拒否は成功済みRace保存を維持し、ItemKey/error codeを持つisolated failureへ変換した。過去エラーは既存のnotification/revision gate付きrepair APIとSettingsを再利用し、不正URL・誤生成参照・名前/identity不一致を盲目的に再試行しない分類へ強化した。
- **Checkpoint review** — 各契約、producer/repair、UI/本番操作のcheckpointで記録する。
- **Final review** — 全ACと旧UAC2を実際の入口から再追跡し、未完了・未分類のlegacy ID生成経路がない場合だけImplementedとする。

## Documentation updates

- `docs/22-collector-design.md`: 全主体のcanonical IDと過去ジョブfallbackを本提案へ接続し、表示名だけの自動統合禁止を明記する。
- `docs/26-collection-platform-design.md`: 新規producerと互換修復に共通する不変条件を追加する。
- `docs/changes/20260915_complete-pending-collection-changes/README.md`: UAC2の実装後回帰と、本記録で再検証する事実を履歴追記する。
- `docs/changes/20260918_canonical-subject-job-fallback/mocks/settings-subject-migration.md`: 設定画面のpreview、分類、選択、確認、結果表示、responsive/accessibility契約を定義する。

## Verification record

- 2026-09-18: 本番Task詳細から、対象Horseが有効なJRA source identityを持つ一方、4回ともプロフィール保存404で`SubjectProjectionNotReady`になったことをread-onlyで確認した。
- 2026-09-18: CodeGraphと現行ソースで、bulk DTOがHorse source identityを保持しないこと、RaceEntry保存が名前由来ID、子Taskがidentity由来IDを作ることを確認した。
- 2026-09-18: Jockey/Trainerのproducer、bulk保存、個別upsertが異なる正規化入力でIDを生成し得ることを確認した。
- 2026-09-18: Ownerの子Task IDとOwner list/detailのcanonical ID生成方式が異なり、Raceの主体一括登録がOwnerを対象にしていないことを確認した。
- 2026-09-18: 本調査ではプロダクションコード、本番データ、Task状態を変更していない。
- 2026-09-19: `dotnet format HorseRacingPrediction.sln --verify-no-changes --no-restore` とRelease buildが成功した。
- 2026-09-19: non-External全体試験で全機能試験が成功した。既存Playwright時間上限試験2件は全体実行時のみホスト負荷で失敗し、各単独再実行は成功した。変更関連focused試験35件とAPI性能/identity試験3件も成功した。
- 2026-09-19: `codegraph sync .` と変更入口の再照会が成功し、bulk requestの17 caller、session wrapperのhandler/test経路を再確認した。

## Deviations and follow-up

- 既存UAC2は当時の検証記録を履歴として保持し、本番で判明した回帰を本記録で再開する。過去記録を成功扱いのまま根拠にせず、最適化後の実入口で再検証する。
- 現在のHorse Taskは削除対象ではなく、canonical ID不一致の安全な修復候補である。承認・実装・preview前に手動削除や一括再試行は行わない。
- previewの固定manifestを新規テーブルとして増設せず、既存のfailure notification ID、active状態、required revision、repair ledger/redirectを実行直前に再検証する方式を採用した。UI上の観測可能な競合防止と冪等性は同等で、既存履歴と高速経路を維持できる。

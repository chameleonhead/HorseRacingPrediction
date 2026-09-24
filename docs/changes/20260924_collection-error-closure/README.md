# 収集エラー解消を優先する統合対応方針

- Status: Approved
- Change record schema: 2
- Owner: Main
- Created: 2026-09-24
- Updated: 2026-09-24

Approval: 2026-09-24、利用者が「改修を進めてください。対処を迷うものがあれば聞いてください」と明示承認。AC1-AC7の改修を進める。新規のデータ補正・破壊的操作・本番mutationの個別安全gateは維持する。

## Implementation contract

本recordは承認済みの実装契約である。優先順位だけでなく、[実装・統合・復旧の実行仕様](implementation-plan.md)を承認対象の一部とする。同仕様には実ファイル・symbol、条件別動作、既存patchとの統合、テスト名/コマンド、本番canary、停止/rollback条件、保存成果物を記載した。

実装の入口はPR #64/#65とlocal metadata修正を欠落なく統合・検証することであり、既にある修正を作り直すことではない。ただし今回の変更はそれだけで終了せず、対象の収集エラーについて原因確定、必要な設計、実装、テスト、必要な配備・安全な復旧、独立した本番確認までを含む。原因未確認のOwner補正や停止policyは内容を捏造せず、今回の未完了taskとして証拠・設計gateを通す。文書検証の成功はコード・本番の成功ではない。

### 今回の変更の完了境界（2026-09-24の追加指示）

- 初回baselineで固定する対象障害と、それを直すうえで必要な収集経路の欠陥を、改修・回帰検証・必要な運用確認まで閉鎖する。調査完了、別task起票、PR merge、部分復旧では全体完了にしない。
- Owner/profileを含む未知原因は調査だけで外部フォローアップへ除外しない。原因別専用recordは設計と排他所有の単位であり、本recordのAC7から追跡する子作業である。
- 設計変更・新規データ補正・本番操作に追加承認が必要な場合は、そのtaskを未完了で残して具体的内容を提示する。許可不足を「対象外」に変更して完了扱いにしない。独立した承認済み修正は先行できる。
- 取得不能な公式情報は、根拠あるUnavailableと再試行終了を検証する。欠損を推測してエラーゼロにしない。無関係な将来機能・全履歴再構築へ範囲を拡大しない。
- 本文の安全制約・操作承認は維持する。「完了までを今回の変更」とする指示は成果範囲の指定であり、未確定の破壊的操作の包括承認ではない。

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | In progress | 既存修正13966c9c配備済み。再開後の日別番組RaceList誤分類を局所修正、検証・配備中。Owner/profile同定の残課題は未閉鎖。 |
| Verification | In progress | 基準全体1198 passed・既存1skip、Collector318件成功。新規分類修正は再現red→focused72、Scraping277、実サイト同一session1件成功。全体gate/review続行。 |
| Deployment/operation | In progress | 利用者がMain判断のresumeを許可。21:56解除→race-detail1件成功→21:58後続discovery解析失敗で再停止。修正配備後に再開・連続観測。Recoveryは未実行、安定稼働は未検証。 |

### 再停止closure checkpoint

T2f/T2e/T3a・AC2/3/4/7: 日別番組の誤分類は既存bugとして承認範囲内で修正する。Main排他write、T1-A1が経路調査、T2e-A1が独立read-only reviewを担当。詳細・反例・検証は[operations](evidence/operations.md)を参照。通常の解析失敗は安全停止を維持し、基点recordは変更しない。旧記述のresume承認待ちは今回の明示指示で解消、未知のデータ補正・通知Recoveryの権限と混同しない。

## Context

利用者は2026-09-24、監視中断はトークン切れによるもので一時的に解消したと説明し、監視改善より収集処理がエラーなく進むことを優先した。本件では監視停止を新規の恒久障害と断定しない。

同日18:20 JSTの既存runnerの読み取り結果は29 findings / 27 actionable。UnexpectedPipelinePause 1、StalledActiveTask 19、ActionableFailureGroup 7が要対応。findingは原因数でも失敗task数でもなく、現在の停止の直接原因は未確定。同一fingerprintを過去のTargetClosedExceptionと同一原因と断定しない。

## Reviewed changes and decisions

| 変更記録 | 確認した内容 | 今回の扱い |
| --- | --- | --- |
| `20260921_jra-rescheduled-meeting-recovery` | 結果リンク要素identity、Card/Result独立、振替開催、wakeのActiveElsewhere隔離。PR #65はremote mainへmerge済み。記録の本番移行・確認は未完了。 | 既存修正を採用し、実配備revisionと正常/過去/振替の実経路を検証。全JRA移行の完了を通常収集復旧の前提にしない。 |
| `20260920_dispatch-order-monitoring-fix` | PR #64はmerge済み。記録は配備後確認待ち。 | 優先度契約は維持し、新しい順序制御を追加しない。 |
| [再取得metadata継承](../20260920_collection-recovery-metadata-inheritance/README.md) | ManualRefresh/Recoveryが最新非空task、次にResource属性を継承し、明示補正をキー単位上書き。local実装完了、本番操作は除外。remote mainの記録一覧に同名recordなし。 | ローカル・remote・配備物の実コード差分を確認。文書の不在だけでコード未配備とは断定しない。必要なら既存patchを統合。 |
| [主体ID移行](../20260920_subject-profile-id-migration/README.md) | 後段のAC8-AC11はAPI preflightと既存repair workflowを採用し、旧migration applyを廃止。 | 古い前半のapply説明を実行手順に使わない。最新のpreflight/repair契約で入力と保存先を検証。 |
| [名称補正](../20260920_subject-name-normalization-tool/README.md) | Horse/Jockey/Trainer用。Owner、ID変更、task作成は対象外。 | 全件名称補正を復旧策にしない。必要対象だけpreviewし、Ownerには流用しない。 |
| [処理中件数](../20260920_collection-dispatch-compatibility-recovery/README.md) | 実際の承認範囲はlease有効期限による件数表示修正のみ。Lambda/SQSエラー修正は明示除外。 | ファイル名から互換性エラー修正済みと判断しない。 |
| [構造障害隔離](../20260918_isolate-structural-collection-failures/README.md) / [TargetClosed](../20260919_collection-target-closed-pause-recovery/README.md) | 既知の対象単位隔離と広域停止の設計がある。TargetClosed記録はImplementedと未完了のcompletion summaryが矛盾。 | 停止規則を一律緩和せず、現行コード・配備・実行証拠で確認する。文書Statusだけでは解消扱いにしない。 |

## Goals and fixed scope

1. 現在の停止の直接原因とAPI/Collectorの配備revisionを先に確定する。
2. 既存修正の統合・配備漏れを解消し、同じ修正を重複実装しない。
3. 正常なrace discovery、現在の出馬表、完了レース結果の進行を第一優先とする。過去結果、主体profile、Ownerを次に原因別で閉鎖する。
4. 既知の局所障害やlease競合が無関係な後続処理を落とさない。未知の整合性障害・広域障害の安全停止は維持する。
5. 修正revision適用後、根拠のある対象だけを既存の安全な回復手順で復旧する。障害履歴と旧taskを保持する。
6. 対象障害を分類・起票しただけで終わらず、必要な改修と受け入れ検証まで本変更内で追跡する。

## Non-goals and safety boundaries

- 監視の新機能、通知経路追加、トークン切れ対策、UI改修、処理能力増強の先行実装。
- 全失敗の無条件retry、履歴削除、例外の握り潰し、同名だけの主体統合、旧ID書換え。
- 本書による既存の全JRA移行ACの削除・完了扱い。元recordの未完了移行は残し、通常収集復旧と分離して管理する。
- `20260919_collection-monitor-root-cause-triage/README.md` は参照専用。作成・編集・移動・削除・コミット対象化しない。
- GitHub Actions起動、Issue作成、監視タスクからの本番mutationは行わない。復旧操作は明示許可と既存専用recordの安全条件を満たす解消タスクに限定する。
- 今回の文書確定依頼を、新しいデータ移行や本番操作の包括承認とは扱わない。

## Delivery plan

1. 本番停止理由・代表attempt・配備revisionを読み取り、既存修正との対応を作る。APIとCollectorの混在version、dispatch契約、期限切れleaseを確認する。
2. 既存patchの不足を統合し、通常/過去結果、Card失敗後Result、lease競合、再取得metadataを回帰試験する。未証明の新原因は反例で確定してから専用設計を追加する。
3. 許可された修正版の配備と少数対象の回復を行う。再開条件は既知原因への対策適用、worker健全性、広域障害なし。再停止時は再開を反復せず追加証拠へ戻る。
4. 既存の読み取り専用runnerと実task保存結果で成功・後続進行を確認する。Ownerの生成/検索契約不一致は現時点では仮説であり、代表データの契約照合前に補正方式を決めない。

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | 停止理由、代表失敗、API/Collector配備revision、既存patch有無を証拠で対応付ける。未確認を明示する。 | T1,T1a,T1b | 実行仕様§1/3のrevision/attempt/patch matrix | Connected |
| AC2 | 現在Card、直近/過去Result、Card失敗後Result、ActiveElsewhere後の後続処理、metadata省略Recoveryが実経路のテストを通る。異なるRace/主体へ保存しない。 | T2,T2a,T2b,T2c,T2d,T2e | 実行仕様§4/5、V1-V6、公式E2E、format/build/regression | Connected |
| AC3 | 許可後の本番で原因別の回復対象が成功し、少なくとも次の独立した処理単位も進行する。通常処理周期の2回以上を観測し、即時再停止・同原因再発がない。 | T3,T3a | 実行仕様§6、task/attempt、保存データ、観測開始終了時刻 | Not started |
| AC4 | 既知局所障害は他対象を停止させず、未知整合性障害と広域障害は安全停止する。retryは有限で履歴を保持し重複実行しない。 | T2,T2b,T2c,T2d,T2e,T3,T3a | 実行仕様S2-S4、V2-V5と停止条件 | Connected |
| AC5 | 今回対象の残存障害群は根拠または不足証拠と専用task/recordへ対応付く。今回新たな曖昧主体のalias/ID自動書換えを追加せず、取得不能は正常成功と混同しない。既存Owner名グループの存在確認を人物同一性の証明としない。 | T1,T1b,T1c,T3,T3a | 実行仕様S5、原因別dispositionと許可対象照合 | Connected |
| AC6 | 対象レースについて期待数と保存数、Card/Result取得時刻を確認する。次の金曜のCardおよびレース後Resultの実測はその時点まで未確認とする。 | T3,T3b | 実行仕様§7、0件unknown・Unavailable別集計 | Not started |
| AC7 | baseline対象の各原因について、必要な修正・回帰試験・配備/復旧・本番結果へ追跡でき、未解決の改修taskがない。原因未確定、承認待ち、子record起票済みを解消と数えない。公式Unavailableは根拠と安全終端の実証を要する。 | T1c,T2,T2f,T2e,T3,T3a,T3c | 原因→子task/record→patch/test→配備→本番終端のclosure matrix | Not started |

## Concern and agreement ledger

| ID | Concern and evidence | Impact | Proposed disposition | AC/task/test | Agent position | User disposition | State |
| --- | --- | --- | --- | --- | --- | --- | --- |
| C1 | merge/local完了と配備は別。local checkoutにも既存変更が多数ある。 | 修正漏れ・目的外変更の配備 | cleanな承認済みrevisionを選びpatch/配備物を照合。現在のdirty checkoutを配備しない。 | AC1/T1 | 推奨。代替の重複再実装は却下。残riskは配備証拠不足。 | 2026-09-24設計全体を承認 | Resolved in design |
| C2 | エラーゼロを停止規則撤去や失敗非表示で達成できてしまう。 | データ破損・見かけの復旧 | 正常対象の成功と独立後続進行を判定し、安全停止・隔離を維持。 | AC3-5/T2,T3 | 推奨。一律非停止は却下。未知障害は停止が残る。 | 2026-09-24設計全体を承認 | Resolved in design |
| C3 | ID/開催移行は整合性・不可逆性riskがある。 | 誤結合・mixed state | preview、対象選択、backup/drain、再検証、冪等性、rollbackを既存recordに従い別操作gate化。 | AC4,5/T3 | 推奨。全件補正先行は却下。曖昧候補は人の判断待ち。 | 2026-09-24設計全体を承認 | Resolved in design |
| C4 | 従来recordの完了表現と残作業が不整合。 | 未配備修正を完了誤認 | 原因修正の実証と同じ作業cycleで元recordの状態も更新。ただし参照専用基点は除外。 | AC1,3/T1,T3 | 推奨。今回既存recordの歴史は書き換えない。 | 2026-09-24設計全体を承認 | Resolved in design |
| C5 | local HEADとremote mainが分岐し、metadataはlocal、PR #65はremoteにある。PR #65はschemaを含む。 | 一方を採用して他方の修正を失う、API/Collector混在 | T1aでpatch単位の包含を確認し、cleanな統合revisionでV1-V6を再検証。schema互換性のない片側先行配備は禁止。 | AC1,2,4/T1a,T2e | 推奨。全ファイル上書き統合は却下。配備経路未確定は本番gateに残す。 | 2026-09-24設計全体を承認 | Resolved in design |
| C6 | 現在停止原因とOwner mismatchは未証明。週末期限の実測は将来観測を要する。 | 誤った原因修正・完了宣言 | 既知統合sliceを具体化し、未知の補正は証拠/再設計gateへ分離。将来実測はDependent、独立した通常復旧は先行可。 | AC1,5,6/T1b,T3b | 推奨。未知原因まで包括承認を求めない。 | 2026-09-24設計全体を承認 | Resolved in design |
| C7 | BuildOwnersAsyncは正規化名によるgroupを作る。現行lookupだけでは同名別人物を区別できない。 | 存在確認を同一性証明と誤認し誤補正する | 本scopeは新たなalias/ID書換えを追加しない。既存名モデルの限界を明示し、人物同一性の解決が必要ならT1cから別設計gateへ。Owner未解決を通常収集の解消と混同しない。 | AC5/T1c | 推奨。Owner成功を人物同定成功と呼ぶ案は却下。既存モデルのriskは残る。 | 2026-09-24設計全体を承認 | Resolved in design |

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State | Routing | Audit | Result metrics |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | 停止・revision・patch・原因対応確定 | Main（調査補助: explorer） | Lead + Worker | Design approval | read-only | AC1,AC5 matrix、Mainによる原証拠照合 | baseline/operations: 停止原因、日別番組誤分類と残存Owner/profileの証拠境界 | In progress | Lead — 未確定原因・配備契約判断。既存patchの呼出経路探索のみexplorerへ分離 | T1-A1 | unavailable; retries 0; corrections 0; reviews 3 |
| T2 | 既存patch統合と必要な原因修正、回帰 | Main（今回coding委譲なし） | Lead | T1、原因別設計承認 | Mainが永続化・共有契約・統合を所有。workerはdispatch前に専用recordへ列挙した実ファイルのみ | AC2,AC4 tests、独立反例、既存regression | 未取得: attributable diff、test結果、独立反例を提出 | In progress | Lead — persistence/concurrency/integration。既知局所sliceは既実装を検証 | none | unavailable; retries 0; corrections 0; reviews 0; 未実行 |
| T3 | 許可された復旧と本番完了確認 | 原因別解消taskのMain（本番操作owner）、独立review担当 | Lead + Review | T2、明示的operation許可 | 本番writeは指定Main一人。review担当はread-only | AC3-6 read-back、配備/復旧チェックリスト | 未取得: 許可対象・配備revision・観測時刻・保存結果を提出 | Dependent | Lead — 本番権限・データ安全・最終判定。高risk差分は別default agentが独立review | none | unavailable; retries 0; corrections 0; reviews 0; 未実行 |
| T1a | clean統合基準とpatch包含表を確定 | Main + explorer | Lead | Approval | read-only、Mainだけbaseline記録 | 実行仕様§1/3; SHAとsymbol比較 | baseline.md: ff95b224+metadata差分、配備unknownを明記 | Verified | Lead — integration/配備基準判断。経路探索はexplorer | none | unavailable; retries 0; corrections 0; reviews 0 |
| T1b | 最新停止原因・S4に必要な証拠を確定 | 解消task Main | Lead | T1aの基準確認 | read-only、専用record evidence | 実行仕様§3/S4; task/attempt/batch照合 | baseline.md: 現行実サイト再現+公式9/21中山中止証拠 | Verified | Lead — ambiguity/concurrency/security。資格情報は委譲しない | none | unavailable; retries 0; corrections 0; reviews 0 |
| T1c | Owner/profile失敗と同一性証拠の限界を分類 | 解消task Main | Lead | T1aの基準確認 | read-only、専用record evidence | S5; task名/ID/API/alias照合 | baseline.md: OwnerNotRegistered/NoCandidate。入力同定と配備証拠不足 | Externally blocked | Lead — identity/public contract。通常復旧sliceを阻害しない | none | unavailable; retries 0; corrections 0; reviews 0 |
| T2f | 未知/残存原因の設計・必要改修・回帰を閉鎖 | 解消task Main | Lead | 該当原因のT1bまたはT1c、設計/操作gate | decisions/cancelled-meeting-discovery.md記載のmodel/parser/navigator/handler/tests、その他はread-only | 再現反例→修正→実経路回帰。T2eへ統合 | 中止開催sliceの実装/実サイト/回帰/独立review済み。残るprofile/Ownerは証拠/承認待ち | Externally blocked | Lead — identity/public contract/統合判断。短いfixture分割はreview費用過大。親の責任を維持 | none | unavailable; retries 0; corrections 0; reviews 1 |
| T2a | Result選択patch統合と反例閉鎖 | Main（既存patch検証、coding委譲なし） | Lead | T1a、承認済み契約 | 実行仕様S1の2 production filesとV1 testsのみ | V1最小testとScraping非External | 既存PR65保持、Scraping非External266件と外部5件成功（Main検証） | Verified | Lead — 既実装の統合検証のみ、局所coding不要 | none | unavailable; retries 0; corrections 0; reviews 0 |
| T2b | Card/Result・振替の統合整合を確認/修正 | 解消task Main | Lead | T1a,T2a | S2、PR65のdomain/API/schema依存とV2/V5 | V2/V5、identity/write反例 | verification.md: Card/Result実保存・版数反例を閉鎖 | Verified | Lead — public contract/persistence/migration | none | unavailable; retries 0; corrections 0; reviews 0 |
| T2c | metadata継承patchを欠落なく統合 | 解消task Main | Lead | T1a、Store排他owner確保 | S3とV3 tests | V3、Collector非External | verification.md: Collector300件成功、別Resource/Definition混入なし | Verified | Lead — persistence/overlapping writes | none | unavailable; retries 0; corrections 0; reviews 0 |
| T2d | wake競合と既知/未知停止境界を閉鎖 | 解消task Main | Lead | T1a,T1b、T2cのStore編集終了 | S4、V4 tests | V4、Collector非External | PR65 wake隔離保持、Collector300件にV4回帰包含。現在停止の追加契約はT2f | Verified | Lead — concurrency/fencing、安全分類判断 | none | unavailable; retries 0; corrections 0; reviews 0 |
| T2e | 統合CI相当と独立高risk review | Main + default reviewer | Lead + Review | 今回配備sliceのT2a-T2d | read-only | V1-V6、実行仕様§5全gate | verification/operations: 既存1198件と追加closure。日別番組誤分類の独立reviewはblockingなし、Scraping277/実サイト1成功。残存slice未閉鎖 | In progress | Lead — integration/final acceptance。reviewerは独立反例 | T2e-A1 | unavailable; retries 0; corrections 0; reviews 11 |
| T3a | 許可済み配備・限定回復・連続観測 | 解消task Main | Lead | T2e、operation明示許可 | 実行仕様O1-O7の許可対象のみ | 代表最大5件、各原因1件開始、次のtask成功 | 既存CI/CDとMain判断resume許可済み。1回再開後に新原因再停止、修正版配備から継続。Recovery未実行 | In progress | Lead — destructive/production safety | none | unavailable; retries 0; corrections 0; reviews 0 |
| T3b | Card/Resultの件数・時刻で受入判定 | Main | Lead | T3a、必要checkpoint到来 | read-only、evidenceのみ | 実行仕様§7、既存設定値照合 | 未取得: 分母、成功/Unavailable/unknown、取得時刻 | Dependent | Lead — final acceptance/将来観測の誤認防止 | none | unavailable; retries 0; corrections 0; reviews 0 |
| T3c | 全対象原因の改修完了と親子recordを照合 | Main | Lead | 全対象のT2e/T3a、T3b | 本recordとclosure evidenceのみ | AC7 closure matrix、AC1-AC6照合 | 未取得: 全対象Verified、改修積み残し0 | Dependent | Lead — final acceptance/子task移管による偽完了防止 | none | unavailable; retries 0; corrections 0; reviews 0 |

T1/T2/T3はAC群の統合責任、末尾英字taskは実行可能なsliceであり、二重に起票しない。T2e/T3aはT2fも含む独立して配備可能な修正sliceごとに実施し、全原因の修正完了を待つ一括gateにはしない。部分配備の証拠だけでT2/T3全体をVerifiedにはしない。T3cは全対象の閉鎖まで完了しない。

### Agent selection and dispatch contract

- **Main / Lead:** 本スレッドは統合方針・原因間の優先順位を保持し、本番mutationは行わない。原因別Codex解消taskのMainが設計・共有契約・永続化/移行・並行性・統合・最終判定を担当する。監視が委譲したという事実は操作承認にならない。
- **explorer / Worker:** T1で「当該修正がどのcommitにあり、どのentry pointから呼ばれるか」など独立したcodebase質問だけをread-onlyで担当する。コード位置の引用と反例候補を返し、本番資格情報・本番操作・根本原因の最終確定は任せない。既存調査を重複してやり直させない。
- **low_cost_coding_worker / Cost efficient:** T2の設計凍結後、局所的で独立検証可能な実装とテストに使用する。現時点のrole設定は `gpt-5.6-luna` / `medium`。これは本計画時点の候補であり恒久policyではない。API契約変更、移行、lease/transactionの設計、秘密情報、破壊的操作は委譲しない。
- **default / Review:** データ・並行性・本番復旧の高risk変更は、実装者と別の高能力agentへread-only reviewを依頼する。可能なら実装者の結論を渡さず、AC・diff・原証拠・反例から評価する。通常の局所変更はMainがAC群単位で統合reviewし、毎回別review agentを起動しない。
- 起動時に、実際に選択したrole、具体的なrequested model ID/reasoning、agent ID、開始revisionを記録する。Lead/explorer/reviewerは実行時の利用可能設定を確認し、不明なmodel名を推測しない。observed modelとusageは別項目で、取得不能はnullと理由を記録する。
- worker promptにはAC、固定判断、変更禁止判断、read/write実ファイル、依存revision、反例、自由度、最小testとhandoff前regressionの具体的コマンド/期待結果、成果形式、昇格条件を必須にする。実ファイルやテストが未確定の現段階でcoding workerを起動しない。
- workerは他作業と共存しており、他者の変更を戻してはならない。基点recordは全agentに変更禁止、本統合recordはMainだけが所有し、原因別taskは専用recordだけを更新する。worktreeが別でも共通schema・migration snapshot・共有契約は同時編集しない。
- 1回のfocused correction後も検証不合格、scope拡大、契約/安全判断が必要ならMainへ昇格する。承認済み内部修正はMainが自律継続し、外部仕様変更・データ損失risk・認証不足だけ利用者判断へ戻す。
- 委譲codingの起動/完了時は専用recordのtask行と `agent-audits/<attempt-id>.json` を更新する。実行していないagentの監査JSONや成功値を先に作らない。準備・worker・review・修正・再検証の総工数で委譲を評価し、細分化の負担が上回る短い変更はMainへまとめる。

### Revised execution workflow

```text
既存Codex task・registry照合（同一原因は既存taskを継続）
  → T1: Mainが本番証拠確認 / explorerが既存patch経路調査
  → Mainが原因と既存修正との差分を確定
  → 原因別recordの承認範囲確認・write owner/test契約凍結
  → T2: Mainの高risk修正 + Workerの独立した低risk slice
  → AC群で統合検証・必要時に独立review
  → T3: 明示許可・安全条件を満たす解消taskのMainだけが配備/限定復旧
  → 別の読み取り観測で対象成功・後続進行・データ保存を確認
  → 専用recordとCodex task/registryを整合 → Verified
```

1. **重複防止:** 本書T1-T3は作業段階であり、一律に3つの新Codex taskを作る意味ではない。根本原因をdedupe keyとし、既存未完了taskを優先する。作成・追記後はtask ID、専用record、状態、根拠をregistryへ記録してから次へ進む。sub-agent IDと永続Codex task IDを混同しない。
2. **並列上限:** 利用可能4枠のうちMain 1を確保し、実作業は最大3子agent。同じ原因の調査→修正→reviewを依存前に起動しない。原因別Codex taskをまたいでも本番mutationは一人に直列化する。
3. **最短復旧経路:** 現在の停止原因を直すsliceを最初に検証・配備候補化する。無関係なOwnerや全過去移行の完了を待たせない。ただし当該配備に必須のschema/整合性移行は省略しない。現在Card・Resultの成功を優先し、監視拡張は後回しのままとする。
4. **検証失敗:** worker自己申告だけで受け入れず、既存regressionまたはMainの独立反例を追加する。失敗は元のACへ戻して修正・再検証し、task追加で未解決を隠さない。reviewはAC2/4の統合群、AC3/5/6の本番群で行う。
5. **本番gate:** revision一致、許可対象、preview、必要なbackup/drain、rollback条件を確認する。配備可能だけではVerifiedにしない。再停止/誤保存/重複実行なら追加retryを止めて原証拠へ戻る。移行の新規承認が必要ならコード修正と区別して提示する。
6. **待機と再開:** トークン切れや外部待ちに備え、最後の証拠時刻・現在状態・次の具体操作・検証コマンド・未コミット対象を専用recordに残す。再開時は状態を再読して本番操作を重複しない。次の金曜など将来の観測はDependentとして残し、観測前にAC6/全体を完了扱いにしない。
7. **承認境界:** 設計中はread-only探索を許可し、coding agent・新規永続Codex task・本番操作は開始しない。本体はProposedを維持する。承認後もAC1-AC6とC1-C7の安全境界を守る。詳細な実装・操作契約は[実行仕様](implementation-plan.md)に従う。

## Documentation updates

- `docs/11-automation-design.md`: 既存CI/CDの停止/drain/backup/移行失敗時停止維持/再開前failure検査を現在の運用契約として明記。新Workflow追加なし。

- `docs/27-jra-site-collection-contract.md`: 承認済み中止開催の公式証拠と不明時の失敗維持を現在契約へ追記。具体的URL・表示構造・反例は本recordの`evidence/jra-site-observations.md`を参照する。

- 今回は本recordを新規作成し、優先度・既存修正の扱い・承認境界を記録する。既存の実行時仕様は変更しないため、確認した `docs/changes/` の関連設計を置き換える正本文書変更は不要。
- 後続で実行時契約を変更する場合は原因別recordと対応するarchitecture/operational正本文書を同時更新する。
- 元recordの移行・配備の未完了義務は本書で消去しない。
- `implementation-plan.md`: 本変更専用の実装入力、動作、変更責務、試験、運用gateを追加。これは既存runtimeのarchitecture正本を置き換えない。`docs/26-collection-platform-design.md`と既存PR #65の`docs/23-jra-scraping-redesign.md` / `docs/27-jra-site-collection-contract.md`変更を統合時に保持する。既存契約を変える新設計が必要なら、専用recordの承認前に対応する正本文書を更新する。

## Review gates and verification record

- Execution / Pre-implementation review, 2026-09-24: 利用者承認により開始。clean worktree `collection-error-closure`、基準 `ff95b22499312462194a970b038dcc9d959163bd`。T1/T1a/T1b/T1cとT2cをIn progress、T2a/T2bをRunnable、その他は依存未充足のためDependentとして実行する（設計時の下表状態をこの実行frontierで更新）。C1-C7の設計境界を承認内容として保持。本番writeはまだ未承認・未実施。
- MainがStoreとStoreTestsを排他所有し、local commit `e036a6b8` のmetadata差分だけを統合。最新mainのexplicit URL保存・期限切れlease表示を保持。V3最小test→Collector非Externalを実行し、別Resource/Definition、通常登録、既存snapshot不変性を独立確認する。永続化判断と同一Store編集が不可分のためMain保持。
- read-only explorer `/root/collection_code_map`（requested gpt-6-sol / medium、observed/usage未取得）へS1/S2/S4の具体的反例調査を再依頼。書込なし。Mainのmetadata/本番GET診断と並列、production秘密情報は委譲しない。既存実装が合格ならT2aのcoding委譲は省略する。
- WorktreeのCodeGraphは設定directoryのみでindexなしを確認。exploreが明示したfallbackに従いrg/sourceで検証し、未作成indexを勝手に作らない。

- Design/task-split review, Main, 2026-09-24: 今回は既存文書の矛盾と運用権限を含む一つの優先順位判断のためLeadが保持。coding委譲なし。未確定原因の実装sliceを先に作らない。
- Concern review: C1-C4を安全境界で解決する案を提示。利用者の新方針確認前につきProposed。
- Pre-implementation: 未実施。承認後、実コード/配備差分に応じたexclusive write scopeとテストを定義する。
- Checkpoint: remote mainのchange record一覧、PR #64/#65のmerge、local/worktree上の関連記録を確認。文書の実装自己申告は今回の独立検証結果とは扱わない。
- Final review: 本番解消は未確認。本ターンは文書のみ。次はAC1のread-only照合。変更禁止の基点と既存の未コミットファイルは変更しない。
- Workflow review, Main, 2026-09-24: 利用者の追加依頼によりagent role、exclusive ownership、独立検証、1回修正後の昇格、dedupe、再開checkpointを追加。今回の短い単一文書編集は委譲コストが上回るためMainが実行。AC1-AC6の成果要件は維持し、監視拡張や本番権限は追加していない。監査validator初回でevidence欄のplaceholderとmetrics書式の不備を検出し、必要成果物と未実行の表記へ修正した。
- Implementation-readiness review, Main, 2026-09-24: 前版はrouting中心で実装契約として不足していたため、実コード・PR file inventory・test名・CI workflowを調査し実行仕様を追加。CodeGraph exploreで経路を取得し、local `d9710e38` / remote `ff95b224`の差を記録。実装・本番検証は未実施。
- Read-only exploration: `/root/collection_code_map`、requested `gpt-6-sol` / medium、role explorer。metadata/Owner/failure isolationのsymbol・既存反例を調査、書込なし、test未実行。observed model/usageは未取得。Mainが既存契約と照合して計画へ採用、設計・安全・最終判定は委譲していない。coding委譲ではないためcoding監査JSONなし。
- Design/task-split再review: T2aのみ凍結された局所実装を低コストworker候補とし、schema/identity/fencing/Storeは具体的risk理由によりMain所有。共有StoreのT2c/T2dは直列、独立S1とStore作業だけ並列可能。実装開始前に基準revisionと具体的test契約を再確認する。
- 独立read-only reviewで、Owner lookupの人物同一性証拠としての限界と、Owner未解決がwake修正を阻害する依存を指摘。AC5を今回の自動書換え禁止と証拠限界の可視化へ明確化し、停止調査T1bとOwner調査T1cを分離。Ownerの完全同定を本scopeで保証しない。metadataのResource fallbackは同Resource単位であることも明記した。
- 文書詳細化の最終検証: `python scripts/audit_agent_execution.py docs/changes/20260924_collection-error-closure/README.md` はvalid、DDD validatorはissues=0。相対リンクの実在、追加仕様の末尾空白、`git diff --check`を確認。Mainは独立reviewの2指摘を修正後に依存関係とACを再照合した。本番・コード・test実行の完了証拠は未取得のまま維持。今回変更は本ディレクトリのREADMEとimplementation-planのみ、基点文書は変更なし。
- Scope review（追加指示）: 今回の収集変更を改修完了までと明確化し、T2f/AC7/T3cを追加。未知原因を調査または別recordに移しただけでは閉鎖しない。既存の「新しい補正は承認対象外」は、実行権限gateを指すのであって成果範囲からの除外ではない。skill更新は別の狭いrecord `20260924_agent-assignment-in-change-records` で検証し、skill完了と収集改修完了を分ける。

## Execution checkpoint / next action

公開・本番操作の最新gateは [operations](evidence/operations.md)。2026-09-24の追加指示により専用branch/commit/pushを開始。Actions可否・AWS再認証・対象限定本番操作のgateは別管理とする。

最新checkpoint: 利用者は既存CI/CDを許可、新Workflowは禁止。PR66/67の修正は13966c9cとして本番配備成功（既存app-deploy35997116414全job成功）。21:19 JSTの独立GETで既存pause時刻を保持、Running0、29 findings/27 actionable。次は通知dbca6a70-d70b-4ffe-8a6f-bbc90e643e68一件のRecoveryと一度resumeの個別承認後、安全条件再確認→代表終端→独立後続→2周期→金曜/結果鮮度を確認する。Owner/profileの未知補正は未承認のまま。今回未コミット対象は配備結果のREADME/operationsのみで、記録commit/push後に操作承認待ちとする。先行する旧checkpointの「Actions未許可/AWS再認証後のみ配備」は本段で更新する。

現在の正規状態は上記Task planと本節。以下の旧review箇条書きは設計時点の履歴であり、未承認を意味しない。
[baseline](evidence/baseline.md) / [検証](evidence/verification.md) / [追加設計案](decisions/cancelled-meeting-discovery.md) を参照。
2026-09-24「お願いします。JRAのサイトに関する情報も記録をお願いします」によりT2f中止契約を追加承認。実装と実サイト回帰を進める。AWS再認証・個別操作許可まで本番mutationは行わない。
[JRAサイト確認記録](evidence/jra-site-observations.md)へ公式URL、HTML構造、候補誤検出の原因、判定限界を記録。`docs/27-jra-site-collection-contract.md`を現在契約の正本として同時更新する。
Mainは新規model/parser/navigatorとdiscoveryのidentity/失敗境界を所有。純粋fixtureは短く統合判断直後に作成するため分割review費用が上回りLead実施。explorerは公式構造のみread-only、reviewerはAC群の証拠保存/identityを独立確認。後続失敗時の既確認中止証拠欠落をclosure itemとして修正・反例testを追加する。

追加slice checkpoint: 中止判定、混在開催、独立代替日、未知失敗維持、後続失敗時の証拠保持はローカル検証・独立再reviewで閉鎖。既存スキルの実経路/反例gateで捕捉・修正した局所欠陥であり恒久routing変更なし。現在の停止理由はAWS認証期限切れ・本番操作の個別許可、残るprofile/Ownerの同定証拠/補正承認。親recordはApprovedのまま。本番は未検証であり収集エラー解消完了ではない。
次の操作: 利用者の`aws login`後、配備revisionをGETで照合し、実行仕様O1-O7の対象・schema/rollback・限定Recovery/resumeを確定して個別承認を得る。続いて代表task終端・独立後続・2周期・鮮度を確認。残るprofile/Ownerは同定証拠から設計を確定する。未コミットファイルと検証コマンドはevidence/verification.mdに列挙済み。

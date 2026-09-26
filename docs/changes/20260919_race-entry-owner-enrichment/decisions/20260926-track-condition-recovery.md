# 馬場状態の誤認と収集停止への対応設計

- Status: Approved
- Owner: Main
- Updated: 2026-09-26
- Governing record: [馬主取得・出走割当の修正](../README.md)。実装・限定復旧と配備時の短時間API停止は承認済み。以下の設計時点の未承認記述は履歴。

## Incident ledger / 証拠

## Execution checkpoint（承認後）

- 配備前gate: 最新origin統合版で `dotnet test HorseRacingPrediction.sln --configuration Release --no-restore --filter 'TestCategory!=External'` 成功1,242/失敗0/既存skip1。最終編集後のexact `dotnet format HorseRacingPrediction.sln --no-restore --verify-no-changes` 成功。Release build警告0/エラー0、EF pending-modelなし、空DB migration成功、deployment停止保持/collector temp lifecycle script全成功。DDD/audit validator成功。RT1/RT2のコードとローカル検証を完了、RT3配備・限定復旧へ進む（IAC9/10は本番証拠まで未Verified）。
- 統合checkpoint: 最新origin/mainの隔離branch `codex/track-condition-recovery`へparserとidentity gateを統合。元workspaceのユーザー変更は含めない。実HTML/公式障害URLを含むparser72件、API保存関連39件、元workspace全非External testが成功。最新originではrevision固定のfixture3件を更新し再検証中。新規発見の天候全文fallbackと専用欄の不正子要素はHTML反例を追加して解消した。
- revision入口inventory: API登録、CLI登録、日程発見、代替開催発見、Horse履歴、owner migration要求、owner個別要求の7か所を共通 `CollectionDefinitionRevisions.RaceDetail = 3` に統一。Domain seedの1は過去取得済み版、migrationの1は互換性の最低版チェックであり、新規要求の旧版固定ではない。
- 運用手順の具体化: API起動の `RegisterDefinitionAsync` はrevision行も登録するため、その後の `/revisions/preview` / `apply` は「既存revision」で拒否される。既存 `/requests/bulk/preview` → `/requests/bulk` の `SpecificResources`、`ExpectedResources`、固定batch IDを使い、同じ承認対象1件・revision3・Normal/30を要求する。新しいAPI仕様や全件impactは追加しない。対象・安全境界を変えない内部手順の補足である。
- 12:37 JSTの独立GETではpipeline理由は原障害のまま、対象にrevision2のReady recovery task `ddad3668-6b57-4db0-ad2f-51591200366c` が存在（12:15作成、当方操作ではない）。新版要求がこの既存taskと重複しないことを確認してから実行する。
- CodeGraph: 隔離worktreeではsync/indexが未初期化で失敗したため `codegraph init -y` 後 `codegraph sync .` 成功。`ValidateCollectedEntryIdentities` は通常bulk/refresh、`ValidateCollectedEntryAssignments` は両aggregate入口の各2 callersを確認。DB model差分なし・空SQLite migration成功。
- 残作業: 新版入口の最終format/build/test、commit/PR/既存workflow配備、限定要求、resume、本番終端結果と10分観測。現時点で本番mutationなし。
- 独立運用reviewのclosure: bulkは既存Ready revision2をその場で書き換えず、終端後にrevision3を1件だけmaterializeする。新規 `RegisteredRevisionBulk_QueuesOneFollowUpBehindExistingReadyTask` が同一batch再送・RequiredRevision・旧task維持・新版Normal/30・Result再取得を検証して成功（bulk全4件）。再送receiptの空TaskIdだけを成功証拠にせずGETの要求/状態/履歴で追跡する。
- bulkのSpecificResourcesではmetadataを引き継がないというreview指摘も確認。対象には元からdomainRaceIdがなく、2026-04-04の10Rをdomain検索した結果は0件。通常経路の決定論的ID `race-7f5feb42-1498-5643-a6e0-6ad48d8d42f5` を本番照合対象とする。既存の異なるIDへrefreshする対象ではない。日付はresourceから保持されることを追加testで検証。別対象への一般化や既存Raceのrefresh代替としてbulkを使わない。

- 追加承認: 利用者は「修正版配備時の短時間API停止を許容する」を選択。既存単一APIの入替に必要な短時間停止を本復旧だけの範囲に含める。既に停止中のpipelineを修正版検証前にresumeしない。本文のAPI停止を承認外とする記述はこの追加承認で置き換える。
- 利用者の「お願いします。」でIAC7–10、RC1–4、再開前identity gateと限定復旧運用を承認。以下の未承認記述は設計履歴。
- RT1: Runnable、RT2: Runnable（identity gateを先行、revision統合はRT1後）、RT3: Dependent。Mainがproduction/testの唯一のwrite owner。取得境界判断とデータ整合性の横断修正を分離できないため実装はLead保持。
- 独立read-only調査は既存explorer `entry_reference_inventory`（snapshot境界・test helper）、`revision_reacquisition`（配備/旧実行境界）へ委譲。requested gpt-6-sol、observed model/tokenは取得不能、audit none（coding委譲なし）。設計判断・本番操作はMainのみ。
- Pre-implementation review: RT1はRaceResultPageParserTests＋実snapshotterのHTML回帰、RT2は通常/refreshの馬番・公式identity反例と副作用不変テスト、関連Scraping/API/Collector regressionを実行。未知値の握潰し、直接DB補正、全件retryは禁止。認証不足や新たな停止必須なら運用gateを保留し、独立実装は継続する。
- Routing / Audit / Result metrics: Main=Lead（境界・整合性・統合責任）、read-only explorers=bounded discovery、coding audit none、usage unavailable、retry 0、review pending。
- RT2-testsを分離: owner `/root/identity_guard_tests`、cost-sensitive `gpt-5.6-luna`、write scopeは新規 `CollectedRaceIdentityGuardTests.cs` のみ。凍結済みのHTTP200/構造化拒否・副作用ゼロ・通常/refresh同一性反例のテストに限定。IAC8/9へ対応、RT2実装に依存、In progress。Audit: [RT2-tests-A1](../agent-audits/RT2-tests-A1.json)。結果指標は未計測、親が関連回帰と独立反例を確認する。
- Checkpoint: RT2-testsは指定5件と関連33件が成功、Mainがdiffと独立domain反例をreviewし受理。domain全108件、parser/HTML70件（原障害の公式URL External testを含む）、navigation64件成功。最初の検証では旧fixtureの専用欄欠落とnull診断metadata、既存の危険なidentity上書き期待を検出し修正。全面regression/format・最新origin統合・revision・配備・本番復旧は残作業。
- 次操作: 関連API再検証、format gate、owned codeのcheckpointを作成し、origin/mainのclean worktreeへ統合。ユーザーのWeb/Collector Program変更は混在させない。全入口revisionは最新origin上で更新し、配備workflowの既存停止保持・限定再要求を検証する。

- 2026-09-26 12:08 JSTの本番GETでpipelineの停止を確認。停止日時は11:57:37.2548098 JST。
- resource: `Race/JRA/20260404:Nakayama:10/race-detail`。failure group `EA4F1507E81E0AC4`、task `10421e31-843f-4bdb-8992-838655776a61`、batch `5f21b90a-6194-4094-95da-c283ace18ab8`。
- 失敗通知、対象resourceの履歴、batchを照合。同batchは直前14件が成功し、15件目が `JraUnexpectedValueException`、`TrackCondition(Turf)`、`RawValue=商事(株)` で失敗。要求revision 2、適用revision 0、試行1回。停止理由はこの通知を明示している。
- [公式結果](https://www.jra.go.jp/JRADB/accessS.html?CNAME=pw01sde1006202603031020260404/0B)の現在の天候・馬場欄は雨／ダート重。取得HTMLには勝馬情報 `馬主：青芝商事(株)` が存在する。これは全出走馬のowner取得元をResultへ拡張する根拠ではない。
- 現行 `RaceResultPageParser.ParseTrackConditionText` は見出し＋本文全体に `芝\s*(?<value>\S+)` を適用する。「天候 雨 ダート 重 馬主：青芝商事(株)」から「商事(株)」を抽出することを再現した。外部の異常値ではなく、本文の別項目を馬場状態と誤認できる欠陥。
- 当時snapshot、配備SHAは未取得。現行HTML＋同じ抽出式による再現と本番エラーの一致を証拠とし、実行バイナリや当時DOMの完全再現とは呼ばない。従来テストの具体的欠落は実装前に棚卸しする。
- Temporary recovery: 未実施。Permanent fix: Not started。今回の設計でコード、要求revision、task、停止状態を変更しない。

## 取得契約

1. 馬場状態は、対象Raceの概要内にある天候・馬場状態の専用項目からだけ取得する。本文全文、馬名、馬主、勝馬紹介、別Race、コース説明の「芝」「ダート」を検索対象にしない。
2. DOM→semantic snapshot→parserの実経路で項目境界・出所を保持する。既存のRoot/KeyValues/Sourceで境界を保持できるか実ページfixtureで確認し、不足する場合だけJRAの取得境界を補う。平坦化された本文へのfallbackは禁止。汎用snapshotにJRA専用の推測を追加しない。
3. 値は「良／稍重／重／不良」の完全一致。改行・空白や別spanは項目内で正規化し、部分一致で未知値を既知値に変えない。芝とダートの専用表示が双方あれば各々保存する。ダートのみなら芝は未記載のままとする。
4. 対象面の必須項目が欠落、専用項目の値が未知、同一面の表示が矛盾、対象Raceの境界が不明なら、保存前に構造化エラーとする。公式中止など正当な欠落は既存契約を維持し、通常結果の欠落と混同しない。空文字や「良」に丸めて成功させない。
5. 誤認した本文を無視することと、専用項目にある本当の異常値を無視することを区別する。未知値・同一性異常に対するシステムの安全停止は無効化しない。今回、全体の停止ポリシー変更や自動再開機能は追加しない。

## 実装・復旧の順序と承認範囲

承認後の第一段階は本障害のparser修正・回帰検証・配備・限定再取得・再開確認。第二段階は既存IAC1–IAC6に沿う馬主・確定待ち・オンライン補正。第一段階で第二段階の完了を偽装しない。

ただし既知の阪神11Rの誤馬番へ結果を書き込ませないことは**再開の前提**である。IAC3の副作用前identity検証を先行し、通常/refresh/ingestionから誤対応を保存できないことを確認する。対象の実状態を再読込し、未実装のholdやcancelだけを安全保証に使わない。不一致時は拒否を維持し、自動停止し得る残存問題を隠して継続運転を保証しない。別障害による再停止は復旧未完了として報告する。

1. **事前証拠:** pipeline reason、対象失敗、active/lease・配備版、対象の保存値を再確認。既存の自動停止を維持し、検証前にresumeしない。「停止不要」は新たなメンテナンス目的の全体停止を行わない方針として維持する。
2. **検証と配備:** 実ページ相当HTMLを実snapshotterに通してparser/handler/保存を検証する。API/Collector双方の修正版稼働、旧実行の終了または無効化を確認。配備手順が新たなAPI全停止やDB移行を必須にする場合は本承認の外であり、影響と代替を提示する。
3. **revision:** 本番と全登録入口の版を再確認し、第一段階には現行より大きい未使用の次版（現時点の候補3）を割り当てる。第二段階はさらに次版。別内容に同一revisionを再利用しない。先にRequiredRevisionだけ上げない。
4. **限定要求:** 既存revision preview/apply/recollectを `SpecificResources` でこの中山10Rだけに適用する。Result収集の失敗復旧であり、Card公開期間外でもResultが取得可能なら対象にできる。owner補正migrationや全期間のrecollectは使わない。新request/taskが冪等であること、既存priorityを勝手に上げないことを確認。停止中は要求の登録までであり実行成功とはしない。
5. **再開:** 最新の停止理由が本障害から変わっていないことと上記gateを再確認し、既存resume操作を1回行う。別の停止理由なら自動解除しない。新しい停止を競合して上書きしないよう実運用の単一操作主体を確認し、理由/versionの条件付き解除が必要なら実装前に操作契約を具体化する。
6. **本番確認:** 対象が新版で成功し、保存値がダート重、芝が勝手に生成されず、着順・馬の帰属が公式と一致することを確認。failureは実成功の記録で解決し、手動消去しない。少なくとも対象の終端成功＋別resourceの成功＋10分間の再停止なしを観測する。通常処理が10分を超えるなら1つの意味ある処理単位が完了するまで延長する。処理可能な別resourceがなければその事実と代替証拠を記録し、queue数だけを成功判定にしない。
7. **失敗時:** 同じ入力・同じ実装で再試行やresumeを反復しない。自動再停止を保持し、新証拠を保存。旧版に戻すと本件が再発するため、旧版へ戻して再開する運用をrollbackとしない。アプリ版の戻しが必要でも書込み・dispatchの安全性を別に確認し、DB復元、結果の付替え、queue/failure削除は行わない。

既存馬主対象の限定再取得は、[revision再取得案](20260926-revision-recollection.md)のCard取得期間・参照ゼロ・Race単位排他条件を維持する。Cardが公開終了していれば復元不能を明示し、Resultの勝馬ownerを全entryへ流用しない。本番補正前の差分確認と、破壊的操作への別途承認を省略しない。

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| IAC7 | 本障害相当のHTMLが実snapshotter経由でダート重として取得され、青芝商事(株)・芝を含む馬名や勝馬情報の有無/配置で値が変わらない | RT1 | HTML→snapshot→parser回帰。芝のみ/ダートのみ/双方/空白/改行/別spanも網羅 | Verified |
| IAC8 | 専用欄の未知値・矛盾・必須欠落・別Raceを保存前拒否し、安全停止を維持。公式中止等の正当な欠落を壊さない | RT1, RT2 | parser反例、handler/API副作用なし、完了分類・停止policy統合test | Verified |
| IAC9 | 修正版と旧実行の境界を確認してから次revisionへ進め、明示対象だけを冪等再要求。既知の誤馬番への副作用を防ぎ、他の停止理由を解除しない | RT2, RT3 | IAC3のidentity統合test、全登録入口/再送/低版active/再起動/限定preview、配備証拠 | Connected |
| IAC10 | 対象Resultが本番で新版成功し保存値・帰属が正しく、他の収集も進み再停止なし。失敗履歴を保持し、復旧と馬主補正全体の完了を分けて報告 | RT3 | 上記観測窓、resource履歴・failure・保存値・pipelineのGET照合 | Not started |

## Concern and agreement ledger

| ID | Evidence / impact | Recommended disposition / alternative / residual risk | AC/task | Agent position | User disposition | State |
| --- | --- | --- | --- | --- | --- | --- |
| RC1 | 全文regexが会社名を拾う。既知値だけの全文検索も他項目の「芝良」等を拾い得る | 専用欄の構造境界を採用。全文regex緩和・例外握潰しは却下。DOM変更時は明示失敗 | IAC7–8/RT1 | 同意 | 設計確認待ち | Resolved in design |
| RC2 | resumeは全体へ作用し、既知の誤馬番問題や別の新規障害を顕在化させ得る | identity gateを先行し、最新理由確認・新版限定再要求・観測。単純resumeと全件retryは却下。別障害時は停止保持し復旧未完了 | IAC9–10/RT2–3 | 安全装置の無効化に反対、処置に同意 | 設計確認待ち | Resolved in design |
| RC3 | 配備SHA/当時snapshotは未取得、旧処理と新revisionが混在し得る | 版確認・旧実行境界を運用gateとする。ローカル成功を配備証拠としない。アクセス不能なら本番段階はblocker | IAC9/RT3 | 同意 | 設計確認待ち | Resolved in design |
| RC4 | Result内の勝馬ownerが誤認原因だが、全頭ownerの正本にはならない | Card限定契約維持。馬主補正は別段階。全体成功/owner修復成功と混同しない | IAC7, IAC10/RT1, RT3 | 同意 | 設計確認待ち | Resolved in design |

## Task plan / review gates

| ID | Task | Owner / model tier / routing | Depends on | Exclusive write scope | Verification / completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- |
| RT1 | 専用欄抽出と実snapshot回帰 | Main / Lead、取得境界判断と最小修正が不可分。判断固定後に限定fixture sliceの委譲可否を再評価 | 本設計承認 | ScrapingのJRA parser、必要最小限のsnapshot境界、Scraping.Tests、fixture | IAC7–8。関連test成功＋production-shaped反例 | Proposed |
| RT2 | identity先行gate、revision入口、停止安全性統合 | Main / Lead、保存・版・停止契約の横断整合が必要 | 承認、RT1、既存IT3と同一ownerで直列 | Collector/API/Contracts/Domainの必要箇所と関連tests、登録入口 | IAC3/8/9の実経路test、既存正常取得の回帰 | Proposed |
| RT3 | 配備・限定再要求・resume・本番照合 | Main / Lead、運用権限・最新状態による判断 | RT1–2検証、明示運用承認、認証 | 承認対象の運用のみ、本文への証拠追記 | IAC9–10、配備版・終端結果・観測窓 | Proposed |

- Design/task-split review: Main。上記ACは成功・反例・保存・再送・運用の証拠へ対応。今回は短い文書統合のみで委譲なし。未確定のDOM実装詳細や本番権限は実装完了と偽らず、pre-implementationでテストファイルと入口を確定する。
- Concern review: RC1–4の処置を設計に反映。未知エラーを安全停止から除外しない。既存IAC1–IAC6は未完了のまま保持。
- Pre-implementation / implementation Checkpoint / Final: 未実施。実装承認後に実行。今回の文書検証は親recordへ記録する。
- 検証予定: `dotnet test tests/HorseRacingPrediction.Scraping.Tests --filter FullyQualifiedName~RaceResultPageParserTests` に加え、snapshotterとCollector/APIの関連統合test、CI相当build/testとformat検証。実行前にproject/fixtureと正確なfilterを確認する。実サイト単独testやregexの単体再現だけでIAC7–10をVerifiedにしない。
- Next action: IAC7–10とRC1–4、段階配備・限定再取得・再開の運用範囲を提示し承認を得る。承認前のコード変更と本番操作なし。

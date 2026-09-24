# Playwright 一時領域 ENOSPC の恒久対策

- Status: Proposed
- Change record schema: 2
- Owner: Main（親 owner: `01a0b79f-6ea8-7f61-84e1-cec6652615f4`）
- Created: 2026-09-25
- Updated: 2026-09-25
- Dedupe key: `root-cause:playwright-tmp-enospc:20260925`
- Parent contract: [`20260924_collection-error-closure`](../20260924_collection-error-closure/README.md) AC3 / AC4 / AC7、T1 / T2f / T3a

Approval: 2026-09-25、利用者が親スレッドで「修正を承認します。対応をお願いします。」と明示回答。提示済みの所有一時領域、安全な終了時・次回回収、観測、反例試験、既存 CI/CD による配備と独立検証を承認した。共有 `/tmp` 全削除、DB/履歴削除、未知補正、Recovery、強制 retry、resume、新 Workflow は承認対象外。C5 の実行基盤は承認によって事実確定したとは扱わず、read-only AWS 証拠を pre-implementation gate とした。

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Deployed; corrective amendment proposed | owned directory cleanupは配備済みだが、post-deploy観測でfilesystem free blocksがinvocationごとに減少し、プロセス寿命の追加修正が必要。 |
| Verification | AC1–AC4 verified; AC6 failed | lifecycle反例、CI/CD gateは成功。実Lambdaではowned rootが空へ戻っても14完了で約355 MiB減少し、bounded要件を満たさなかった。 |
| Deployment/operation | Safety-paused | 親Mainが一度resume後、再発予防のため03:52 JSTに再pause。Recovery、retry、cleanup、環境recycleは未実施。 |

## Context and incident ledger

Incident: 2026-09-25 00:21:56–00:21:57 JST、task `a55077b9-76cd-403e-9af4-c92b78013b83` の attempt 11 が `PlaywrightException: ENOSPC: no space left on device, mkdtemp '/tmp/playwright-artifacts-Vx0yzL'` で失敗し、全体収集が停止した。

Temporary recovery: なし。2026-09-25 00:39:24 JST の独立 GET で `paused=true`、pause 更新時刻 00:21:57.2402052、Running 0。本調査でも GET だけを実行し、停止状態を維持した。

Root cause boundary:

- **直接原因（確認済み）:** Playwright がブラウザー起動前に `/tmp/playwright-artifacts-*` を `mkdtemp` できず ENOSPC となった。URL 取得前なので JRA ページ内容や対象 trainer の入力エラーではない。
- **実行経路（確認済み）:** attempt 11 は batch `34afaabc-eb03-46c6-b1f9-5658c002e6a9` の ordinal 1 / 4。1件目だけ失敗記録があり、安全停止により後続3件は開始されていない。配備 revision は `f07a190d`、既存 deploy run `36007623965` は成功済み。
- **資源契約上の欠陥（確認済み）:** Playwright 1.62.0 は OS temp 配下へ artifact directory と browser profile directory を作り、通常は browser process close 後に削除する。現行 bootstrap は `HOME`、XDG、event、headers、response、failure reason、Playwright の temp を共有 `/tmp` 直下へ置き、アプリ所有の invocation 単位境界、上限、残留回収、使用量観測を持たない。Lambda 設定は ephemeral storage 4096 MiB、reserved concurrency 1。
- **漏えい起点（有力だが未確定）:** 通常の `PlaywrightWebBrowser.DisposeAsync` と `JraSessionExecutionScope.finally` は cleanup を行う。一方、14分打切りの `Environment.Exit(1)`、runtime 強制終了、Playwright cleanup 自体の失敗では managed cleanup または Node 側 cleanup の完了を証明できない。warm execution environment では `/tmp` が後続 invocation に残り得る。
- **不足証拠:** ENOSPC が block 容量不足か inode 不足か、失敗時の `/tmp` 使用量・inode・所有ディレクトリ一覧、cleanup error は保存されていない。latest attempt の `lambdaRequestId` は `local-3c5e...` で実Lambda UUIDと異なる。AWS read-only照合で同一warm streamの過去155 invocationと対象156番目、900秒timeoutまでは確認したが、失敗時の残留量や個々の残留起点は断定しない。

Corrective proposal: 共有 `/tmp` を掃除するのではなく、collector が所有する root の下に invocation ごとの一時ディレクトリを作り、Playwright、HOME/XDG、event/response をその中へ閉じ込める。通常、失敗、cancellation の終了時に当該 invocation directory だけを削除し、runtime 再初期化時には ownership marker と固定 root の検証後に stale child だけを回収する。回収前後の容量・inode・件数を秘密情報や生ログを含めず記録する。4 GiB 増量だけで漏えいを隠さない。

Permanent fix: Incomplete — owned directory lifecycle修正は配備済みだが、post-deploy free-block boundに失敗した。process-lifetime amendmentは再承認待ち。

Remaining risk: 現在の実 `/tmp` 状態が未取得で、停止解除後に同じ ENOSPC が直ちに再発し得る。resume の判断権限は親 Main にあり、本 task にはない。

## Post-deploy closure evidence and proposed amendment

2026-09-25 03:46 JST、親Mainが既存利用者承認に基づき一度だけresumeした。read-only観測では同一warm log streamで16 invocation start、15 finishを確認した。最初の14 finishまで、各finishは一貫して`OwnedKiB=4`、`InvocationDirectories=0`、inodeも開始前水準へ概ね回復した一方、filesystem free blocksは`4,194,280 KiB`から`3,830,536 KiB`へ363,744 KiB減少した。特に短時間 invocation の終了ごとに約30 MiB減少し、約2分のinvocation終了では回復も追加減少もなかった。ENOSPC再発はまだ発生していないが、AC6のbounded要件はこの時点で不合格とする。

03:52 JST、親Mainが`Safety hold: post-deploy temp filesystem free blocks continue decreasing after owned cleanup; ENOSPC AC6 verification failed. Preserve evidence; no automatic resume.`を理由にpauseした。03:56 JST時点で`isPaused=true`、Running 12。pauseは新規dispatchを止めるが既にRunningの収束を強制終了しないため、read-only観測を継続する。本taskはpause、resume、Recovery、retry、environment recycle、production cleanupを実行していない。

Evidence boundary and hypothesis:

- owned directoryが空、inodeが回復、blockだけが減る事象は、削除済みfileを生存processがopenしたまま保持する場合と整合する。
- collector本体はbootstrapの直接childとして`wait`されるが、Playwright Node/Chromium子孫は同じprocess groupを明示管理していない。14分deadlineの`Environment.Exit(1)`やPlaywright異常終了では子孫がbootstrapへreparentされ、所有directory削除後もopen fileのblocksを保持し得る。
- ただしproduction `/proc` のprocess tree、process group、deleted fdは取得しておらず、仮説は未確定。free blocks減少だけから特定processやfileを断定しない。
- WSL isolated fixtureでは、専用session内のgrandchildが30 MiB fileをopen後unlinkして生存すると`du=0`でもfree blocksが30,720 KiB減少し、同PGIDへTERM後に30,720 KiB全量が回復した。これはLinuxのdeleted-open-file機序と提案するPGID回収の有効性を独立再現するが、productionで同じprocessが原因であること自体は証明しない。

Proposed amendment（再承認対象）:

1. collectorをinvocation専用process group/sessionで起動し、直接child終了後に同groupの残存子孫へTERM、短いbounded wait、残存時だけKILLを行ってからowned directoryを削除する。
2. kill対象はbootstrapが当該invocation用に生成・保持したPGIDに限定し、PID/PGID再利用、空値、1、bootstrap自身、別groupを拒否する。共有process列挙や名前/prefixによるkillは行わない。
3. signal trapも直接childだけでなく同じ専用groupを終了し、元のcollector result、Lambda response、failure policyを変えない。
4. isolated Linux testで、collector direct child終了後も削除済み30 MiB fileをopen保持するgrandchildを再現し、修正前のfree-block残留と修正後の回復を確認する。別process group、unrelated process、PID/PGID不正値は生存する反例を必須にする。
5. metricsへ秘密情報を含めないgroup残存process数とdeleted-fd block推定値を追加できるか検証する。`/proc`で独立取得不能ならunknownを記録し、全system processを走査しない。
6. 配備後は再び一度の明示resume後、同一warm streamで最低20 finishまたは従来減少量を検出できる十分な回数を観測し、finish後`OwnedKiB=4`、directories 0、free blocks/inodesがboundedであることを確認する。通常2周期確認は別途維持する。

## Goals

1. production-shaped test で Playwright temp の作成と残留を再現し、ENOSPC へ至る資源寿命を説明可能にする。
2. collector が所有する一時資源だけを invocation 単位で隔離し、正常・例外・cancellation・強制終了後の次回起動で安全に回収する。
3. cleanup が他プロセス、並行利用、共有 `/tmp`、現在実行中の invocation を削除しないことを反例で保証する。
4. 既存 CI/CD で配備後、親 Main が許可した場合に限り、独立 GET で対象の正常終端、次の処理単位の進行、2周期以上の無再発を確認する。

## Non-goals and safety boundaries

- 共有 `/tmp` の全削除、prefix だけを根拠にした未知ディレクトリ削除、DB・履歴・failure notification の削除を行わない。
- Recovery、強制 retry、resume、本番 mutation を本 record の設計承認に含めない。resume は親 Main の判断権限とする。
- ephemeral storage の増量だけを恒久修正としない。実測が別途容量不足を示した場合も、lifecycle 修正と観測を先に満たす。
- Playwright package の fork、JRA parser、trainer-profile の同定仕様、停止 policy の緩和、新 GitHub Workflow、Issue 作成を行わない。
- `docs/changes/20260919_collection-monitor-root-cause-triage/README.md` は参照照合だけとし、作成・編集・移動・削除・commit 対象化しない。
- 資格情報、未編集 CloudWatch logs、API key、ページ本文を保存・出力しない。

## Technical impact and decisions

### D1. アプリ所有 root と invocation lease

- production Lambda bootstrap は `/tmp/horse-racing-prediction-collector/` を唯一の app-owned temp root とする。
- invocation ごとに安全な一意 child directory を作成し、所有 marker をその child 内に作る。`TMPDIR`、`HOME`、`XDG_CACHE_HOME`、`XDG_CONFIG_HOME`、event、headers、response、failure reason は child 配下へ向ける。
- cleanup 対象は、固定 absolute root の直下、期待する directory 形状、所有 marker、現在 invocation ではないことをすべて検証できた child に限定する。symlink を辿らない。root、`/tmp`、空文字、相対 path は削除対象にしない。
- reserved concurrency 1 は現在の追加防御であり、cleanup 安全性をそれだけに依存させない。同一 root の並行 invocation を test し、active lease は保持する。

### D2. cleanup の二段階化

- invocation の success / reported failure 後に、当該 child だけを削除する。
- runtime の abrupt termination で後処理が走らない場合に備え、次回 bootstrap 初期化または invocation 開始時に stale 判定を行う。age だけではなく marker と active lease を必要条件にする。
- Playwright/.NET の内部 cleanup は維持する。外側 cleanup はその完了に依存しない防御層であり、ブラウザーが稼働中の directory を削除しない。
- cleanup 失敗は握り潰して成功扱いにしない。現在 invocation の本処理結果と区別した bounded diagnostic を残し、次回の preflight で再評価する。ただし cleanup failure だけを理由に未知データを補正しない。

### D3. 観測と容量 gate

- invocation 前後に app-owned root の directory count、bytes、filesystem free bytes、free inode（取得可能な platform のみ）を構造化して記録する。directory 名、ページ URL、credential は記録しない。
- Playwright 起動前に temp directory を作れない場合、`TempStorageExhausted` 相当の安定分類へ包むかは実装時に既存 failure contract と照合する。元の ENOSPC 情報を失わず、安全停止は維持する。
- 現行 4096 MiB は据え置く。lifecycle 修正後の最大 production-shaped 使用量と余裕から不足が実証された場合だけ、別の明示判断として増量を検討する。

### D4. runtime telemetry の整合

- `lambdaRequestId=local-*` が本当に local queue を示すのか、bootstrap header parsing の欠落なのかを CloudWatch と invocation metadata で確定する。
- この確認は実装対象を決める事実 gate である。Lambda bootstrap が実行経路と確認できるまで、bootstrap 修正を恒久対策として確定しない。local queue、別 container、または correlation 欠落が確認された場合は、その実経路へ設計を更新して再承認を得る。
- API task/attempt/batch の相関 ID が実行基盤を誤表示する場合も、原因経路と telemetry defect を分離して扱う。public API schema または実装対象の変更が必要なら本 record を更新して再承認する。

## Documentation updates

- 本 record を新規作成し、incident evidence、承認境界、lifecycle 設計、反例、検証計画を記録した。
- `docs/11-automation-design.md` に collector の app-owned temp root、invocation cleanup、stale recovery、観測契約を現在運用の正本として追記した。
- `.github/workflows/app-ci.yml` と `.github/workflows/app-deploy.yml` の既存 verify jobへ専用 lifecycle test を追加した。新 Workflow は作成していない。
- `docs/26-collection-platform-design.md` と `docs/23-jra-scraping-redesign.md` を確認した。今回の runtime ownership は収集データ契約や JRA navigation 契約を変えないため更新しない。

## Concern and agreement ledger

| ID | Concern and evidence | Impact | Proposed disposition | AC/task/test | Agent position | User disposition | State |
| --- | --- | --- | --- | --- | --- | --- | --- |
| C1 | ENOSPC は容量と inode の両方を意味し得る。production の失敗時実測は未保存。 | 誤原因に対する容量増量や不十分な修正。 | 両方を観測し、owned-resource lifecycle の再現を先に行う。増量単独案は却下。 | AC1/T1/V1 | 推奨 | 2026-09-25 限定修正を承認 | Resolved in design |
| C2 | `/tmp` には runtime、Chromium、他ライブラリの資源もある。 | 広域削除で実行中 process や診断を破壊。 | 固定 app-owned root、marker、active lease、symlink 非追跡を全て満たす child だけ削除。 | AC2,AC3/T2/V2-V6 | 強く推奨。共有 `/tmp` cleanup には反対。 | 2026-09-25 限定修正を承認 | Resolved in design |
| C3 | abrupt termination では同一 invocation の finally は信用できない。 | warm environment に残留し再発。 | invocation 終了 cleanup と次回起動 stale cleanup の二層化。強制終了後の forward test を必須化。 | AC2/T2/V4 | 推奨 | 2026-09-25 限定修正を承認 | Resolved in design |
| C4 | stale 判定と次 invocation が競合し得る。reserved concurrency 1 も将来変更され得る。 | active directory の誤削除。 | active lease を明示し、並行利用反例を test。concurrency 設定だけには依存しない。 | AC3/T2/V5 | 推奨 | 2026-09-25 限定修正を承認 | Resolved in design |
| C5 | API attempt の `local-*` correlation と Lambda 想定が矛盾した。AWS read-only 証拠で障害時刻 00:21:55 JST の Lambda request `39ac8b64-...` と ENOSPC、同一 warm log stream、900秒 timeoutを確認した。 | 修正対象 runtime の誤認、CloudWatch 誤相関。 | 実行基盤は Lambda と確定し、承認済み bootstrap 設計を維持する。`local-*` は別の telemetry defect として元 request ID を欠落させない bounded test を含める。 | AC1/T1/V1 | Lambda lifecycle 修正へ進む。API相関だけで runtime を判定しない。 | 2026-09-25 限定修正を承認 | Resolved in design |
| C6 | cleanup 配備だけでは既存停止・失敗taskは進まない。 | 配備を復旧完了と誤認。 | 配備と Recovery/resume を分離。親 Main の個別判断後だけ AC5 を観測。 | AC4,AC5/T3,T4/V8-V9 | 推奨 | 2026-09-25 限定修正を承認 | Resolved in design |
| C7 | post-deployでowned directoryとinodeは回復したがfree blocksが14 finishで約355 MiB減少。direct childだけをwaitするbootstrapはPlaywright/Chromium子孫の寿命を所有していない。 | 現修正のままwarm reuseするとENOSPC再発のおそれ。広域killは別processを誤停止する。 | invocation専用PGIDだけをTERM/KILLする設計と、deleted-open-file grandchild・別group生存の反例testを追加する。production `/proc`未取得のため原因processは断定しない。 | AC6,AC7/T4,T5/V10-V12 | process group境界を明示する修正を推奨。名前検索・全process killには反対。 | 再承認待ち | Open decision |

C5 の実行基盤 gate は read-only AWS 証拠で Lambda と確定し、承認済み D1–D4 を変更しない。C1 の容量対 inode は失敗時に未観測だが、共有領域を削除しない観測・所有権設計はどちらにも必要な安全条件であり、production-shaped local fixture と配備後 telemetry で検証を続ける。

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | ENOSPC を production-shaped filesystem 制限で再現し、block/inode、owned temp、直前終了形態、実行基盤 correlation の確認値と不足値を区別して記録する。 | T1 | V1: fixture + read-only CloudWatch/config/task/attempt/batch matrix | Verified |
| AC2 | success、handler exception、cancellation、Playwright launch failure、collector child 強制終了の各経路で、実行中でない app-owned invocation directory だけが最終的に削除される。 | T2 | V2-V4 automated shell/runtime tests | Verified |
| AC3 | 共有 `/tmp` の unrelated file、marker 不正、symlink、active invocation、並行 invocation は削除されず、path が空・root・範囲外なら cleanup が安全に失敗する。 | T2 | V5-V6 destructive counterexample tests in isolated temp root | Verified |
| AC4 | 既存 build/test/format、Terraform validation、collector container build、deploy guard tests が通り、安全停止・有限 retry・履歴保持・既存 CI/CD 契約が維持される。 | T3 | V7 workflow-equivalent gates | Verified |
| AC5 | 承認済み既存 CI/CD 配備後、親 Main が別途許可した operation に限り、対象または根拠ある replacement が正常終端し、独立した後続処理が進み、通常周期2回以上で ENOSPC と即時再停止がない。 | T4 | V8 deployment revision GET、V9 bounded task/attempt/batch GET | Not started |
| AC6 | 配備後観測で app-owned temp 使用量が bounded であることを示し、容量増量なしで余裕を確認する。初回V10はfree-block boundに失敗しており、corrective amendment後に再検証する。 | T4,T5 | V10 sanitized resource telemetry | Connected |
| AC7 | direct child終了・bootstrap signal時に当該invocation専用process groupの子孫だけがboundedに終了し、削除済みopen fileのblocksが回収され、unrelated groupは生存する。 | T5 | V11 process-group/grandchild counterexamples、V12 Linux/container regression | Not started |

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State | Routing | Audit | Result metrics |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | 実行基盤、容量/inode、残留 lifetime、correlation を確定し再現fixtureを作る（AC1） | Main | Lead | Approval、AWS read access | record evidence と test fixture。production は read-only | V1 | Lambda/config/log/API fact matrix、ENOSPC fail-closed fixture | Verified | Lead — production credential、runtime/security 判断、根本原因確定 | none | unavailable; retries 0; corrections 0; reviews 1 |
| T2 | app-owned invocation temp lifecycle と反例 test を実装する（AC2,AC3） | Main | Lead | T1 runtime gate、frozen D1-D4 | `deploy/lambda-bootstrap`、`deploy/collector-temp-lifecycle.sh`、`Dockerfile.collector-lambda`、対応 test の排他範囲。infra/API schema は変更禁止 | V2-V6 | success/failure/signal/stale/active/PID reuse/unowned/symlink/root/header tests green | Verified | Lead — path deletion safety、abrupt lifecycle、統合を同一の小変更で保持。分割review費用が上回る | none | unavailable; retries 2; corrections 3; reviews 2 |
| T3 | 正本文書と workflow-equivalent regression を統合する（AC4） | Main | Lead | T2 | `docs/11-automation-design.md`、本 record、必要時のみ既存 infra test | V7 | local、app-ci、app-deployのformat/build/test/Terraform/container/deploy guard成功 | Verified | Lead — 統合、CI/CD、最終 scope 判定 | none | unavailable; retries 1; corrections 1; reviews 2 |
| T4 | 既存 CI/CD 配備と独立 production verification を行う（AC5,AC6） | 親 Main（operation owner）+ 本 task read-only verifier | Lead + Review | T3、既存CI/CD、operation 個別許可 | 既存 CI/CD。production mutation は親 Main のみ | V8-V10 | revision、terminal attempt、後続、2周期、resource telemetry | Dependent | Lead — 本番権限、安全、最終判定。resume は本 task 非所有 | none | unavailable; retries 0; corrections 0; reviews 0 |
| T5 | invocation専用process groupでPlaywright/Chromium子孫を終了し、deleted-open-file block残留を防ぐ（AC6,AC7） | Main | Lead | C7再承認 | `deploy/lambda-bootstrap`、lifecycle helper、専用test、必要なDocker packageだけ。API/DB/queue/policyは変更禁止 | V11-V12 | grandchild block回収、別group生存、signal/nonzero、Linux/container/CI成功 | Proposed | Lead — process kill安全性、PID/PGID再利用、production統合は分離不能 | none | unavailable; retries 0; corrections 0; reviews 0 |

### Agent ownership and escalation

- 現在は未承認で、design と read-only diagnosis だけを Main が実施した。production secret と根本原因判断を分離できず、短い設計段階の委譲費用が上回るため agent は起動していない。
- 承認後の T2 は、D1–D4、exact files、test names、最小 test、workflow regression、反例、変更禁止範囲を凍結してから cost-sensitive coding worker を第一候補にする。worker は他者の変更を戻さず、排他 write scope 外を編集しない。
- path validation、concurrency、public failure contract、infra size、API schema、production operation に判断が広がる場合は即座に Main へ戻す。focused correction 1回後も gate 不合格なら昇格する。
- delegated coding を開始した場合だけ `agent-audits/` を作成し、requested/observed model、usage availability、patch attribution、quality/scope gate、独立反証、retry/promotion を記録して validator を実行する。

## Verification plan

- **V1 root-cause reproduction:** isolated filesystem または quota fixture で temp create の ENOSPC を再現し、正常 close、managed abrupt exit、次回起動の directory 差を測る。AWS 再認証後は同時刻の CloudWatch を bounded query し、未編集 logs を record へ貼らず集約値だけ保存する。
- **V2 normal:** mock collector child success 後、現在 invocation child がなく unrelated sibling が残る。
- **V3 exception/cancellation:** non-zero exit、reported handler failure、TERM/timeout 相当後に current child が回収され、元の exit/result が隠れない。
- **V4 abrupt forward recovery:** cleanup を実行せず child を残した前回 process を fixture で作り、次回 startup が marker 付き stale child だけを回収する。
- **V5 ownership counterexamples:** no marker、改変 marker、root 外、symlink、unexpected nesting、空 path、`/`、`/tmp` は拒否する。
- **V6 concurrency counterexample:** active lease を持つ invocation と cleanup を重ね、active child と unrelated child が保持される。
- **V7 regression:** repository CI と同じ format/build/test、`tests/scripts/test-deploy-pipeline-state.ps1`、Terraform fmt/validate、collector image build。exact commands は承認後の pre-implementation review で現行 workflow から固定する。
- **V8 deploy:** 新 Workflow を作らず既存 `app-deploy` の成功、deployed revision 一致、pipeline paused 維持を確認する。
- **V9 production behavior:** 親 Main の operation 許可後だけ、対象 terminal attempt、別 batch/task の進行、2周期以上の無再発を GET で確認する。Recovery/resume は verifier が実行しない。
- **V10 resource telemetry:** app-owned directory count/bytes と free bytes/inode の sanitized 前後値を確認する。未取得なら AC6 は未完了のままにする。
- **V11 process-group counterexamples:** direct childが削除済み30 MiB fileをopenするgrandchildを残して終了するfixtureで、当該groupだけが終了しfree blocksが回復する。別group、unrelated process、不正PGIDは生存する。
- **V12 amended regression/deploy:** signal、nonzero、header相関、既存lifecycle群、Linux、container、Terraform、CI/CDを再実行し、productionでは同一warm streamの十分なfinish数でfree blocks/inodesのboundを確認する。

## Review gates

- **Design and task-split review — Main, 2026-09-25:** AC1–AC6 を T1–T4 と V1–V10 に対応付けた。T2 だけが設計凍結後に独立委譲可能。T1 は production credential と原因判断、T3 は統合/CI、T4 は本番権限のため Lead が保持する。write scope は直列で重ならない。
- **Concern and agreement review — Main, 2026-09-25:** 容量対 inode、共有 temp の破壊、abrupt termination、並行 cleanup、correlation 欠落、配備と復旧の混同を C1–C6 で確認。利用者は限定修正と全 safety boundary を承認。C5 は承認自体では解消とせず、その後の AWS/API 照合で Lambda と確定して `Resolved in design` とした。
- **Pre-implementation review — Main, 2026-09-25:** T1 を `In progress`、Lambda runtime gate を満たした T2 を `Runnable`、T3/T4 を `Dependent` とした。T2 の排他 write scope は `deploy/lambda-bootstrap`、新規 lifecycle helper、`Dockerfile.collector-lambda`、専用 script test。共有 `/tmp`、infra size、API schema、親/禁止文書は変更禁止。test は success/failure/cancellation/stale/active/unowned/symlink/root拒否とし、path safety または外部契約変更が必要なら設計へ戻す。削除安全性と bootstrap 統合が不可分な小変更のため Main が保持する。
- **Checkpoint review — Main, 2026-09-25:** 下記 `Checkpoint review` のとおりAC1–AC3をVerified、AC4をConnectedと判定。local不可のgateは既存CIへ残した。
- **Final review — Main, 2026-09-25:** AC1–AC4とT1–T3はlocal、Linux CI、Terraform/container build、既存app-deploy、AWS revision、API停止状態の独立証拠へ追跡できる。禁止した共有`/tmp`削除、DB/履歴削除、Recovery、retry、resume、新Workflow、親record編集は実施していない。AC5/AC6とT4は、停止解除後の実invocation、後続進行、2周期、sanitized temp metricsを必要とし、本taskのoperation権限外なので`Dependent`を維持する。よって配備は成功だがrecord全体を`Implemented`にはしない。
- **Post-deploy closure review — Main, 2026-09-25:** 一度の明示resumeでV10を実測し、owned cleanupは成功したがfree blocksのboundは失敗した。テスト成功やdirectory countだけでAC6をVerifiedにしない。C7を`Open decision`、AC7/T5/V11-V12を追加し、Statusを`Proposed`へ戻した。process-group killは誤停止リスクを持つため既存承認から自動拡張せず、再承認前にproduction codeを変更しない。

## Verification record

2026-09-25 の read-only evidence:

- 00:39:24 JST の独立 GET: paused、pause updatedAt 00:21:57.2402052、Running 0（引継ぎ証拠）。
- 本 task の GET: pipeline reason、ENOSPC task search 3件、対象 resource の request/task/attempt history 100件上限、batch `34afaabc-...` を確認。mutation なし。
- 対象 attempt 11 は 0.95秒で browser launch 前に失敗。attempt 1–10 は別時刻の `TimeoutException 10000ms` であり、これだけでは temp leak の起点と断定できない。
- current source: Playwright 1.62.0 の bundled `coreBundle.js` は OS temp に artifact/profile directory を作り、browser process close で `removeFolders` する。`PlaywrightWebBrowser.DisposeAsync`、`JraSessionExecutionScope.finally`、`Environment.Exit(1)`、bootstrap `/tmp` 利用、Terraform 4096 MiB/concurrency 1を照合した。
- CodeGraph directory はあるが index は利用不能との tool 応答を得たため、指示どおり index を作らず source/`rg` へ fallback した。
- AWS read-only check は session expired。再認証を本 task が代行せず、不足証拠として維持した。
- `python scripts/audit_agent_execution.py docs/changes/20260925_playwright-tmp-enospc/README.md` は valid。
- `python .codex/skills/document-driven-development/scripts/validate_change_records.py docs/changes/20260925_playwright-tmp-enospc/README.md` は `issues=0`。初回に repository root の `scripts/validate_change_records.py` を参照して不在だった記録を、skill 配下の正規 path で再検証した。
- 本番 cleanup、配備、retry、Recovery、resume は実施していない。

2026-09-25 approval後の evidence:

- AWS read-only認証復旧後、Lambda configuration は timeout 900秒、memory 2048 MiB、ephemeral storage 4096 MiB、reserved concurrency 1、最終更新 2026-09-24 22:52:16 JST と確認。環境変数・secretは取得していない。
- 00:21:55 JST開始のLambda request `39ac8b64-99b0-5bc1-a1fe-e84bdbe3b0ff`が対象ENOSPCと一致。同一warm log streamでは23:47:36以降155 invocationがSTART/END/REPORTを完了し、対象は156番目。対象はENOSPCを反復後900000 msでtimeoutし、Max Memory Used 2033 / 2048 MB。生ログは保存せず集約値だけ記録した。
- API attemptの`local-*`は実Lambda request IDと不一致。bootstrap header parserをCRLF/LF両対応・欠落時fail-closedとし、fixtureで実UUIDがcollectorへ渡ることを確認した。
- `tests/scripts/test-collector-temp-lifecycle.ps1`: normal、stale、ENOSPC create failure、active、PID reuse、unowned、symlink、unsafe root、success/nonzero、Lambda header、TERM cancellationの全case成功。WSL Linux上でも実symlinkが保持される反例を独立確認した。
- `tests/scripts/test-deploy-pipeline-state.ps1`: 既存17 case成功。
- `dotnet format HorseRacingPrediction.sln --no-restore --verify-no-changes`: 成功。
- 初回`dotnet build --no-restore`はassets未生成で失敗。`dotnet restore HorseRacingPrediction.sln`成功後、同じRelease buildは警告0・error 0で成功。
- `dotnet test HorseRacingPrediction.sln --no-build --configuration Release --filter "TestCategory!=External"`: 1217 passed、1 skipped、0 failed。
- local Docker engineは未起動、Terraform CLIは未導入のためlocal container buildとTerraform validationは未実行。既存CIの必須gateとして残す。
- PR #72 の初回app-ci run `36041077489` はLinuxで非実行属性のbootstrapを直接起動した専用testが失敗。production Dockerfileは実行属性を付与するためruntime defectではなくtest portability defect。testを`sh bootstrap`起動へ修正し、全caseをlocal再実行した。CI再実行結果待ち。
- PR #72 のapp-ci再実行 `36041296970` は全gate成功。PRはmerge commit `fe8ddf527260c57a20cc8375ac67f232c9fb23d8` としてmainへ統合された。
- 既存app-deploy run `36041894195` は同merge commitを対象に全job成功。verifyでlifecycle Linux test、build/test、Terraform validation、collector Lambda container buildが成功し、`deploy-collector-lambda`、API deploy、health checkも成功した。
- 2026-09-25 03:43 JSTのAWS read-only確認でLambdaは`Active`、`LastUpdateStatus=Successful`、`LastModified=2026-09-25 03:40:20 JST`、revision `67fee38e-c26d-4980-8460-bbe9e59b8cdd`、resolved image digest `sha256:132769ee12405caa04d7cdc5f545ea5d27b552abbe503bef57919eb3d03b8675`。4096 MiB、2048 MiB、900秒は維持された。
- 同時刻のproduction API GETで`isPaused=true`、reasonとupdatedAtは元ENOSPC停止のまま、Running 0。mutationは実施していない。停止中のため新bootstrapの実invocationとsanitized temp metricsは存在せず、AC5/AC6を推測で完了していない。

## Checkpoint review

2026-09-25 Main: AC1はproduction API/AWS相関と不足値、AC2/AC3は独立temp rootの反例群でVerified。実装は固定owned root直下だけを対象とし、marker、direct-child、non-symlink、PID+process start leaseを全て検証する。通常・非zero・signalではcurrent childだけを削除し、hard kill残留は次bootstrapでdead lease確認後に回収する。共有`/tmp`列挙、容量増量、failure policy変更、DB操作はdiffにない。初回local test失敗2件（未設定TMPDIR assertion、fake curl URL抽出）、PID reuse review finding、初回Linux CIの非実行属性test defectを修正し全群を再実行した。app-ci再実行とapp-deployでTerraform/containerを含む全gateが成功したためAC4/T3をVerifiedとする。T4とAC5/AC6は配備済みだが、親operation後の実行観測待ちで未完了。

## Approval boundary and next action

利用者は初回の限定修正を明示承認し、その範囲は実装、検証、既存CI/CD配備まで完了した。しかしpost-deploy観測でAC6が不合格となり、process-group終了という新しいmaterial decisionが必要になったためrecordを`Proposed`へ戻した。C7の再承認なしにproduction codeを変更せず、`Implemented`にはしない。

次はpause後のRunning収束とmetrics最終値をread-only確認し、C7/AC7/T5/V11-V12の再承認を親Main経由で利用者へ求める。承認後だけprocess-group amendmentを実装する。本taskはRecovery、retry、resume、environment recycle、本番cleanupを行わない。

中断時点の未コミット対象は本recordの配備証拠追記だけ。次回検証は親operation後のV9/V10。目的外の未コミットファイルはない。

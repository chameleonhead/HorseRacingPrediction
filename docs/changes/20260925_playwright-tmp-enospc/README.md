# Playwright 一時領域 ENOSPC の恒久対策

- Status: Approved
- Change record schema: 2
- Owner: Main（親 owner: `01a0b79f-6ea8-7f61-84e1-cec6652615f4`）
- Created: 2026-09-25
- Updated: 2026-09-25
- Dedupe key: `root-cause:playwright-tmp-enospc:20260925`
- Parent contract: [`20260924_collection-error-closure`](../20260924_collection-error-closure/README.md) AC3 / AC4 / AC7、T1 / T2f / T3a

Approval: 2026-09-25、利用者が親スレッドで「修正を承認します。対応をお願いします。」と明示回答。提示済みの所有一時領域、安全な終了時・次回回収、観測、反例試験、既存 CI/CD による配備と独立検証を承認した。共有 `/tmp` 全削除、DB/履歴削除、未知補正、Recovery、強制 retry、resume、新 Workflow は承認対象外。C5 の実行基盤は承認によって事実確定したとは扱わず、read-only AWS 証拠を pre-implementation gate とした。

Amendment approval: 2026-09-25、利用者がC7のinvocation専用process group、当該group限定のTERM/bounded wait/KILL、deleted-open-fileと別group生存の反例、既存CI/CD再配備、停止を維持した再観測を「承認します」と明示回答。名前検索・全process kill、共有`/tmp`削除、Recovery、retry、resume、environment recycleは引き続き承認対象外。

C8 diagnostic approval: 2026-09-25、利用者が親Mainの承認依頼へ「お願いします」と明示回答。削除前後の容量、対象PGID termination前後の安全な生存判定、post-delete blocks/inodesを秘密情報なしで記録し、session離脱・owned-root外writeの隔離Linux/container反例を追加して、関連回帰と既存CI/CDで検証・配備し原因識別証拠と最小修正案を作る範囲を承認した。新Workflow、全`/proc`走査、command/env/secretログ、広範kill、共有`/tmp`cleanup、容量増量、DB/履歴操作、retry、Recovery、recycle、resumeは禁止。本番停止を維持し、診断配備後の実行は親Mainの別途安全判断とする。

C9 temporary capacity approval: 2026-09-25、利用者が「まずはLambdaの一時領域を10GBに変更し、状況を監視」と明示指示した。従来の容量増量禁止を対象collector Lambdaのephemeral storage `10240 MiB`に限り上書きし、既存Terraform、関連contract test、既存CI/CD配備、独立設定確認を承認した。これは原因解消またはAC6達成の判定ではなく、段階診断telemetryを維持した監視余裕の暫定拡大である。memory、timeout、concurrency、他service、DB、共有`/tmp`cleanup、kill、retry、Recovery、recycle、履歴削除、resume、新Workflowは範囲外。resumeは親Main所有とする。

C10 core suppression approval: 2026-09-25、利用者がcore dumpは不要としてC10/T8/V15を明示承認した。collector子processの起動前だけcore sizeを0にし、raw crash dump生成を抑止する。異常終了、元exit status、互換性チェック、例外報告、段階診断は維持し、成功へ偽装しない。unrelated processのcore policy、API/DB/queue、cleanup/kill範囲を変更しない。`CollectionDispatchCompatibilityException`は親record T2/T2fの別原因として未解消のまま保持する。新Workflow、resume、retry、Recovery、recycle、DB/履歴操作は禁止。

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Core suppression, bounded diagnostics, and compatibility snapshot fix deployed | invocation専用PGID、段階容量・bounded descendant診断、collector子processだけのcore size 0に加え、dispatch/rebuild/acquireのtask metadata snapshotをmainへ統合し、既存CI/CDで配備済み。worker互換性検査は維持。 |
| Verification | V11/V12/V13/V15/V16 verified | Linux/container core反例と段階診断、resource A→B/A→missing、wake再構築、legacy null fallbackを確認。production 39 finishでblocks/inodesが完全にbounded、互換性例外・abort・ENOSPC 0件。既存lifecycle/17 deploy guard、format/build、非External 1220 passed / 1 skipped、Terraform/container/CI/CDが成功。 |
| Deployment/operation | Deployed; bounded runtime verified; cycle gate pending | Lambda revision `fd9f0902-b4f8-4a70-bb61-b18de175d842`、image digest `sha256:695fb958c4831ae699b8f78172aa7975fcd5837fbdd2f6e60ad5162544dbb4dd`。10240 MiB、memory 2048 MiB、timeout 900秒、Active/Successful。親Mainのguarded resume後、39 session/155 taskで互換性例外、core abort、ENOSPC、容量減少なしを確認したが、dispatcher/scanner通常周期2回の時刻・ID・API証拠は未取得。 |

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

03:52 JST、親Mainが`Safety hold: post-deploy temp filesystem free blocks continue decreasing after owned cleanup; ENOSPC AC6 verification failed. Preserve evidence; no automatic resume.`を理由にpauseした。pauseは新規dispatchを止めるが既にRunningの収束を強制終了しない。04:08 JSTにRunning 0へ自然収束し、`isPaused=true`を維持した。本taskはpause、resume、Recovery、retry、environment recycle、production cleanupを実行していない。

Evidence boundary and hypothesis:

- owned directoryが空、inodeが回復、blockだけが減る事象は、削除済みfileを生存processがopenしたまま保持する場合と整合する。
- 同一streamの15 REPORTは約119–122秒が3件、約0.79–0.85秒が12件。free blocks減少も短時間12 invocationの各finishと一対一に約30 MiBずつ対応し、長時間3 invocationのfinishでは追加減少も回復もなかった。常時増加ではなく特定の早期終了経路に結び付く。
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
- ephemeral storage の増量だけを恒久修正としない。C9では監視余裕の暫定拡大として対象Lambdaだけを10240 MiBへ変更するが、段階診断とAC6は継続する。
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
- C9の明示判断により対象collector Lambdaのephemeral storageだけをAWS上限の10240 MiBへ変更する。memory 2048 MiB、timeout 900秒、reserved concurrency 1は変更しない。容量増量を原因解消の証拠にせず、既存の段階telemetryで絶対残量と減少速度を監視する。

### D4. runtime telemetry の整合

- `lambdaRequestId=local-*` が本当に local queue を示すのか、bootstrap header parsing の欠落なのかを CloudWatch と invocation metadata で確定する。
- この確認は実装対象を決める事実 gate である。Lambda bootstrap が実行経路と確認できるまで、bootstrap 修正を恒久対策として確定しない。local queue、別 container、または correlation 欠落が確認された場合は、その実経路へ設計を更新して再承認を得る。
- API task/attempt/batch の相関 ID が実行基盤を誤表示する場合も、原因経路と telemetry defect を分離して扱う。public API schema または実装対象の変更が必要なら本 record を更新して再承認する。

## Documentation updates

- 本 record を新規作成し、incident evidence、承認境界、lifecycle 設計、反例、検証計画を記録した。
- `docs/11-automation-design.md` に collector の app-owned temp root、invocation cleanup、stale recovery、通常観測とC8 bounded descendant診断の秘密情報非記録契約を現在運用の正本として追記した。
- `docs/26-collection-platform-design.md` にdispatch候補、wake envelope再構築、leaseがtask metadata snapshotを共有し、legacy null metadataだけresourceへfallbackする互換性契約を追記した。
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
| C7 | post-deployでowned directoryとinodeは回復したがfree blocksが14 finishで約355 MiB減少。direct childだけをwaitするbootstrapはPlaywright/Chromium子孫の寿命を所有していない。 | 現修正のままwarm reuseするとENOSPC再発のおそれ。広域killは別processを誤停止する。 | invocation専用PGIDだけをTERM/KILLする設計と、deleted-open-file grandchild・別group生存の反例testを追加する。production `/proc`未取得のため原因processは断定しない。 | AC6,AC7/T4,T5/V10-V12 | process group境界を明示する修正を推奨。名前検索・全process killには反対。 | 2026-09-25 amendmentを承認 | Resolved in design |
| C8 | process-group amendment配備後も、同一warm streamの短時間finish 9件でfree blocksが`4,194,308 KiB`から`3,921,304 KiB`へ273,004 KiB減少した。owned directoryは各finishで4 KiB、directory 0、inodeは概ね回復した。 | PGID回収だけではAC6を満たさず、再開継続はENOSPC再発riskを持つ。現telemetryではPGID外へ離脱した子孫、owned root外write、削除時点のopen fd、filesystem固有挙動を区別できない。 | safety pauseを維持する。次変更は、削除前owned bytes、group termination前後の安全なPGID生存有無、削除直後free blocksを秘密情報なしで段階観測し、production-shaped containerでsession離脱・outside-root反例を追加してから最小修正を再提案する。全`/proc`走査、名前kill、共有`/tmp`cleanup、容量増量は行わない。 | AC6/T4,T6/V10,V13 | 原因をprocess escapeと断定せず、bounded診断を先行する。根本修正への拡張は別途再承認。 | 2026-09-25 bounded診断を承認 | Resolved in design |
| C9 | 4096 MiB環境では短時間finishごとに約30 MiB減少し、恒久原因未確定のまま再開すると再度ENOSPCへ達する。AWSは`/tmp`を512–10240 MiBの1 MiB単位で設定でき、Terraform providerも10240 MiBを許容する。 | 10 GBは監視時間を延ばすが、漏出速度や原因を改善せず、費用増と将来の再枯渇を残す。 | 対象Lambdaだけを10240 MiBへ変更し、診断telemetryを維持する。配備とresumeを分離し、恒久修正・AC6達成とは扱わない。 | AC6,AC8/T4,T6,T7/V10,V13,V14 | 暫定緩和として賛成。容量だけで解決判定することには反対。 | 2026-09-25 10 GB変更と監視を承認 | Resolved in design |
| C10 | 10 GB配備後の一度限りresumeで、`CollectionDispatchCompatibilityException`直後に`Aborted (core dumped)`が発生し、10 finishでfree blocksが242,268 KiB減少した。各stageはInvocationKiB 44→0、OwnedKiB 48→4、GroupAlive=false、Alive/OutsideGroup/DeletedFds=0だった。Linux隔離probeはcore有効時にfileを生成し、`ulimit -c 0`時は生成しなかった。 | owned-root外core dumpが約30 MiB残る仮説を強く支持し、session離脱/deleted-open fd仮説は今回の経路では支持されない。core抑止は容量漏出を止め得るが、crash dump診断を失う。compatibility例外自体を直さず隠すriskもある。 | collector bootstrapの子process起動前だけcore sizeを0へ制限し、隔離testでabort時のcore不生成、通常exit/signal/元exit code/diagnostic維持、他process非影響を保証する。raw coreは保存しない。compatibility mismatchは親`20260924_collection-error-closure`のT2/T2fへ原因証拠を接続し、別taskを重複起票しない。 | AC6,AC9/T6,T8/V13,V15 | 最小core抑止を推奨。ただし根本例外の修正と分離し、診断損失を明示する。 | 2026-09-25 collector-child限定core抑止を承認 | Resolved in design |
| C11 | WeekendSubjects envelopeはdispatch/rebuild時にmutableな`resource.AttributesJson`から`weekendPriorityUntil`を作る一方、acquireは`task.MetadataJson ?? resource.AttributesJson`からleased attributesを作る。resource属性がtask作成後またはdispatch後に更新・除去されると、同一taskでenvelope GroupKeyとacquired snapshotがずれる。代表失敗はJRA/Horse/`horse-profile`/Realtime、WeekendSubjects/TaskCount 12で、provider/type/laneは一致していたため不一致境界は`weekendPriorityUntil`に限定できる。 | core抑止後も互換性例外は異常終了を続け、処理進行を阻害する。worker checkを緩めると異なるweekend cohortを同一sessionへ混在させる。 | `GetPendingDispatchesAsync`と`BuildExecutionEnvelopeAsync`のcompatibility属性をacquireと同じtask snapshot（`task.MetadataJson ?? resource.AttributesJson`）へ統一する。resource属性をA→Bまたは除去した反例で、envelope再構築とleaseがtask作成時snapshot Aを保持することを検証する。null task metadataだけはcurrent resource属性へfallbackする。worker `IsCompatible`は変更しない。 | AC10/T9、親AC2,AC4,AC7/T2,T2f/T2e | snapshot統一を最小修正として推奨。compatibility check無効化には反対。 | 親Mainが既存Approved T2/T2fの局所欠陥修正として実装を指示 | Resolved in design |

C5 の実行基盤 gate は read-only AWS 証拠で Lambda と確定し、承認済み D1–D4 を変更しない。C1 の容量対 inode は失敗時に未観測だが、共有領域を削除しない観測・所有権設計はどちらにも必要な安全条件であり、production-shaped local fixture と配備後 telemetry で検証を続ける。

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | ENOSPC を production-shaped filesystem 制限で再現し、block/inode、owned temp、直前終了形態、実行基盤 correlation の確認値と不足値を区別して記録する。 | T1 | V1: fixture + read-only CloudWatch/config/task/attempt/batch matrix | Verified |
| AC2 | success、handler exception、cancellation、Playwright launch failure、collector child 強制終了の各経路で、実行中でない app-owned invocation directory だけが最終的に削除される。 | T2 | V2-V4 automated shell/runtime tests | Verified |
| AC3 | 共有 `/tmp` の unrelated file、marker 不正、symlink、active invocation、並行 invocation は削除されず、path が空・root・範囲外なら cleanup が安全に失敗する。 | T2 | V5-V6 destructive counterexample tests in isolated temp root | Verified |
| AC4 | 既存 build/test/format、Terraform validation、collector container build、deploy guard tests が通り、安全停止・有限 retry・履歴保持・既存 CI/CD 契約が維持される。 | T3 | V7 workflow-equivalent gates | Verified |
| AC5 | 承認済み既存 CI/CD 配備後、親 Main が別途許可した operation に限り、対象または根拠ある replacement が正常終端し、独立した後続処理が進み、通常周期2回以上で ENOSPC と即時再停止がない。 | T4 | V8 deployment revision GET、V9 bounded task/attempt/batch GET | Connected |
| AC6 | 配備後観測で app-owned temp 使用量とfilesystem減少速度が bounded であることを示し、暫定容量増量を恒久原因解消と混同せず余裕を確認する。初回V10はfree-block boundに失敗しており、C9後も再検証する。 | T4,T5,T6 | V10,V13 sanitized resource telemetry | Verified |
| AC7 | direct child終了・bootstrap signal時に当該invocation専用process groupの子孫だけがboundedに終了し、削除済みopen fileのblocksが回収され、unrelated groupは生存する。 | T5 | V11 process-group/grandchild counterexamples、V12 Linux/container regression | Verified |
| AC8 | 対象collector Lambdaだけがephemeral storage 10240 MiBとなり、memory 2048 MiB、timeout 900秒、reserved concurrency 1、停止状態、診断telemetry、既存CI/CD契約が維持される。 | T7 | V14 contract test、Terraform validation、既存CI/CD、AWS/API read-only GET | Verified |
| AC9 | collector子processのabortがapp-owned root外へcore fileを残さず、通常・nonzero・signalの終了意味、段階診断、他processのcore policyを変えない。compatibility mismatchは独立した親T2/T2f原因として保持する。 | T8 | V15 production-shaped abort/core-policy反例、既存lifecycle/Linux/container regression | Verified |
| AC10 | dispatch候補とwake envelope再構築がleaseと同じtask metadata snapshotからWeekendSubjects互換性を導出し、task/outbox作成後にresource属性がA→Bまたは欠落へ変わっても同一taskのGroupKeyはAを維持する。task metadataがnullのlegacy rowだけはresource属性へfallbackし、worker互換性検査と異常終了意味は変更しない。 | T9 | V16 store/transport/persistence counterexamples、関連回帰、既存CI/CD | Verified |

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State | Routing | Audit | Result metrics |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | 実行基盤、容量/inode、残留 lifetime、correlation を確定し再現fixtureを作る（AC1） | Main | Lead | Approval、AWS read access | record evidence と test fixture。production は read-only | V1 | Lambda/config/log/API fact matrix、ENOSPC fail-closed fixture | Verified | Lead — production credential、runtime/security 判断、根本原因確定 | none | unavailable; retries 0; corrections 0; reviews 1 |
| T2 | app-owned invocation temp lifecycle と反例 test を実装する（AC2,AC3） | Main | Lead | T1 runtime gate、frozen D1-D4 | `deploy/lambda-bootstrap`、`deploy/collector-temp-lifecycle.sh`、`Dockerfile.collector-lambda`、対応 test の排他範囲。infra/API schema は変更禁止 | V2-V6 | success/failure/signal/stale/active/PID reuse/unowned/symlink/root/header tests green | Verified | Lead — path deletion safety、abrupt lifecycle、統合を同一の小変更で保持。分割review費用が上回る | none | unavailable; retries 2; corrections 3; reviews 2 |
| T3 | 正本文書と workflow-equivalent regression を統合する（AC4） | Main | Lead | T2 | `docs/11-automation-design.md`、本 record、必要時のみ既存 infra test | V7 | local、app-ci、app-deployのformat/build/test/Terraform/container/deploy guard成功 | Verified | Lead — 統合、CI/CD、最終 scope 判定 | none | unavailable; retries 1; corrections 1; reviews 2 |
| T4 | 既存 CI/CD 配備と独立 production verification を行う（AC5,AC6） | Main | Lead | T3、既存CI/CD、operation 個別許可 | 既存 CI/CD。production mutation は親 Main のみ、本taskはread-only verification | V8-V10 | revision、terminal attempt、後続、2周期、resource telemetry | Dependent | Lead — 親Mainがoperation authority、本taskがread-only verifierとして同一Main taskを分担。本番権限、安全、最終判定を主担当が保持 | none | unavailable; retries 0; corrections 0; reviews 3 |
| T5 | invocation専用process groupでPlaywright/Chromium子孫を終了し、deleted-open-file block残留を防ぐ（AC6,AC7） | Main | Lead | C7再承認 | `deploy/lambda-bootstrap`、lifecycle helper、専用test、必要なDocker packageだけ。API/DB/queue/policyは変更禁止 | V11-V12 | grandchild block回収、別group生存、signal/nonzero、Linux/container/CI成功 | Verified | Lead — process kill安全性、PID/PGID再利用、production統合は分離不能 | none | unavailable; retries 0; corrections 1; reviews 1 |
| T6 | C8の原因境界を段階telemetryとproduction-shaped反例で識別し、最小 corrective designを作る（AC6） | Main | Lead | C8再承認、親Mainの診断実行判断 | 診断設計、専用test、必要最小限のbootstrap/helper telemetry。本番mutationは既存CI/CDのみ | V13 | pre-delete/post-group/post-delete容量、PGID生存、session離脱/outside-root反例、秘密情報なし | Verified | Lead — production診断、安全境界、bootstrap/helper/testの共有write scopeと次設計判断が密結合し、分割review費用が上回る | none | unavailable; retries 1; corrections 1; reviews 2 |
| T7 | collector Lambdaのephemeral storageだけを10240 MiBへ変更し、既存CI/CDで配備・独立確認する（AC8） | Main | Lead | C9承認 | `infra/collector-lambda/main.tf`、対応contract test、本record。memory/timeout/concurrency/API/DB/workflowは変更禁止 | V14 | test、Terraform validation、CI/CD成功、AWS 10240 MiB、停止状態 | Verified | Lead — production Terraform変更と独立設定確認を小さな単一sliceとして保持 | none | unavailable; retries 0; corrections 0; reviews 1 |
| T8 | collector子processだけのcore dumpを抑止し、compatibility例外と容量漏出を分離する（AC6,AC9） | Main | Lead | C10再承認 | `deploy/lambda-bootstrap`、専用lifecycle/core test、既存app-deployのcontainer test step、正本文書、本recordのみ。例外分類/API/DB/queue/cleanup/kill範囲は変更禁止 | V15 | core enabled/disabled abort反例、通常/nonzero/signal/diagnostic回帰、Linux/container/CI/CD | Verified | Lead — crash policy、診断損失、production bootstrap安全境界の判断を保持 | none | unavailable; retries 2; corrections 2; reviews 1 |
| T9 | C11のdispatch/rebuild/acquire互換性属性をtask snapshotへ統一する（AC10、親T2/T2f） | Main | Lead | C11、親Approved scope | `CollectionPlatformStore.cs`、`CollectionPlatformStoreTests.cs`、本record、必要な正本文書のみ。worker check、public contract、DB schema/data、workflow、operationは変更禁止 | V16 | A→B、A→missing、wake再構築、null metadata fallbackが実transport/persistence経路で成功 | Verified | Lead — persistence/shared contractと同一DB fixtureの排他write、短い一体sliceで委譲・review費用が上回る | none | unavailable; retries 0; corrections 0; reviews 1 |

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
- **V13 bounded residual diagnosis:** pre-delete owned bytes、process-group termination前後の安全なgroup生存判定、post-delete free blocks/inodesを段階相関し、isolated Linux/containerでsession離脱grandchildとowned-root外writeを別々に再現する。秘密情報、command line、環境変数、全system process一覧は記録しない。
- **V14 temporary capacity deployment:** contract testでcollector Lambdaの`ephemeral_storage { size = 10240 }`と旧4096 MiB不在を固定し、Terraform fmt/validateと既存CI/CDを通す。配備後AWS GETで10240 MiB、memory 2048 MiB、timeout 900秒、reserved concurrency 1を、API GETでpaused/Running 0を独立確認する。
- **V15 collector-only core suppression:** production-shaped Linuxで同一abort helperをcore有効・collector子processだけcore size 0の双方で実行し、前者だけcore fileが生じることを確認する。normal/nonzero/signal、元exit code、段階診断、別processのcore policy、既存group/cleanup反例を回帰し、raw core内容は保存・出力しない。

## Review gates

- **Design and task-split review — Main, 2026-09-25:** AC1–AC6 を T1–T4 と V1–V10 に対応付けた。T2 だけが設計凍結後に独立委譲可能。T1 は production credential と原因判断、T3 は統合/CI、T4 は本番権限のため Lead が保持する。write scope は直列で重ならない。
- **Concern and agreement review — Main, 2026-09-25:** 容量対 inode、共有 temp の破壊、abrupt termination、並行 cleanup、correlation 欠落、配備と復旧の混同を C1–C6 で確認。利用者は限定修正と全 safety boundary を承認。C5 は承認自体では解消とせず、その後の AWS/API 照合で Lambda と確定して `Resolved in design` とした。
- **Pre-implementation review — Main, 2026-09-25:** T1 を `In progress`、Lambda runtime gate を満たした T2 を `Runnable`、T3/T4 を `Dependent` とした。T2 の排他 write scope は `deploy/lambda-bootstrap`、新規 lifecycle helper、`Dockerfile.collector-lambda`、専用 script test。共有 `/tmp`、infra size、API schema、親/禁止文書は変更禁止。test は success/failure/cancellation/stale/active/unowned/symlink/root拒否とし、path safety または外部契約変更が必要なら設計へ戻す。削除安全性と bootstrap 統合が不可分な小変更のため Main が保持する。
- **Checkpoint review — Main, 2026-09-25:** 下記 `Checkpoint review` のとおりAC1–AC3をVerified、AC4をConnectedと判定。local不可のgateは既存CIへ残した。
- **Final review — Main, 2026-09-25:** AC1–AC4とT1–T3はlocal、Linux CI、Terraform/container build、既存app-deploy、AWS revision、API停止状態の独立証拠へ追跡できる。禁止した共有`/tmp`削除、DB/履歴削除、Recovery、retry、resume、新Workflow、親record編集は実施していない。AC5/AC6とT4は、停止解除後の実invocation、後続進行、2周期、sanitized temp metricsを必要とし、本taskのoperation権限外なので`Dependent`を維持する。よって配備は成功だがrecord全体を`Implemented`にはしない。
- **Post-deploy closure review — Main, 2026-09-25:** 一度の明示resumeでV10を実測し、owned cleanupは成功したがfree blocksのboundは失敗した。テスト成功やdirectory countだけでAC6をVerifiedにしない。C7を`Open decision`、AC7/T5/V11-V12を追加し、Statusを`Proposed`へ戻した。process-group killは誤停止リスクを持つため既存承認から自動拡張せず、再承認前にproduction codeを変更しない。
- **Amendment pre-implementation review — Main, 2026-09-25:** 利用者承認によりC7を解消しT5を`In progress`とした。write scopeはbootstrap、必要なhelper/Docker package、専用test、本recordに限定する。実装は`setsid`で生成したinvocation専用PGIDをbootstrapが保持し、直接child終了後とbootstrap signal時だけ同groupをTERM、bounded wait、KILLする。空/非数/1/self/PGID不一致はfail closed、別groupは保持する。deleted-open 30 MiB grandchild、別group、success/nonzero/signal/header、既存lifecycle回帰を必須とする。process terminationの安全判断とbootstrap/testが密結合した短いsliceであり、委譲review費用が上回るためLeadが保持する。

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

2026-09-25 amendment approval後の local evidence:

- bootstrapは`setsid` handshake中にPIDと`/proc/{pid}/stat`のPGID一致を確認してからcollectorを開始する。空・非数・1・bootstrap自身と同group・unisolated launcherはfail-closed。直接child終了後とHUP/INT/TERMで当該PGIDだけをTERM、最大1秒wait、残存時KILLする。
- WSL Linuxのproduction-shaped fixtureで、grandchildが30 MiB fileをopen後unlinkしてdirect child終了後も生存する経路を実行。bootstrap終了後はgrandchild停止、unrelated setsid group生存、owned directory 0、filesystem差4 KiBだった。
- `tests/scripts/test-collector-temp-lifecycle.ps1`: 既存caseにunisolated session command拒否を追加し全case成功。Linux実`setsid`環境ではdeleted-open 30 MiB回収とunrelated group生存caseも実行する。
- `tests/scripts/test-deploy-pipeline-state.ps1`: 既存17 case成功。
- `dotnet format HorseRacingPrediction.sln --no-restore --verify-no-changes`: 成功。Release buildは警告0・error 0。非External testsは1217 passed、1 skipped、0 failed。脆弱packageなし。
- `codegraph sync .`はindex未初期化で失敗。ユーザー判断でindexを作らず、graph-based完了証拠を主張しない。
- PR #76 はmainへmerge commit `c8e7be21fd248349f3c68286232fb64e7910780b` として統合された。app-ci run `36094616739` は全gate成功。既存app-deploy run `36095032294` はverify、image build/push、collector Lambda適用、API deploy/health、migration queueの全jobが成功した。verifyではLinux実`setsid`のprocess-group lifecycle test、collector container build、Terraform validationを実行した。
- 2026-09-25 13:44 JSTのAWS read-only確認でLambdaは`Active`、`LastUpdateStatus=Successful`、revision `820532fe-0ef1-49a2-aa8f-f24c87927f67`、resolved image digest `sha256:e8def2c4c36abbb69572eb1714894ad12b095feb0ed5a5681f1b0a0318e1cff4`。4096 MiB、2048 MiB、900秒は維持された。
- 配備後のproduction API read-only GETは`isPaused=true`、pause reason `Safety hold: post-deploy temp filesystem free blocks continue decreasing after owned cleanup; ENOSPC AC6 verification failed. Preserve evidence; no automatic resume.`、updatedAt `2026-09-25T03:52:56.2680851+09:00`、Running 0。停止を解除していないため新revisionの実invocation telemetryはなく、AC5/AC6は未完了のまま維持する。

2026-09-25 process-group amendment配備後のoperation evidence:

- 利用者指示を受けた親Mainが13:49:52 JSTに一度だけresumeした。新revisionのsame warm stream `868d1ba3fc20418aa6547d60c55c23cb`で13:49:55 JSTから実invocationを観測した。
- 最初の長時間finishは176.8秒で`FreeKiB 4,194,280 -> 4,194,308`と回復した。続く短時間finish 9件（約0.77–1.07秒）では各回約30 MiBずつ減少し、`FreeKiB=3,921,304`まで累計273,004 KiB減少した。各finishは`OwnedKiB=4`、`InvocationDirectories=0`、free inodeは開始前付近へ回復した。
- 明示した再停止条件に該当したため直ちに親Mainへalertし、親Mainが13:54:24 JSTに`Safety hold: process-group amendment still loses approximately 30 MiB per short invocation; AC6 failed again. Preserve evidence; no automatic resume.`でpauseした。pause直後はRunning 10、14:10:01 JSTにRunning 0へ自然収束した。ENOSPCは観測中に再発しなかった。
- pause後に完了した次の長時間finishは約97秒で`FreeKiB=3,921,304`のまま増減なし。短時間経路との相関は再現したが、production `/proc`、fd、削除前owned sizeは未取得であり、PGID外子孫またはoutside-root writeを事実とは扱わない。
- CloudWatchのLambda lifecycleはSTART/END/REPORT各11件、bootstrap temp lifecycleはfinish 11件。temp lifecycleの12件目のstartは最後のfinish直後にruntime API `/invocation/next`を待つbootstrap状態で、新しいLambda invocationまたは未完了collectorではない。11 REPORTは長時間2件（176.8秒、96.2秒）と短時間9件（約0.77–1.07秒）で、後者だけがfree-block減少と一対一に対応した。
- 本taskはresume、pause、retry、Recovery、cleanup、environment recycle、履歴/DB mutationを実施していない。resume/pauseは親Mainが保持するoperation authorityで実施した。

## Checkpoint review

2026-09-25 Main: AC1はproduction API/AWS相関と不足値、AC2/AC3は独立temp rootの反例群でVerified。実装は固定owned root直下だけを対象とし、marker、direct-child、non-symlink、PID+process start leaseを全て検証する。通常・非zero・signalではcurrent childだけを削除し、hard kill残留は次bootstrapでdead lease確認後に回収する。共有`/tmp`列挙、容量増量、failure policy変更、DB操作はdiffにない。初回local test失敗2件（未設定TMPDIR assertion、fake curl URL抽出）、PID reuse review finding、初回Linux CIの非実行属性test defectを修正し全群を再実行した。app-ci再実行とapp-deployでTerraform/containerを含む全gateが成功したためAC4/T3をVerifiedとする。T4とAC5/AC6は配備済みだが、親operation後の実行観測待ちで未完了。

2026-09-25 amendment checkpoint — Main: AC6/AC7/T5を一群でreview。diffは承認済みbootstrap、Docker dependency、専用test、正本文書、recordだけ。Linux反例はdeleted-open block回収と別group生存を同時に通し、unisolated launcherはcollector実行前に拒否した。最初のLinux fixtureでdash builtin `kill`が`--`を受理せずgroupが残る欠陥を検出し、portableな`kill -TERM -PGID`へ修正して再検証した。PGIDはhandshakeでcollector開始前に確定し、group memberが残る間はLinuxがIDを再利用しない。local V11は成功、V12のLinux PowerShell/container/CIは未完了のためT5は`In progress`、AC7は`Connected`を維持する。

2026-09-25 amendment final review — Main: AC7/T5はlocal反例、WSL production-shaped fixture、Linux CIの実`setsid` test、container build、Terraform validation、app-ci、既存app-deploy、AWS revisionへ追跡できるため`Verified`とする。配備後もpipelineは同じsafety holdでpaused、Running 0であり、禁止したresume、retry、Recovery、environment recycle、共有`/tmp` cleanup、DB/履歴変更は実施していない。AC5/AC6/T4は新revisionで20件以上のfinishと通常周期2回、sanitized free-block/inode telemetryを必要とするが、これは親Main所有の別途operation判断に依存する。承認済みcode/deploy sliceは完了したが、record全体は`Implemented`にせず`Approved`を維持する。

2026-09-25 post-amendment operation checkpoint — Main: 親Mainの一度限りresumeでV10を再実行したが、10 finish時点で短時間経路のfree-block減少が明確に再現したため20 finish/2周期を待たず安全条件に従い再pauseした。AC7のisolated process-group契約はVerifiedのままだが、production AC6を満たすとの仮説は反証された。C8を`Open decision`、T6/V13を追加しStatusを`Proposed`へ戻す。実runtime imageはAWS revision/digestと新bootstrap由来のmetricsで接続済みだが、現観測だけでprocess escape、outside-root write、open fdを断定しない。次の実装・配備はbounded診断設計の再承認前に行わない。

2026-09-25 C8 pre-implementation review — Main: 利用者承認によりC8を`Resolved in design`、T6を`In progress`、Statusを`Approved`とした。write scopeは`deploy/lambda-bootstrap`、`deploy/collector-temp-lifecycle.sh`、既存lifecycle test、必要な正本文書、本recordに限定する。削除前、group termination前後、削除後の段階値は整数・boolean/countだけを出し、path、PID/PGID実値、command line、環境変数、secret、全process一覧を出さない。isolated testは同group残留、session離脱、owned-root外writeを独立させ、値がどの仮説を識別できるかを検証する。既存normal/nonzero/signal/header/unisolated/group安全性を回帰する。診断が全process走査、外部契約、kill範囲、容量、cleanup範囲を変える必要が出た場合は設計へ戻す。安全境界と共有shell/testが密結合した短いsliceのためLeadが保持する。

2026-09-25 C8 local checkpoint — Main: 削除前後とgroup終了前後の`InvocationKiB/OwnedKiB/FreeKiB/FreeInodes/GroupAlive`、collector稼働中に`/proc/{known-parent}/task/{known-parent}/children`だけから取得したbounded descendantの`Captured/Alive/OutsideGroup/DeletedFds` countを実装した。PID/PGID値、path、fd target、command line、環境変数は出力しない。local Git Bashの既存11 caseとshell syntaxは成功。実`setsid`を要する同group、session離脱、outside-root反例はLinux CI待ちであり、T6は`In progress`を維持する。local Docker engineは未起動のためcontainer gateもCIへ残す。

同checkpointの関連回帰はdeploy guard 17件、`dotnet format --verify-no-changes`、Release build警告0/error 0、非External 1217 passed / 1 skipped / 0 failed、脆弱packageなし。`codegraph sync .`はindex未初期化で失敗したためgraph証拠を主張せず、ユーザー判断なしにindexを作成しない。

初回Linux CI run `36098542222` はsession離脱反例で失敗した。追跡file自身のfilesystem blockを考慮せず`InvocationKiB=4`へ固定したtest期待と、collector親がmonitor捕捉前に終了し得るfixture競合が原因候補だった。production telemetry契約は変えず、fixtureへ0.2秒の捕捉窓を追加し、owned sizeを`<1024 KiB`のbounded条件へ修正した。local lifecycleとformatを再実行して成功し、Linux CI再検証待ち。

2026-09-25 C8 deployment checkpoint — Main: 修正後のapp-ci run `36098675722` はLinux実`setsid`反例を含む全gateが成功し、PR #79をmainへmerge commit `308bc605109dd7656ca67169ddce723a06c1e2d6`として統合した。app-deploy run `36099037785` はLinux V13、全build/test、Terraform validation、collector container build/push、collector Lambda、API health、migration queueを含む全jobが成功した。Lambdaは`LastModified=2026-09-25T14:41:58+09:00`、revision `107b53fc-bd7d-48d6-bee1-5904fbc59709`、image digest `sha256:97819982db662fec65b0fd0ac0ed5ba8e8b5c633f562a070cba23b20e983ff14`、Active/Successful、ephemeral storage 4096 MiB、memory 2048 MiB、timeout 900秒である。

V13の隔離Linux反例は、同groupのdeleted-open fileを回収できること、session離脱では`OutsideGroup>0`かつ`DeletedFds>0`となること、owned-root外writeだけでは`Alive=0`、`OutsideGroup=0`、`DeletedFds=0`となることを独立に確認した。診断は既知親のchildren関係だけを辿るbounded集約であり、全`/proc`走査、PID/path/fd target/command/env出力、kill・cleanup範囲拡大はdiffにない。production APIのread-only確認では`isPaused=true`、reasonは`Safety hold: process-group amendment still loses approximately 30 MiB per short invocation; AC6 failed again. Preserve evidence; no automatic resume.`、updatedAt `2026-09-25T13:54:24.3218382+09:00`、Running 0。本taskは診断revisionを実行していないため、production値による原因識別と最小root-cause修正案は親Mainの一度限りの安全判断に依存する。T6を`Dependent`、AC6/V13を`Connected`、recordを`Approved`のまま維持する。

2026-09-25 C9 pre-implementation review — Main: AWS公式仕様とTerraform provider仕様でephemeral storageの上限が10240 MiBであることを確認した。変更は`infra/collector-lambda/main.tf`の4096→10240、同resource blockを固定するcontract test、本recordだけに限定する。memory、timeout、reserved concurrency、diagnostic bootstrap、API/DB、workflow、運用状態を変更しない。容量増量は約30 MiB/短時間finishの減少を止めず、枯渇までの時間を延ばすだけというmaterial concernをC9へ明記したうえで、利用者の暫定緩和判断に従う。production resumeと監視は親Main所有であり、本taskは配備とread-only確認までとする。単一Terraform値と対応testの小変更で、分割・委譲の調整費用が上回るためLeadが保持する。

2026-09-25 C9 local checkpoint — Main: collector Lambda resourceのephemeral storageを4096から10240 MiBへ変更し、contract testで10240 MiBの存在と旧4096 MiBの不在を固定した。対象test 11件、format verify、deploy guard、change record validator、agent audit、`git diff --check`は成功。local環境にTerraform CLIがないためfmt/validateは実行不能で、既存app-ci/app-deployのLinux gateへ残す。変更差分にmemory、timeout、reserved concurrency、diagnostic telemetry、workflow、API/DB、operation mutationはない。

2026-09-25 C9 deployment checkpoint — Main: PR #81をmainへmerge commit `f00bcb65b22e8d4acec70aa8a55e3cde16e5d653`として統合した。app-ci run `36100736007` は4分50秒で成功。app-deploy run `36101108202` はverify、Terraform validation、collector container、build/push、collector infrastructure apply、API health、migration queueを含む全jobが成功した。AWS read-only GETではephemeral storage 10240 MiB、memory 2048 MiB、timeout 900秒、reserved concurrency 1、Active/Successful、LastModified `2026-09-25T15:11:13+09:00`、revision `90518f11-f9fb-438b-98c5-07c5954da140`、image digest `sha256:52bb2d599e1e2ef5c51fa69257f7f2c95f46101a123313806e391eef85605520`。production API GETは同じsafety holdで`isPaused=true`、updatedAt `2026-09-25T13:54:24.3218382+09:00`、Running 0。本taskはresume、retry、Recovery、cleanup、recycleを実行していない。AC8/T7/V14をVerifiedとし、恒久原因調査AC6/T6は未完了のまま維持する。

2026-09-25 C10 safety checkpoint — Main: 親Mainが15:18:07 JSTに一度だけresumeした後、同一warm streamで10 finishを観測した。最初から最後まで93.138秒、FreeKiBは10,455,092から10,212,824へ242,268 KiB減少し、最初の短時間連続失敗では約1–2秒ごとに約30,296 KiB減少した。各失敗は`CollectionDispatchCompatibilityException`、`Aborted (core dumped)`、pre/post-groupの`Captured=1 Alive=0 OutsideGroup=0 DeletedFds=0`、pre-delete `InvocationKiB=44 OwnedKiB=48`、post-delete `InvocationKiB=0 OwnedKiB=4`と一致した。急速区間の単純投影が60分以内の枯渇条件へ達したため即時alertし、親Mainが15:19:43 JSTにpauseした。pause後は同理由のpausedを維持し、Running 9から15:35:02 JSTに0へ自然収束した。pause後のfinishは既に実行中だった1件だけで、ENOSPCは再発していない。

同checkpointのWSL隔離probeは`core_pattern=core`で、core有効の`os.abort()`が1 file / 3340 KiB、同一process shellで`ulimit -c 0`後のabortが0 file / 0 KiBだった。これはcore抑止の機構を実証するが、productionの約30 MiB file自体は列挙・取得していないため、core dumpを最終factとはせず強い仮説とする。代表persisted taskはJRA/Horse/`horse-profile`/Realtime、attempt 5、Runningで、runtime envelopeはWeekendSubjects/TaskCount 12だった。`IsCompatible`はWeekendSubjectsでacquired resourceの`weekendPriorityUntil`がenvelope GroupKeyと完全一致することを要求するため、このtaskはその属性が欠落または不一致だったと境界づけられる。dispatch生成時点との差が生じた理由は未確定で、親record T2/T2fの既存dispatch原因へ接続する。

2026-09-25 C10 pre-implementation review — Main: 利用者承認によりC10を`Resolved in design`、T8を`In progress`、AC9を`Connected`、Statusを`Approved`とした。実装はinvocation専用session shellがcollectorを`exec`する直前のcore size 0だけに限定するため、そのcollectorと子孫に継承され、bootstrap親・unrelated processへ影響しない。testはcore有効controlとcollector abortを同じ隔離directoryで比較し、collector側のcore不生成、abort由来exit status、段階診断、cleanup、親shell policy保持を確認する。既存normal/nonzero/signal/header/group/session-escape/outside-root反例を再実行する。互換性チェック、例外分類、error response、cleanup/kill範囲、API/DB/queueは変更せず、新Workflowを作成しない。

2026-09-25 C10 local checkpoint — Main: invocation session shellのcollector `exec`直前へ`ulimit -c 0`を追加し、専用shell testを追加した。WSL Linuxではcollector abortがstatus 134を維持し、子process内core limit 0、core file 0、post-group/post-delete/descendant診断、owned cleanupを確認した。同じ親shellからcore有効controlを実行するとcore fileが生成され、親shellのcore policyは前後不変だった。Git Bashの既存lifecycle 11件、deploy guard 17件、shell syntaxは成功。既存app-deployのcontainer build直後に同じ専用testをimage内bootstrap/helperへ実行するstepを追加し、新Workflowは作成していない。互換性チェック、例外分類、error response、API/DB/queue、cleanup/kill範囲に変更はない。

同checkpointの関連回帰はformat verify、Release build警告0/error 0、非External 1217 passed / 1 skipped / 0 failed。local Docker engineは未起動のためimage内V15は既存app-deployのLinux container gateへ残す。raw coreは隔離probeのcontrol判定直後に削除し、内容を読取・保存・出力していない。

2026-09-25 C10 final review — Main: 初回app-ci run `36113341708` と再実行`36113442105`は、source checkoutのbootstrapに実行属性を要求したfixture defectで専用test開始前に失敗した。production imageではchmodされ、testは`sh bootstrap`で起動するため、readable contractへ修正して秘密情報なしの判定値を追加した。修正後app-ci run `36113526075`はLinux実abortを含む全gateが4分41秒で成功。PR #84をmainへmerge commit `3aa99b2e0a2e82aa3d9ec6129b26474173c0f418`として統合した。

app-deploy run `36113974265`はverify、Linux V15、Terraform validation、collector container内V15、build/push、collector infrastructure apply、API health、migration queueを含む全jobが成功した。AWS read-only GETはephemeral storage 10240 MiB、memory 2048 MiB、timeout 900秒、Active/Successful、LastModified `2026-09-25T17:46:16+09:00`、revision `85e4bb5b-4fed-4758-b8ff-5c9b3ab11d8e`、image digest `sha256:24dc6ba711ce0ee26452c887b9e4b5bdb2d2d08bd8554354cfb87ec19c127cb6`。production APIは同じsafety holdでpaused、Running 0。本taskはresume、retry、Recovery、cleanup、recycle、DB/履歴操作を実施していない。AC9/T8/V15をVerifiedとするが、productionでcore漏出停止を実測しておらず、compatibility mismatchとAC6/T6は未完了のまま維持する。

2026-09-25 C11 read-only boundary — Main: dispatch候補、wake時envelope再構築、acquireの3経路を照合した。前二者はcurrent resource attributes、acquireだけはtask metadata snapshot優先であり、C11が同じtaskのcompatibilityを時間差で変える唯一の確認済み属性源である。代表taskでprovider JRA、type Horse、lane Realtimeは一致し、runtime GroupKindはWeekendSubjectsだったため、Workerが拒否し得る残差は`weekendPriorityUntil`の欠落またはGroupKey不一致に限定される。production DB/SQS/message/coreは列挙せず、既存API/CloudWatchとsourceだけで境界づけた。親recordのT2はpersistence/shared contractと必要原因修正をMain所有、T2fは未知/残存原因の閉鎖、T2eは独立反例・統合gateを既に含むため、この最小snapshot修正は既存親scopeの具体化であり、新task・新権限として扱わない。

2026-09-25 C11 pre-implementation review — Main: 親Mainは利用者の既存バグ修正指示と親Approved T2/T2fの範囲に基づき、確認済み局所欠陥のexact sliceを継続実装するよう指示した。T9を`In progress`、AC10を`Connected`とする。frozen decisionは`GetPendingDispatchesAsync`と`BuildExecutionEnvelopeAsync`が`AcquireAsync`と同じ`task.MetadataJson ?? resource.AttributesJson`を使うこと、null metadataのlegacy rowだけresourceへfallbackすること、worker `IsCompatible`を弱めず例外をsuccess化しないことである。V16はtask/outbox作成後のresource属性A→B、A→missing、同一wakeの再構築、null metadata fallbackを実SQLite persistenceとdispatch/acquire transportで確認する。public contract、schema/data migration、workflow、DB補正、cleanup、retry、Recovery、resume、production mutationは範囲外。変更がこれらへ拡大する場合は親Mainへ戻す。共有storeと同一fixtureの短い一体sliceで排他writeが必要なためLeadが保持し、分割・委譲の統合費用が上回る。金曜freshness 4区分は既存snapshotのbounded read-only集約案を記録するだけで、今回API拡張やDB queryを実施しない。

2026-09-25 C11 local checkpoint — Main: `GetPendingDispatchesAsync`と`BuildExecutionEnvelopeAsync`を`DeserializeTaskMetadata(task.MetadataJson ?? resource.AttributesJson)`へ統一した。実SQLite fixtureでtask/outbox作成後のresource属性A→BとA→missingの双方について、pending属性、初回wake envelope、同じwakeの再構築、task leaseが作成時snapshot Aを維持した。legacy null metadataではcurrent resource属性Bへfallbackした。worker `IsCompatible`、public contract、schema/data、failure semantics、workflow、operationに差分はない。専用3件、format verify、Release build（warning/error 0）、非External 1220 passed / 1 skipped、deploy guard 17件、脆弱packageなし、agent audit validator、`git diff --check`が成功した。`.codegraph` indexは存在せず、ユーザー判断なしに作成していない。AC10/T9のlocal evidenceは満たすが、既存CI/CD統合と配備証拠まで`Connected`/`In progress`を維持する。

2026-09-25 C11 final review — Main: PR #87はapp-ci run `36119188079`（4分55秒）成功後、merge commit `e99e95703e1014752313a9d4082f623d9d9ffbc2`としてmainへ統合した。app-deploy run `36119666506`はLinux lifecycle、build/test、空SQLite migration、Terraform validation、collector container、image build/push、Lambda apply、API health、既存migration queueを含む全jobが成功した。AWS read-only確認はActive/Successful、LastModified `2026-09-25T18:48:03+09:00`、revision `fd9f0902-b4f8-4a70-bb61-b18de175d842`、digest `sha256:695fb958c4831ae699b8f78172aa7975fcd5837fbdd2f6e60ad5162544dbb4dd`、ephemeral storage 10240 MiB、memory 2048 MiB、timeout 900秒。deploy guardはpipeline pausedとRunning 0を確認してから配備し、最後に`Collection remains paused; recovery requires an explicit operator decision.`を記録した。AC10/T9を`Verified`とする。production実処理での例外消失、core漏出停止、容量bound、後続進行はresumeを伴うためAC5/AC6/T4/T6のまま親Main判断へ残し、本taskはresume、retry、Recovery、cleanup、recycle、DB補正を実施していない。focused self-auditはfrozen snapshot規則、null fallback、worker check非変更、実transport/persistence反例、scope、CI/CD、停止維持を再照合し、未解決のAC10 gapまたはskill不足を認めなかった。

2026-09-25 post-resume production checkpoint — 親Mainが約18:54 JSTにguarded one-time resumeを実施し、本taskはCloudWatch/AWSをread-only観測した。新revisionで39 invocation finish、39 JRA session、155 task runtime completionを確認した。sessionはRaceDay `Succeeded/TaskCount=7`、修正対象WeekendSubjects `Succeeded/TaskCount=6`、独立したDefinition後続37件を含み、全sessionがSucceeded。task結果はSucceeded 7、ResourceNotYetAvailable 7、ResourceNotFound 125、TransientFailure 16であり、all-session successを全task/data successとは扱わない。全startは`OwnedKiB=32 FreeKiB=10486624 FreeInodes=2848014 InvocationDirectories=1`、全finishは`OwnedKiB=4 FreeKiB=10486652 FreeInodes=2848021 InvocationDirectories=0`で、従来の短時間finishごとの約30 MiB減少は0、finishごとに開始比28 KiB回復した。`CollectionDispatchCompatibilityException`、`Aborted (core dumped)`、ENOSPCは0件。AC6/T6/V13は`Verified`。一方、承認済み実行仕様§6の「通常周期」は当日有効なdispatcher/scanner周期であり、invocation数、RaceDay/WeekendSubjects/Definition group、独立successor sessionでは代替できない。本taskは周期設定、周期開始/終了時刻、cycle ID、2周期分のAPI/task/attempt対応を取得していないため、AC5/T4/V9は`Connected`/`Dependent`へ戻す。本taskはpause、resume、retry、Recovery、cleanup、recycle、DB操作を実施していない。freshness 4区分は本証拠と独立で未検証のまま保持する。

2026-09-25 final review correction — Main: PR #89時点のfinal reviewは39 invocation/sessionを通常周期2回の証拠へ誤って読み替えていた。実行仕様§6は「当日有効なdispatcher/scanner周期の2回以上」を要求し、最初に周期・due・通常実行時間から観測上限を記録し、task/attempt/APIと対応させる。CloudWatch Lambda sessionにはその周期ID/境界がなく、親Mainの19:03:47 JST時点のunpaused/Running 1も2周期を証明しない。よって証拠のない完了主張を撤回し、Statusを`Approved`、AC5を`Connected`、T4を`Dependent`へ戻す。AC6/T6の39 finish容量bound、C11互換性修正、155 outcome内訳は維持する。新taskや基準緩和は行わず、既存T4/V9で実周期の設定・開始終了時刻・ID・API evidenceを取得した後だけ再判定する。

2026-09-25 T4/V9 cycle-evidence audit — Main: production構成は`COLLECTION_QUEUE_ENABLED=true`、`CollectionQueue.DispatchIntervalSeconds=1`で`CollectionPlatformOutboxDispatcher`を有効化する。`CollectionScheduleService`は固定1分、`CollectionPlanningScheduler`も固定1分で走り、後者は3時間単位のplanning bucketを作る。これらが実行仕様§6でいう当日有効なdispatcher/scannerの候補である。しかしdispatcherは成功時にcycle開始/終了、cycle ID、pending/due入力件数、選択/送信件数を記録せず、schedule/planning scannerも成功周期を記録しない。既存`GET /api/admin/collection/monitoring/findings`のstore snapshotはcutoff、active tasks、dispatch timestamps、flow集計を返すが、周期境界または空の成功周期を保存・返さない。Lambda CloudWatchの39 sessionにはAPI側周期とのcorrelation IDがない。従って既存GET/集約ログ/コードだけから2つの区別可能な周期の開始終了時刻、due入力、task/attempt対応を復元できず、時間経過や追加pollでは解消しない。AC5/T4は`Connected`/`Dependent`を維持する。

最小追加案（未承認・未実装）: 既存dispatcher、schedule scanner、planning scannerの各successful cycleへ、秘密情報なしの`CycleId`、`Kind`、`StartedAt`、`FinishedAt`、`PipelinePaused`、`DueCount`、`SelectedCount`、`CreatedTaskCount`、`DispatchedEnvelopeCount`、`ErrorCount`を構造化記録し、task/outboxへcycle correlationを保持するか、boundedな直近cycle read model/GETで2周期を照合可能にする。空周期も成功証拠として残す。public contractまたは永続化を追加するため本taskのread-only権限では実装せず、親Mainへ設計/承認判断を戻す。新task、周期設定変更、production mutation、retryは行わない。

2026-09-25 dispatch-order bounded audit — 19:49 JSTの独立monitoringでは`DispatchOrderViolation=0`だったが、20:20:18 JSTに5件、20:21:26.3712109 cutoff / 20:21:27.8535766 completedの再GETでも同fingerprint `dde5c066c6730424`の5件を確認した。全件はlane Normal、compatibility key `JRA|Definition|trainer-profile|2026-09-13|Normal`、waiting priority 70、bypass count 3、first bypass `2026-09-25T00:21:56.1236628+09:00`、同じsample envelope `39e16612-e5ee-4c61-a814-0ec118bc9ea2` / `b69fd924-e6db-4eaf-882c-6d81ee6b998d` / `7de00262-6d65-4387-a7ac-dce63f682085`。waiting taskは`21e3a1bc-bfc1-4320-ab1c-bf5f87da810e`（candidate since 9/19 23:50:26）、`2e0c68a7-96d9-4139-b37a-fa94e8f9707e`（9/20 00:02:04）、`78e1c98c-d967-471a-9502-28ef3c254f96`（00:02:26）、`082b65c2-4aa6-44ea-aadc-176a68e0d130`（00:03:01）、`b7a492dc-e87c-4f44-900a-0c8ae40cfa39`（00:20:00）。最初のbypassとsample envelopeはC11配備前で、新規post-fix bypassの証拠ではない。trainer-profileのDefinition compatibilityは`weekendPriorityUntil`を使わないため、C11のWeekendSubjects snapshot不一致再発とも分類しない。

監視predicateは現在のactive taskと現在の未dispatch outboxから`DispatchCandidateSince`を作り、24時間内の過去dispatchと比較する。lane/key/available/created/effective priorityは照合するが、各過去dispatch時点でwaiting taskが別envelopeへ予約済み、Running、RetryWaiting、または他の理由で非eligibleだった区間を履歴として保持しない。現在のreservationが解けてcandidateへ戻ると同じ古い3 dispatchが再計数され得るため、19:49の0件から20:20の5件への変化だけでは新しい実違反を示さない。逆にpredicate上は同一key/laneで低いeffective priorityの3 decisionが存在するため、実違反も既存証拠だけでは排除できない。結論は「historical bypass candidate、実違反対diagnostic eligibility履歴不足は未確定」とし、親T2/T2fへ接続する。

最小案（未承認・未実装）: findingへ各bypass representative task ID、base/effective priority、dispatch generation、dispatch時点のwaiting task status/reservation/envelope eligibilityを対応付ける。実時点eligibilityを永続化していない場合は、outbox reservation/lease/status transitionのbounded historyまたはdispatch decision recordへ候補集合と選択理由を保存し、monitorは同じtask snapshot compatibility helperを共有する。現在属性からのhistorical再計算だけでProgramBug確定にしない。新taskを作らず既存親T2/T2fで設計・反例・承認を扱い、本auditはコード、配備、production mutationを行わない。

同時刻の既存read-only freshness sourceは`GET /api/admin/collection/monitoring/findings`で、内部的に`GetMonitoringSnapshotAsync`のrace artifact snapshotとdomain race read modelを突き合わせる。金曜18:00以降にdiscovery 0なら`WeekendDiscoveryCoverageUnknown`、欠落時は`discovered/cardCurrent/missing`または`due/resultCurrent/missing`をevidenceへ出す。今回のGETはfreshness finding 0件だったが、healthy時のexpected/persisted/officialUnavailable/unknown全件数をresponseへ出さず、ResultのCurrentとUnavailableを合算するため、0件だけでは要求された4区分をVerifiedにできない。正確な4区分は既存store snapshotを使うread-only集約または承認済みDB queryが必要で、推測しない。

2026-09-25 21:27 monitoring bounded diff — 親Mainが実施した独立read-only GETはcutoff `2026-09-25T21:27:19.3088078+09:00`、completedAt `2026-09-25T21:27:20.9515107+09:00`、`truncated=true`で、27 findingsはfailure group 10、`DispatchOrderViolation` 1、stalled 16に全て分解された。runner actionable 25との差2件は`SubjectNotIdentified`のKnownHistoricalJobError群であり、freshness findingではない。従って21時閾値通過後の新規・重大freshness findingはこのsnapshotに存在しないが、truncated snapshotであること、healthy時の4区分集計を返さないこと、`FridayCriticalHour=21`がfreshness判定で参照されていないことから、finding 0をexpected/persisted/officialUnavailable/unknownの検証成功とは扱わない。

1件の`DispatchOrderViolation`はBackground、`JRA|Definition|trainer-profile|2026-09-13|Background`、waiting task `5cc010f3-cc28-449d-9954-629354a53b84`、priority 40、bypass count 4、first bypass `2026-09-24T23:47:52.0519905+09:00`であり、first bypassと4 sample envelopeはいずれもC11配備前である。新しいpost-fix bypassまたはWeekendSubjects compatibility再発とは推定せず、既存T2/T2fのhistorical eligibility不足へ接続する。stalledは20:54比で14から16へ増え、提示された2件はいずれも古いReady/Background trainer task（oldest 9/18および9/20）で、一方は同じwaiting taskであるため、増分だけで新規原因を確定しない。

現在性のある実障害証拠は、horse-profile `SubjectNotIdentified` 315件（last 21:25:08）とtrainer-profile `SubjectResourceMissing` 90件（last 21:24:23）である。前者は既存classifier上KnownHistoricalJobError、後者はUnknownHistoricalJobErrorで、実装上は取得プロフィールをAPI-authoritative subject resourceへ保存した際のHTTP 404をisolated permanent failureへ変換したものだった。これはC11互換性修正とは別系統であり、既存T2/T2fの代表task/attempt、resource ID、取得元identity、API正規resource対応をread-onlyで照合するのが次のbounded investigationである。自動補正、retry、Recovery、DB query、コード・配備・operation mutationは本監査で行わない。

freshness 4区分の最小既存snapshot集計案（未承認・未実装）: 同一cutoffの`GetMonitoringSnapshotAsync` race artifact snapshotとdomain race read modelを用い、公式に期待されるrace集合を母集団として、`expected`、永続化済み`Current`、`officialUnavailable`、それ以外の`unknown/missing`を相互排他的に集計する。Resultの`Current`と`Unavailable`を合算せず、card/result別の件数、cutoff、source completeness、truncated状態を返す。既存read-only monitoring response/reportのbounded extensionまたは承認済みbounded queryとして親T3で扱い、今回のsnapshotから値を推定しない。

## Approval boundary and next action

利用者は初回の限定修正、process-group amendment、C8 bounded診断、C9の10 GB暫定緩和、C10のcollector-child限定core抑止を承認した。T6/V13の診断実装・隔離検証・既存CI/CD配備とT7/V14は完了した。C10/T8/V15は異常終了を成功へ変えず、raw coreによる容量消費だけを抑止する。

T7/V14、C10/T8/V15、C11/AC10/T9/V16とproduction AC6の実装・配備・観測は完了した。AC5/T4は既存dispatcher/scanner周期2回の独立証拠待ち。金曜freshness 4区分は既存sourceの0 findingだけで完了扱いにせず、既存snapshotのbounded read-only集約を親scopeで扱う。本taskはRecovery、retry、resume、environment recycle、本番cleanupを行わない。

監査時点でproductionは親Mainのguarded resume後に進行し、互換性例外・core abort・ENOSPC・容量減少を39 finishで認めない。次は既存T4/V9で通常dispatcher/scanner周期2回を時刻・ID・API evidenceへ対応付け、親scopeでfreshness 4区分bounded read-only集約を行う。目的外の未コミットファイルはない。

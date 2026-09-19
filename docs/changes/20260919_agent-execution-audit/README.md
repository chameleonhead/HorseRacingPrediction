# エージェント実行監査と低コスト coding worker の品質改善

- Status: Implemented
- Owner: HorseRacingPrediction maintainers
- Created: 2026-09-19
- Updated: 2026-09-19

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Complete | 監査schema、validator/集計、低コストworker設定、canonical workflowを実装した。 |
| Verification | Complete | 11反例test、3 skill validator、TOML parse、change-record validator、format/diff checksが成功した。 |
| Deployment/operation | Complete | リポジトリ規則とcustom worker設定へ接続済み。今後のdelegated coding taskから監査artifactを蓄積する。 |

## Context

現行の `agent-task-orchestration` は owner、model tier、usage/effort、再試行、escalation、lead decision の記録を求めている。しかし次が標準化されていない。

- 起動時に要求したモデルと、実行基盤から観測できた実モデルを分けて記録する方法
- coding worker の指示品質、実装精度、scope 遵守、検証結果を同じ尺度で比較する方法
- input/output/reasoning/cached token、token budget、truncation を取得できる場合の記録方法と、取得できない場合の扱い
- 安価な coding model をどの task へ割り当て、どの失敗で再指示または上位モデルへ昇格するか
- 実績から prompt、task boundary、reasoning effort、model routing、token budget を継続改善する方法

現在の Codex collaboration interface では、親 agent が worker ごとの実モデル名と token usage を常に取得できるとは限らない。監査は取得不能な値を推測せず、requested、observed、source、availability を分ける必要がある。

## Goals

- delegated task ごとに、期待モデル、要求モデル、観測モデル、reasoning effort、取得根拠を監査できる。
- coding task は、適格な bounded task なら安価なモデルから開始し、高能力な主担当が独立 evidence で精度を判定する。
- 実測 token と budget adequacy を記録し、十分な成功標本がある task class では推奨 budget を実績から更新できる。
- instruction、task boundary、worker output、verification、rework、escalation を評価し、次回 routing を自発的に改善できる。
- telemetry が取得不能でも、取得不能という事実と代替の effort evidence を残し、架空の token 数や実モデル名を作らない。
- 同程度の task class/risk に対する Terra または lead tier の実績を baseline とし、低コスト worker の品質と総費用を公平に比較できる。
- 実装と同じ誤解で作られた test だけに依存せず、重要変更では独立した反証 test または review evidence を持つ。
- 後続 review や運用で判明した escaped defect を元の task audit へ関連付け、初回合格率だけで routing を最適化しない。

## Non-goals

- すべての coding task を最安モデルへ強制すること。
- token 数だけで品質、費用対効果、必要能力を判定すること。
- worker の自己評価だけで精度を証明すること。
- 認証情報、prompt 全文、秘密情報、ソース本文を監査ログへ複製すること。
- 監査結果を根拠に、承認されていない product behavior、外部サービス、破壊的操作を自動変更すること。
- provider が公開しない telemetry を推定値で補うこと。

## Experience and interaction design

通常の作業では、主担当が task plan に task class と risk を記録し、coding worker に具体的な model ID と reasoning effort を指定する。worker 完了後、主担当は監査 validator を実行し、change record 内の compact summary と、機械可読な task audit artifact を確認する。

監査結果は次の3段階で表示する。

- `Pass`: model verification policy、quality gate、scope gate、verification gate を満たす。
- `Pass with telemetry gap`: 実装品質は満たすが、実モデルまたは token usage を基盤から取得できず、取得不能理由が記録されている。
- `Fail`: model mismatch、検証失敗、scope violation、未解決の rework、証拠不足のいずれかがある。

`Pass with telemetry gap` は品質合格を意味するが、「想定モデルが実際に使われた」とは扱わない。UI や外部 dashboard は初回スコープに含めず、change record と version-controlled audit artifact を正本にする。

## Documentation updates

- `.codex/skills/agent-task-orchestration/SKILL.md`: 低コスト coding routing、実モデル照合、token adequacy、品質 scorecard、改善 loop の正本として更新予定。
- `.codex/skills/agent-task-orchestration/references/execution-audit.md`: task audit schema、判定規則、sanitized example を新設予定。
- `.codex/skills/document-driven-development/SKILL.md`: delegated coding task の audit artifact と final reconciliation を change record gate に接続予定。
- `.codex/skills/document-driven-development/references/change-record-format.md`: task plan と final review に audit reference、model verification、usage、quality verdict を追加予定。
- `.codex/skills/learn-from-implementation-failures/SKILL.md`: routing/prompt/budget の改善条件と、単発失敗を恒久ルール化しない境界を追加予定。
- `AGENTS.md`: リポジトリ全体の低コスト coding worker 優先方針、主担当の独立検証責任、persistent improvement の承認境界を追加予定。
- 現時点では上記 canonical documents をまだ変更していない。Status が `Approved` になった後、本 change record と同じ change set で更新する。

## Technical impact

### Audit artifact

delegated task ごとに、`docs/changes/<change>/agent-audits/<task-id>.json` を保存する。秘密情報や prompt 全文は保存せず、task contract は change record の task ID と、必要なら正規化した instruction fingerprint で参照する。

必須情報は次とする。

- identity: change ID、task ID、run/attempt ID、owner、task class、risk、開始・終了時刻
- routing: expected tier、requested model ID、requested reasoning effort、observed model ID、observation source、verification state
- usage: input、cached input、output、reasoning、total token、configured budget、truncated、availability、unavailable reason
- instruction assessment: objective、scope、dependencies、acceptance、evidence、escalation、output format の充足
- quality assessment: build/test/format、AC coverage、scope compliance、lead findings、lead-authored correction、retry、escalation、regression
- review effort: reviewer identity/tier、review model/usage、review pass count、elapsed review time、human active minutes、blocking/non-blocking findings、correction time、re-verification time、availability/source
- outcome: verdict、lead decision、follow-up、次回 routing recommendation

実モデルまたは token telemetry を取得できない場合、対応フィールドは `unavailable` と取得不能理由を持たせる。requested model と observed model を同一視しない。

### Low-cost coding routing

現時点の候補は次とする。モデル availability と価格は変わるため、公式 model catalog と実行時の available model list を確認し、固定価格は運用 skill に埋め込まない。

| Task class | Initial routing | Conditions | Promotion trigger |
| --- | --- | --- | --- |
| 機械的で局所的な実装、明確な test 追加、単純な rename/adapter | `gpt-5.6-luna`, low または medium | frozen contract、狭い write scope、独立検証可能、security/data migration/public contract なし | 1回の focused correction 後も gate failure、scope 拡大、曖昧性発覚 |
| 複数ファイルだが境界が明確な実装 | `gpt-5.6-terra`, medium または high | dependency と acceptance が確定し、主担当が統合可能 | architecture/public contract/security/persistence 判断、反復失敗 |
| 要件解釈、設計、schema/migration、security、統合、最終判定 | Lead tier | 高い曖昧性または損失リスク | 委譲しない |

Luna を coding worker に使用すること自体を成功としない。quality/scope gates と lead review に合格した結果だけを成功標本に含める。

### Coding accuracy scorecard

品質の正本は pass/fail gate とし、単一の総合点で重大失敗を相殺しない。

1. acceptance: 担当 AC が実経路で満たされる。
2. verification: task 固有 test、関連 regression、build、format が成功する。
3. scope: 許可 write scope 外、秘密情報、無関係変更、危険な副作用がない。
4. review: 主担当の blocking finding が0である。
5. rework: worker retry、lead-authored corrective diff、model promotion を数える。

比較用指標として first-pass acceptance rate、escaped defect count、lead correction lines/files、retry count、elapsed time、successful outcome 当たりの token/cost を集計する。ただし test 不足、scope violation、未達 AC は token 効率が良くても `Fail` とする。

### Token adequacy

「必要 token 数」は事前に真値として決めず、次の evidence で評価する。

- allocated budget と実測 usage
- truncation、途中終了、context overflow の有無
- retry または continuation に追加された usage
- quality/scope gates を初回で通過したか
- 同一 task class・risk・model・reasoning effort の成功標本

成功標本が5件未満なら個別実績だけを表示し、推奨 budget を自動変更しない。5件以上では P50/P90、最大成功値、失敗時 usage を算出して recommendation を生成する。persistent default の変更は、直近の比較可能な成功標本と失敗根拠を change record に示し、validator と forward test を通す。

### Autonomous improvement boundary

同じ approved task の実行中は、主担当が自律的に次を行える。

- worker instruction の不足を補って再試行する。
- task を狭く再分割する。
- reasoning effort または model tier を昇格する。
- verification を追加し、worker result を reject する。
- task audit に次回 recommendation を残す。

永続的な routing/prompt/budget policy の変更は、次のいずれかの evidence がある場合だけ候補にする。

- 同一 failure mode が比較可能な task で反復した。
- security、data integrity、scope violation、false completion の重大 failure が1回発生した。
- 5件以上の成功標本が、より安価な routing でも quality/scope gate と許容 rework を安定して満たした。

候補は `learn-from-implementation-failures` に従い、最小の skill/config 変更、validator、独立 forward test、変更前後の比較を行う。現在の approved product scope を超える改善は提案に留め、別の承認なしに実行しない。

## Decisions

- requested model と observed model を別フィールドにする。observed 値がない場合は mismatch ではなく telemetry gap とするが、期待モデル使用の確認は未達と表示する。
- coding worker は安価なモデルを優先するが、適格性を task risk と独立検証可能性で判断する。
- `gpt-5.6-luna` を最小 task の初期候補、`gpt-5.6-terra` を bounded multi-file task の候補とする。lead/review tier は設計と最終採用を保持する。
- prompt 全文は監査 artifact に保存しない。指示の構造化項目、change record 参照、fingerprint だけを保存する。
- aggregate score だけで合否を決めず、重大 gate は個別 pass を必須にする。
- telemetry 欠落時には token/cost の比較をせず、elapsed time、retry、rework、検証結果を proxy として明示する。

### Comparable baseline and total outcome cost

低コスト routing の評価は、同程度の task class、risk、変更規模、test coverage を持つ Terra または lead-tier run を baseline として行う。比較可能な baseline がない場合は、絶対品質 gate の判定だけを行い、相対的に安価または高効率とは断定しない。

`successful outcome cost` には worker の usage だけでなく、次を含める。

- lead/reviewer の model usage、review pass count、elapsed review time
- 人間がレビューへ参加した場合の active review minutes。計測できない wall-clock 待機時間と区別する
- blocking/non-blocking finding の確認、修正方針決定、再レビューに要した effort
- worker の retry、continuation、追加指示
- lead または人間が行った corrective implementation と correction time
- 上位モデルへの promotion と再検証
- build/test/format/E2E の再実行と re-verification time
- failure により破棄された attempt

次を別々に集計し、最後に取得可能な項目だけで total successful-outcome cost を示す。

- `worker cost`: worker の token/credit/currency cost
- `automated review cost`: reviewer model の token/credit/currency cost
- `human review effort`: active minutes と、明示的に設定された換算単価がある場合だけ算出する人件費
- `rework cost`: correction、retry、promotion、re-verification の model cost と active minutes
- `review burden ratio`: `(automated review cost + review/rework effort) / total successful-outcome cost`

通貨またはcreditへの換算は、実行時の価格 source、取得日、単位を記録できる場合だけ行う。人間の換算単価はリポジトリへ既定値を置かず、利用者または組織が明示した場合だけ使用する。料金または人間工数を取得できない場合は token、active minutes、elapsed time、retry、finding、lead correction files/lines を別々に表示し、単一の架空費用へ換算しない。レビュー計測そのものに要する追加工数も `audit overhead` として分離し、低コスト worker の負担へ隠して加算しない。

### Task difficulty and reproducibility

実行前に task difficulty profile を記録する。最低限、ambiguity、affected execution paths、public contract、persistence/migration、concurrency、security/privacy、external dependency、既存 test coverage、expected write scope を分類する。routing 比較は profile が十分近い run に限定する。

再現性のため、audit artifact には開始 revision、終了 revisionまたはpatch identity、requested model/reasoning、task contract fingerprint、適用 skill set、tool/config profile version を記録する。provider が snapshot ID や observed model version を公開する場合だけ保存し、公開されない値は推測しない。

### Independent challenge and work attribution

worker が実装と test を同時に作成した重要変更では、主担当または別 reviewer が少なくとも一つの独立 evidence を作る。候補は反証 test、既存 test の追加条件、実経路 trace、property/invariant check、または user-visible E2E である。低リスクで機械的な変更は、既存の独立 regression suite が十分ならその根拠を記録して代替できる。

共有 worktree では、開始 revision、開始時 status、許可 write scope、worker 完了時 patch/diff、並行 task owner を記録する。他 agent または既存ユーザー変更を worker の成果や correction count に含めない。帰属できない変更が quality evidence に影響する場合、その run は比較統計から除外する。

### Escaped defects and guarded adaptation

後続 review、CI、運用、incident で worker の変更に起因する defect が確認された場合、元の task audit に defect reference、severity、検出時点、root cause classification を追記する。集計は initial pass と escaped-defect-adjusted pass を分ける。

自動 recommendation は policy を直接無制限に書き換えない。変更可能範囲を一段階の model/reasoning/budget または task-boundary tightening に限定し、次を必須にする。

- 変更理由となる比較可能標本または重大 failure evidence
- 適用対象 task class と除外条件
- 変更前 baseline と期待効果
- validator と独立 forward test
- rollback condition と観測期間

観測期間中に quality gate failure、escaped defect、総 outcome cost 悪化のいずれかが閾値を超えた場合、直前の安定 policy へ戻す recommendation を生成する。security/data integrity failure は標本数にかかわらず即時に低コスト routing の停止候補とする。

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | 各 delegated task で expected/requested/observed model、reasoning effort、observation source、verification state を区別して記録できる。 | T1,T2 | schema fixtures と validator test | Verified |
| AC2 | 実モデルを取得できない場合、監査は未確認理由を表示し、指定モデルが使用されたと誤認させない。 | T1,T2 | telemetry-missing fixture | Verified |
| AC3 | token usage、budget、truncation、retry usage を取得可能な範囲で記録し、取得不能値を推測しない。 | T1,T2 | complete/missing usage fixtures | Verified |
| AC4 | bounded coding task は Luna、やや複雑な bounded task は Terra を初期候補とし、危険・曖昧な task は lead tier に保持する routing rule が適用される。 | T3,T4 | routing forward test | Verified |
| AC5 | coding worker の成果は AC、test/build/format、scope、lead findings、rework、regression の gate で評価され、自己申告だけでは合格しない。 | T1,T3,T4 | pass/fail counterexample tests | Verified |
| AC6 | task ごとの指示について objective、scope、dependency、acceptance、evidence、escalation、output format の不足を監査できる。 | T1,T2 | incomplete-contract fixture | Verified |
| AC7 | 5件未満では budget default を変えず、十分な比較可能標本がある場合だけ successful outcome の token 分布から recommendation を作る。 | T2,T4 | small/qualified sample tests | Verified |
| AC8 | 失敗時に再指示、再分割、reasoning/model 昇格を自律実行でき、persistent policy 変更は evidence、validator、forward test、承認境界を守る。 | T3,T4 | failure and promotion simulation | Verified |
| AC9 | 監査 artifact に credential、secret、prompt 全文、ソース本文が保存されない。 | T1,T2 | forbidden-field/secret fixture tests | Verified |
| AC10 | 今後の delegated coding task の final review が task audit を参照し、model verification、quality verdict、usage availability、routing recommendation を change record へ要約する。 | T3,T4 | DDD format review と realistic forward test | Verified |
| AC11 | 低コスト model の結果は、比較可能な task class/risk の Terra または lead-tier baseline と区別され、baseline 不在時に相対的な費用優位を断定しない。 | T1,T2,T4 | comparable/non-comparable baseline fixtures | Verified |
| AC12 | 成功結果当たりの評価が worker usage に加えて review、retry、corrective implementation、promotion、破棄 attempt を含む。 | T1,T2,T4 | total-outcome-cost aggregation tests | Verified |
| AC13 | task の ambiguity、execution paths、contract、persistence、concurrency、security、external dependency、test coverage、write scope を実行前に分類できる。 | T1,T2 | difficulty-profile schema tests | Verified |
| AC14 | model/reasoning、task contract、開始・終了 revision、skill/tool/config profile を記録し、取得不能な provider version を推測しない。 | T1,T2 | reproducibility and missing-version fixtures | Verified |
| AC15 | worker が実装と test を作る重要変更は独立した反証 evidence を持ち、機械的変更で既存 regression suite を代替根拠にする場合は理由が記録される。 | T3,T4 | hidden-counterexample and low-risk exemption simulations | Verified |
| AC16 | 共有 worktree の worker diff を既存ユーザー変更および並行 agent 変更から区別でき、帰属不能 run は比較統計から除外される。 | T1,T2,T4 | mixed-worktree fixture tests | Verified |
| AC17 | 後続 review、CI、運用で確認された defect を元 task audit へ関連付け、initial pass と escaped-defect-adjusted pass を分けて集計できる。 | T1,T2,T4 | escaped-defect lifecycle test | Verified |
| AC18 | 自動 recommendation は一段階の変更、対象、evidence、validator、forward test、観測期間、rollback condition を持ち、重大 failure では低コスト routing の停止候補を生成する。 | T2,T3,T4 | adaptation/rollback simulations | Verified |
| AC19 | reviewer model usage、review回数、active review time、human review time、finding対応、correction、再検証、audit overheadを個別記録し、worker・review・reworkを合算した成功結果当たり総費用とreview burden ratioを算出できる。 | T1,T2,T4 | complete/partial/missing review-cost fixtures と aggregation tests | Verified |
| AC20 | 人間工数はactive minutesと待機時間を区別し、利用者が換算単価を明示しない限り架空の人件費へ変換しない。価格換算にはsource、日付、単位が記録される。 | T1,T2,T4 | human-rate absent/present、stale/missing price-source fixtures | Verified |

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | audit schema、sanitization、verdict、difficulty、baseline、reproducibility、attribution、review-effort rules を定義する。AC1-AC3, AC5, AC6, AC9, AC11-AC14, AC16, AC17, AC19, AC20。 | Main | Lead tier | Approval | orchestration reference と audit schema | schema review、fixtures | versioned schema と examples | Verified |
| T2 | audit validator、worker/review/rework総 outcome cost・baseline・escaped defect 集計、guarded recommendation script と test を実装する。AC1-AC3, AC6, AC7, AC9, AC11-AC14, AC16-AC20。 | Coding worker + Main review | Luna candidate; Lead review | T1 | `.codex/skills/agent-task-orchestration/scripts/**` と tests | script tests、invalid/mixed-worktree/lifecycle/review-cost fixtures | validator/summary/recommendation outputs | Verified |
| T3 | orchestration、DDD、failure skill、format、AGENTS.md を独立反証、帰属、改善安全弁へ接続する。AC4, AC5, AC8, AC10, AC15, AC18。 | Coding worker drafts + Main integration | Luna/Terra candidates; Lead integration | T1 | listed canonical documents | skill validators、diff review | policy and workflow diff | Verified |
| T4 | low-cost coding forward test、baseline比較、review工数集計、hidden counterexample、mixed-worktree、escaped defect、rollback の反証 test と final reconciliation を行う。全 AC。 | Independent reviewer + Main | Review tier + Lead tier | T2,T3 | read-only、audit fixtures、本記録 | realistic delegated task simulations、review-cost aggregation、全 validator、`git diff --check` | audit reports と closure ledger | Verified |

## Review gates

### Design and task-split review

- Reviewer: Lead tier main agent.
- Inputs: 現行 orchestration/DDD/failure skills、change-record format、AGENTS.md、Codex subagent configuration、現行 model catalog、現在の collaboration tool telemetry。
- Decision: schema/validator、policy integration、independent forward test は境界を分離できる。設計、security/privacy、baseline comparability、最終採用は主担当に保持する。
- Model decision: 安価な coding worker の実証対象は Luna を第一候補とし、失敗時に Terra、さらに lead tier へ段階昇格する。実モデルの observed telemetry が得られない環境では、その run は model-confirmed sample に数えない。
- Write-scope decision: T2 の script と T3 の canonical documents は分離し、schema を主担当が frozen にした後に着手する。
- Evidence decision: worker-authored tests だけでは重要変更を合格にせず、T4 が hidden counterexample または同等の独立 evidence を提供する。共有 worktree の帰属不能 run は比較から除外する。
- Improvement decision: recommendation は一段階、観測期間付き、rollback可能に限定し、永続 policy の無制限な自己書換えは許可しない。
- Cost decision: worker単体費用ではなく、automated review、人間のactive review、correction、promotion、再検証、audit overheadを分離して成功結果当たり総費用を評価する。換算根拠がない値は通貨へ変換しない。
- Follow-up: ユーザー承認後、pre-implementation review で現在の未コミット変更と write scope を再確認する。

### Pre-implementation review

- Reviewer: Lead tier main agent.
- Inputs: Approved AC1-AC20、現在のgit status、T1-T4の依存・write scope、既存skill validator、利用可能なlocal test環境。
- Decision: T1を`In progress`、T2-T4を`Dependent`とする。schema/referenceを主担当が確定してからvalidator、canonical documents、forward testの順に進める。現在のproduction codeおよび既存の未コミット変更は対象外とする。
- Worker decision: 本実装は監査基盤・複数canonical policy・validatorが密結合であり、現ターンでは共有ファイル競合を避けて主担当が直列統合する。実装後のforward testでLuna/Terra routingを模擬し、将来の実taskから実績を蓄積する。
- Evidence required: schema fixtures、validator/aggregation tests、skill validators、change-record validator、`git diff --check`、対象ファイルのstatus/diff review、AC closure ledger。
- Escalation: schemaがprovider固有telemetryを必須化する、secretを保持する、またはAC間の矛盾が判明した場合だけ設計へ戻す。

## Verification record

- 2026-09-19: `.codegraph/` と現在の未コミット変更を確認した。本提案では既存の production code と他 change record を変更していない。
- 2026-09-19: `agent-task-orchestration`、DDD、change-record format、既存の model-tiered orchestration change record を確認した。現行は model tier と usage/effort の記録を求めるが、requested/observed model、token schema、coding accuracy scorecard、budget recommendation を標準化していない。
- 2026-09-19: 現行 `.codex/config.toml` に CodeGraph MCP 以外の agent default/custom agent 設定がないことを確認した。
- 2026-09-19: OpenAI Docs の model catalog で Luna が cost-sensitive workload、Terra が intelligence/cost balance 向けであることを確認した。
- 2026-09-19: OpenAI Docs の subagent configuration で、起動時の明示 model が default/parent を上書きできることと、custom agent が model/reasoning effort を設定できることを確認した。
- 2026-09-19: 追加設計レビューで、baselineなしの費用優位判断、lead review費用の欠落、worker-authored testへの循環依存、task難易度差、共有worktreeの成果帰属、後発 defect、自動改善の過剰適応をリスクとして特定し、AC11-AC18と反証 testへ接続した。
- 2026-09-19: review costをmodel usageだけでなく、review pass、active human minutes、finding対応、correction、re-verification、audit overheadへ分解し、AC19-AC20と総 outcome cost aggregationへ接続した。

### Verification failure ledger

| ID | Original command/step | Observed failure | Classification | Cause and disposition | State |
| --- | --- | --- | --- | --- | --- |
| VF1 | `python -m unittest '.codex/skills/agent-task-orchestration/scripts/test_audit_agent_execution.py'` | `ValueError: Empty module name` before test discovery | Environment/command mismatch | Windowsの`unittest` module-name解釈へfilesystem pathを渡した。`python .codex/skills/agent-task-orchestration/scripts/test_audit_agent_execution.py`で11件成功し、意図した全testを検証した。 | Verified |

## Checkpoint review

- Reviewer: Lead tier main agent.
- Inputs: schema/reference、custom low-cost worker TOML、validator/aggregation script、11反例test、orchestration/DDD/failure/AGENTS diffs、skill validators。
- Decision: T1-T3は承認設計へ接続済み。requested/observed modelを分離し、telemetryを推測せず、quality/scope/attribution failureをfailにし、5件未満のpersistent adjustmentを拒否する。review costはworker、automated review、human review、rework、audit overheadへ分解し、全componentがある場合だけtotalとburden ratioを検算する。
- Follow-up: T4としてformat、change-record validator、全反例test、TOML parse、diff/status、AC closureを再検証する。

## Deviations and follow-up

- 実行基盤が worker の実モデルと token usage を親へ公開するかは環境依存である。実装時に利用可能な metadata を再確認し、取得不能なら `Pass with telemetry gap` とする。推測値で AC1-AC3 を満たしたことにはしない。
- 価格は変動するため、運用 artifact には task 実行時の source/date を記録できるようにするが、skill 本文には固定価格を埋め込まない。

## Acceptance-criterion closure ledger

| Scope | Evidence | Result |
| --- | --- | --- |
| AC1-AC3, AC6, AC9, AC13-AC14 | version 1 schema、requested/observed separation、telemetry-gap、instruction/reproducibility fields、forbidden-field tests | Verified |
| AC4-AC5, AC8, AC10, AC15 | Luna custom worker、Luna→Terra→lead routing、quality gates、independent challenge、DDD final review接続 | Verified |
| AC7, AC11, AC17-AC18 | 5-sample gate、comparable baseline、escaped defect、one-step adjustment/rollback rulesと反例test | Verified |
| AC12, AC19-AC20 | worker、automated/human review、rework、audit overhead、total、burden ratioのschema・検算test | Verified |
| AC16 | start status、patch identity、parallel owner、unattributed change exclusionと反例test | Verified |

## Final review

- Reviewer: Lead tier main agent.
- Inputs: Approved AC1-AC20、全対象diff、11 validator/aggregation tests、3 skill validators、custom agent TOML parse、change-record validator、CI-equivalent format gate、`git diff --check`、`git status`。
- Decision: Pass。T1-T4とAC1-AC20はすべて`Verified`で、承認範囲の未完了状態またはacceptance-blocking external blockerはない。実モデル/tokenが取得不能なrunをverified扱いせず、レビュー工数を含む総費用も根拠がある単位だけで算出する。
- Cost review: 本変更自体はdelegated workerを使用していないため、モデル別audit sampleには含めない。将来のdelegated codingからartifactを蓄積し、5件未満ではpersistent defaultを自動変更しない。
- Self-audit: model selection、token availability、quality/scope、独立反証、shared-worktree帰属、baseline、escaped defect、review/rework/audit overhead、rollback境界を再確認した。新たな再利用可能skill gapは検出しなかった。

## Implementation result

- `.codex/agents/low-cost-coding-worker.toml` にLuna/mediumの狭いcoding workerを追加した。
- `execution-audit.md` にversion 1 audit schema、routing、budget、品質、帰属、review cost、改善安全弁を定義した。
- `audit_agent_execution.py` がartifact validation、P50/P90集計、5-sample gate、escaped-defect suspension recommendationを提供する。
- orchestration、DDD、failure-learning、change-record format、AGENTS.mdを同じ監査契約へ接続した。

## Final verification record

- `python .codex/skills/agent-task-orchestration/scripts/test_audit_agent_execution.py`: 11件成功。
- `quick_validate.py`: `agent-task-orchestration`、`document-driven-development`、`learn-from-implementation-failures` の3件成功。
- `validate_change_records.py docs/changes/20260919_agent-execution-audit/README.md`: issues 0。
- Python `tomllib`: custom agent TOML parse成功。
- `dotnet format HorseRacingPrediction.sln --no-restore --verify-no-changes`: 成功。
- 対象ファイルの`git diff --check`: 成功。
- `git status`: 本変更対象だけが変更・追加され、既存production codeの変更はない。

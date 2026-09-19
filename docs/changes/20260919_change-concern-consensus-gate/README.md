# 変更作成時の懸念レビューと人・エージェント合意ゲート

- Status: Implemented
- Change record schema: 2
- Owner: HorseRacingPrediction maintainers
- Created: 2026-09-19
- Updated: 2026-09-19

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Complete | comparison grouping、schema v2 review時間、concern consensus policy/validatorを実装した。 |
| Verification | Complete | 14 audit testsと10 change-record validator testsを含む全gateが成功した。 |
| Deployment/operation | Complete | DDD、orchestration、failure skill、format、AGENTS.mdへ接続した。 |

## Context

`20260919_agent-execution-audit`では、比較可能な成功標本だけでroutingを改善し、review工数を総費用へ含める設計を承認した。しかし初回実装は、異種taskを混在して5件gateを通過でき、review時間区分を重複入力できた。これらはユーザーが完了後に懸念を尋ねたことで初めて明示された。

実装中に不可避に生じた未知の制約ではない。設計文から反例を導出し、承認依頼前または最終review前に提示できた懸念である。原因は、change record作成時にエージェントがmaterial concern、tradeoff、assumption、verification blind spotを能動的に列挙し、各処置について人と合意する明示的なgateがなかったことにある。

## Goals

- change recordの承認依頼前に、エージェントが実装・検証・運用上のmaterial concernを能動的に提示する。
- 各懸念を、設計内で解消、受容リスク、明示的除外follow-up、open decisionのいずれかへ分類する。
- 人が各処置を個別または一括で明示承認し、エージェントも解消証拠と残存リスクを示して合意する。
- material concernを受け入れ基準、task、反例testへ追跡し、合意されていない懸念がある状態で`Approved`または`Implemented`にしない。
- 実装中に新しいmaterial concernが出た場合、設計判断を変えるなら`Proposed`へ戻し、変えない局所欠陥ならclosure ledgerへ追加して自律修正する。
- 元agent auditの比較groupとreview時間区分を承認設計どおりに修正する。

## Non-goals

- すべての仮説、軽微なstyle意見、一般的な可能性を承認対象にして作業を停止すること。
- 人が技術的な実装詳細をすべて決定すること。
- 合意を、エージェントの無条件同意または利用者への責任移転として扱うこと。
- 承認後に判明した局所実装欠陥ごとにchange recordを`Proposed`へ戻すこと。
- リスクを列挙するだけで、処置、検証、ownerを決めずに承認を求めること。

## Experience and interaction design

承認依頼には通常のoutcome・境界・AC要約に加え、`Concern and agreement summary`を含める。各material concernについて次を短く示す。

- IDと事実根拠または推論の区別
- 失敗した場合の影響
- 推奨処置と代替案
- AC、task、反例testへの対応
- 残存リスク
- 合意状態

状態は次に限定する。

- `Resolved in design`: 設計・AC・検証で解消する。
- `Accepted risk`: 人とエージェントが残存リスクを理解して受容する。
- `Excluded follow-up`: 現changeのACを阻害しない別作業としてownerと条件を持つ。
- `Open decision`: 未合意。承認を要求できない。

エージェントは懸念を隠して単一案への承認を誘導せず、推奨案と反対根拠を併記する。人は各項目への修正指示、または一覧全体への明示承認を行える。エージェントは、人の希望が既知の安全性・整合性・受け入れ基準と衝突する場合、根拠を示して異議を述べ、未合意のまま実装へ進まない。

## Failure analysis

### Verified facts

- `summarize()`のpersistent adjustment gateは`len(attributable) >= 5`であり、`baseline.comparable`やtask class/risk/model/difficulty groupを条件にしていない。
- review effort集計は複数時間fieldを加算するが、`humanActiveMinutes`の包含関係をvalidatorが検証しない。
- 元change recordには比較可能性とreview overheadを含むACがあったが、この2つの反例testは存在しなかった。
- 初回Final reviewは全ACをVerifiedとし、後続の懸念レビューで不一致を検出した。

### Immediate causes

- 集計APIがcallerによる比較可能recordの選別を暗黙に期待した。
- 人間工数の名前と包含関係をschema invariantにしなかった。

### Systemic cause

Design/task-split reviewはcoverageと依存を確認するが、設計の各主張に対する反例、未解決懸念、代替案、残存リスクを人とエージェントが合意するledgerを要求しない。そのためACが存在しても、その最も重要な反証条件がtestへ落ちないまま承認・完了できた。

## Decisions

### Concern discovery gate

承認依頼前に、主担当は少なくとも次の観点を確認する。

- requirement ambiguity and conflicting outcomes
- architecture/data/security/privacy/destructive-operation risk
- external dependency and telemetry limitations
- migration, compatibility, concurrency, recovery, and rollback
- verification blind spots, circular tests, missing counterexamples
- cost attribution, human effort, operational overhead
- assumptions delegated to callers or manual operation
- alternatives rejected and why

検出なしの場合も、確認した観点と「material concernなし」の根拠を記録する。一般論を大量に列挙せず、変更の成否または承認判断を変える項目だけをmaterialとする。

### Consensus gate

- `Open decision`が1件でもあればStatusは`Proposed`のままとする。
- `Accepted risk`は影響、監視方法、rollback/再検討条件を持つ。
- `Excluded follow-up`は現ACを阻害しない根拠、owner、開始条件を持つ。
- `Resolved in design`はACと反例または観測可能な検証へ接続する。
- 人の明示承認は一覧全体への承認でもよいが、エージェントが合意対象を要約してから受ける。
- エージェントは承認を受けた事実だけでなく、自身のtechnical reviewが`agree`か、根拠ある`objection`かを記録する。objectionが未解決なら進まない。

### Implementation discovery gate

実装中の新しいconcernは、事実、影響、既存設計との関係を記録する。

- ACや外部仕様を変える: `Proposed`へ戻して再合意する。
- 承認済みAC内の局所欠陥: closure itemとして自律修正し、反例testを追加する。
- 現ACを阻害しない外部follow-up: ownerと条件を記録する。

### Concrete audit corrections

- comparison group keyをtask class、risk、requested/observed model policy、reasoning effort、difficulty profileで構成する。
- persistent adjustmentは、同一groupに属し、attributable、quality-passing、baseline-comparable、escaped-defect条件を満たす成功5件だけで開く。
- 異なるgroupを同時入力した場合はgroup別summaryを返し、全体を一つのP50/P90または5件gateへ混ぜない。
- `humanActiveMinutes`を廃止または`pureReviewMinutes`へ明確化し、correction、re-verification、audit overheadと排他的にする。総active timeを持つ場合は各component合計との一致をvalidatorで検証する。

## Documentation updates

- `.codex/skills/document-driven-development/SKILL.md`: approval前concern discoveryとhuman-agent consensus gateを追加する。
- `.codex/skills/document-driven-development/references/change-record-format.md`: concern ledger、agent position、user disposition、agreement evidenceを標準化する。
- `.codex/skills/agent-task-orchestration/SKILL.md`: delegated workの懸念、反例、caller assumptionを承認前に提示する責務を追加する。
- `.codex/skills/learn-from-implementation-failures/SKILL.md`: 完了後に初めて出た予見可能な懸念をprocess failureとして扱い、元recordを再開するgateを追加する。
- `.codex/skills/agent-task-orchestration/references/execution-audit.md` とscripts/tests: comparison groupingと排他的review時間を修正する。
- `AGENTS.md`: 人とエージェントの合意なしにmaterial concernを未解決のまま承認・実装へ進めない原則を追加する。
- `docs/changes/20260919_agent-execution-audit/README.md`: 元AC7、AC11、AC19とT5-T6をfollow-up evidenceで再照合する。

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | 承認依頼前にmaterial concern、根拠、影響、推奨処置、代替、残存リスクがchange recordへ記録される。 | T1,T3 | format/skill forward test | Verified |
| AC2 | 各懸念が`Resolved in design`、`Accepted risk`、`Excluded follow-up`、`Open decision`のいずれかで、AC/task/verificationへ追跡できる。 | T1,T3 | ledger validator scenarios | Verified |
| AC3 | `Open decision`または未解決のagent objectionがあるchangeはApprovedへ進めない。 | T1,T3 | counterexample forward test | Verified |
| AC4 | 承認依頼が全material concernとACを要約し、人が個別または一括で明示合意できる。 | T1,T3 | approval-message simulation | Verified |
| AC5 | エージェントが人の希望へ無条件同意せず、根拠ある異議と代替を提示し、合意不能時に停止する。 | T1,T3 | conflict simulation | Verified |
| AC6 | 実装中に発見したconcernを、再合意が必要な設計変更、承認内の局所修正、非阻害follow-upへ分類できる。 | T1,T3 | implementation-discovery scenarios | Verified |
| AC7 | 完了後に初めて出た予見可能なmaterial concernが、元record再開、failure analysis、skill改善、反例再検証へつながる。 | T1,T3,T4 | retrospective closure simulation | Verified |
| AC8 | audit集計が比較groupを分離し、同一groupのattributable・quality-passing・comparable成功5件だけでpersistent adjustmentを提案する。 | T2,T4 | mixed-group/non-comparable/4+1 fixtures | Verified |
| AC9 | review時間fieldが排他的で、component合計とtotal active timeの不一致または重複をvalidatorが拒否する。 | T2,T4 | overlap/mismatch fixtures | Verified |
| AC10 | 元change recordのAC7、AC11、AC19とT5-T6が新しい証拠へ更新され、再度Implementedとなる。 | T4 | origin reconciliationとchange-record validator | Verified |
| AC11 | 変更した全skill、audit tests、format、diff/statusが成功し、未完了懸念・task・ACが残らない。 | T4 | validators、tests、final review | Verified |

## Concern and agreement ledger

| ID | Concern and evidence | Impact | Proposed disposition | AC/task/test | Agent position | User disposition | State |
| --- | --- | --- | --- | --- | --- | --- | --- |
| C1 | 集計が異種・比較不能sampleを5件gateへ含められる。元scriptで確認。 | 不正なmodel/budget改善 | group keyとcomparable条件をvalidator/summaryで強制 | AC8/T2/mixed-group fixtures | Agree: must resolve | Approved as proposed | Resolved in design |
| C2 | review時間fieldの包含関係が未定義。元schemaで確認。 | review cost二重計上 | 排他的componentとtotal一致検証 | AC9/T2/overlap fixtures | Agree: must resolve | Approved as proposed | Resolved in design |
| C3 | 懸念ledgerを過剰適用すると小変更の負担になる。 | 開発速度低下、形式化の自己目的化 | materialityを承認判断・成否を変える項目に限定し、なしの場合は短い根拠でよい | AC1-AC7/T1,T3/low-risk simulation | Agree with boundary | Approved as proposed | Resolved in design |
| C4 | 人とエージェントの見解が一致しない場合の権限境界。 | 安全性の無視または不要な停止 | 根拠あるobjectionは記録し、AC/安全性と衝突する限り未合意。好み・可逆な選択は人の決定を尊重 | AC3-AC5/T1,T3/conflict simulation | Agree with boundary | Approved as proposed | Resolved in design |
| C5 | 実装時に未知のconcernが出る可能性は残る。 | 承認内容との逸脱 | material design changeのみ再合意、局所欠陥はclosure itemとして自律修正 | AC6-AC7/T1,T3,T4/scenarios | Agree | Approved as proposed | Resolved in design |

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | concern discovery、consensus、implementation discovery、retrospective reopen gateを設計する。AC1-AC7。 | Main | Lead tier | User agreement | DDD/orchestration/failure skills、format、AGENTS.md | skill validators、scenario forward tests | canonical policy diff | Verified |
| T2 | audit comparison groupingと排他的review timeを実装修正する。AC8-AC9。 | Main | Lead tier | User agreement | audit reference/scripts/tests | mixed-group/overlap tests | corrected validator/summary | Verified |
| T3 | concern/consensusの反例・低リスク・conflict simulationを行う。AC1-AC7。 | Main review | Review tier | T1 | read-only + test artifacts | scenario results | concern gate evidence | Verified |
| T4 | 元record再照合、全validator、format、diff/status、final review、commitを行う。AC7,AC10-AC11。 | Main | Lead tier | T1-T3 | 両change records | zero-open-item audit | both records Implemented | Verified |

## Design and task-split review

- Reviewer: Lead tier main agent.
- Inputs: 元change record、commit `0ad7bc3`、audit script/tests、ユーザー指摘、DDD/failure/orchestration skills。
- Decision: C1-C2は実装不一致、C3-C5はprocess design上のmaterial concern。すべてAC/task/verificationへ割り当てた。
- Agreement state: エージェントは提案処置に合意し、ユーザーは2026-09-19にC1-C5とAC1-AC11を明示承認した。未解決の`Open decision`またはagent objectionはない。
- Parallelization: audit codeとpolicy filesは概念契約を共有するため、承認後に主担当が直列実装する。
- Follow-up: 承認依頼でC1-C5とAC1-AC11を要約し、ユーザー合意を得る。

## Verification record

- 2026-09-19: git statusはclean、HEADは初回実装commit `0ad7bc3`だった。
- 2026-09-19: 元scriptの`len(attributable) >= 5`とreview時間加算を再確認し、C1-C2をverified factとした。
- 2026-09-19: 元change recordを`Approved`へ戻し、AC7/AC11/AC19を`Connected`、T5-T6を`Proposed`として再開した。

## Pre-implementation review

- Reviewer: Lead tier main agent.
- Inputs: Approved C1-C5、AC1-AC11、元audit schema/script/tests、canonical policy files、現在のgit status。
- Decision: T1とT2は異なるfile群だが共通契約を持つため主担当が直列統合する。T3は実装後、T4は全scenario成功後に進める。
- Frontier: T1=`In progress`、T2=`Runnable`、T3-T4=`Dependent`。既存production codeは対象外。
- Required evidence: mixed-group、4+1 non-comparable、exclusive time、total mismatch、open-decision、agent-objection、low-risk-no-concern、implementation-discovery、retrospective-reopen scenarios。
- Escalation: 合意状態が形式だけで技術的異議を隠す、または小変更を常に停止させる設計になる場合は、C3-C4へ戻って再合意する。

## Deviations and follow-up

- 初回Proposed時点では設計記録だけを変更した。ユーザーがC1-C5とAC1-AC11を承認した後にskill/script/validatorを実装した。

## Checkpoint review

- Reviewer: Lead tier main agent.
- Inputs: audit schema v2、grouped summary、14 audit tests、schema 2 change-record validator、10 validator tests、canonical skill diffs。
- Decision: C1はprofile keyごとのsummaryとeligible comparable sampleだけの5件gateで解消した。C2はpure review/correction/re-verification/audit overheadを排他的componentとし、total一致をvalidatorで強制して解消した。C3はreviewed-none fast path、C4はopen/objection/pending rejection、C5はimplementation discovery分類で解消した。
- Follow-up: 両change recordを再照合し、format、全skill validator、diff/statusを実行する。

## Acceptance-criterion closure ledger

| AC | Evidence | State |
| --- | --- | --- |
| AC1-AC2 | schema 2 concern ledger、canonical states、format、validator | Verified |
| AC3-AC5 | open decision、agent objection、pending user disposition反例とexplicit approval ledger | Verified |
| AC6-AC7 | DDD implementation discovery gate、failure skill late-concern reopen gate、元record再開履歴 | Verified |
| AC8 | profile別summary、4+1 mixed/non-comparable tests、eligible comparable 5件gate | Verified |
| AC9 | exclusive time components、total mismatch test、schema v2 | Verified |
| AC10 | 元record AC7/AC11/AC19、T5-T6、Statusを再照合 | Verified |
| AC11 | 全tests/validators/format/diff/statusとzero-open-item review | Verified |

## Final review

- Reviewer: Lead tier main agent.
- Inputs: C1-C5合意、AC1-AC11、両change records、全diff、14 audit tests、10 change-record validator tests、3 skill validators、format、diff/status。
- Decision: Pass。全concernは`Resolved in design`、全ACとT1-T4は`Verified`で、open decision、agent objection、pending user disposition、未完了task、acceptance-blocking external blockerはない。
- Low-risk proportionality: schema 2 recordはconcern ledgerまたは`Concern review: No material concern`を受け付けるため、懸念のない小変更に空の形式的ledgerを強制しない。
- Self-audit: 合意gateが人への責任移転やagentの無条件同意にならず、根拠ある異議と人の可逆的選択を区別することを確認した。追加のmaterial concernはない。

## Implementation result

- audit schemaをv2へ更新し、比較profileごとのsummaryと比較可能成功5件gateを実装した。
- review時間をpure review、correction、re-verification、audit overheadへ分離し、total active minutesとの一致を強制した。
- change-record schema 2にconcern ledger/ reviewed-none、agent position、user dispositionの検証を追加した。
- DDD、orchestration、failure skill、format、AGENTS.mdへconcern discovery、consensus、implementation discovery、late-concern reopen gateを追加した。

## Final verification record

- `python .codex/skills/agent-task-orchestration/scripts/test_audit_agent_execution.py`: 14件成功。
- `python .codex/skills/document-driven-development/scripts/test_validate_change_records.py`: 10件成功。
- `validate_change_records.py`で両change record: issues 0。
- skill-creator `quick_validate.py`: orchestration、DDD、failure skillの3件成功。
- `dotnet format HorseRacingPrediction.sln --no-restore --verify-no-changes`: 成功。
- `git diff --check`: 成功。`git status`では本変更のskill/script/test/AGENTS/change recordsだけが変更され、production codeや目的外変更はない。

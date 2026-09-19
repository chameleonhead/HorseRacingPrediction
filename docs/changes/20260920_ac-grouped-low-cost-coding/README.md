# 細粒度worker指示とAC単位レビューによる低コストcoding

- Status: Implemented
- Change record schema: 2
- Orchestration schema: 2
- Owner: HorseRacingPrediction maintainers
- Created: 2026-09-20
- Updated: 2026-09-20

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Complete | orchestration/DDD/AGENTS、audit reference、schema-2 validatorとfixturesを更新した。 |
| Verification | Complete | 33 compact-audit tests、14 legacy audit tests、10 DDD validator tests、両skill validation、change-record validation、formatを通過した。 |
| Deployment/operation | Complete | repository policyとして即時適用可能。既存schema-1 auditは後方互換で維持する。 |

## Context

現在のroutingは、architecture、public contract、persistence/migration、concurrency、security/privacy等を含むtaskをLead tierへ保持する。この安全境界自体は必要だが、changeまたはtaskを大きな単位のまま評価すると、その内部にあるDTO追加、確定済みquery、fixture、mapping、機械的更新までSol相当のLead作業になりやすい。

実際のagent auditでは、`subject-profile-id-migration`のようなchange全体はidentity、persistence、recoveryを含むためLeadが妥当だった一方、store candidate sliceやEF persistence sliceはworkerへ分離できた。監査記録には`runtime-default`や抽象的なcost-sensitive worker指定も残り、低コストmodelの実利用確認は限定的だった。

本変更ではmodel defaultを一律に下げない。Leadが受け入れ条件、技術的不変条件、公開契約、統合順序を先に確定し、その決定に従う実装を小さく詳細なworker contractへ分解する。レビューはmicrotaskごとに重く行わず、統合された成果を受け入れ条件のgroup単位で確認する。AC evidenceが失敗または不十分な場合だけtask/diff単位へ掘り下げる。

## Goals

- model routing前に、Lead-owned decisionとworker-executable implementationを分離して作業難易度を下げる。
- Lead tierを選ぶ前に、低コストworkerへ安全に切り出せるworkstreamがないか明示的に評価する。
- workerにはobjective、frozen contract、不変条件、write scope、禁止事項、反例、検証を詳細に渡す。
- reviewは原則としてACまたは密接なAC group単位で行い、microtaskごとの重複reviewを避ける。
- AC group reviewは統合された実経路、実行可能な証拠、scope、不変条件、未解決findingを確認する。
- gate failure、scope逸脱、設計判断追加等がある場合だけ詳細なtask/diff reviewへ降りる。
- worker token削減がprompt作成・review・rework overheadで相殺されていないか監査する。

## Non-goals

- high-riskな設計判断、identity/security/data-integrity判断、破壊的操作、最終受入れを軽量modelへ移すこと。
- ACに書かれていない重要な安全性・scope・非回帰をレビュー対象から除外すること。
- change全体を無理にmicrotask化すること。
- すべてのmicrotaskへ個別reviewer、個別change record、重複ledgerを作ること。
- model telemetryが取得不能なrunをLuna成功実績として扱うこと。

## Proposed workflow

### 1. Acceptance contract freeze

Leadは承認前に、各ACへ正常系、重要反例、不変条件、非回帰、scope、必要な統合/E2E evidenceを含める。architecture、public contract、data model、migration order、security/privacy、concurrency semantics等の判断を確定し、未決定事項をworkerへ渡さない。

### 2. Decomposition before routing

各Lead候補taskについて、次の順に分解可能性を確認する。

1. 判断を必要とする部分と、確定済み判断を実装する部分を分ける。
2. 実装部分を、単一objective、限定write scope、独立verificationを持つsliceへ分ける。
3. 共有contract、migration snapshot、generated file、同一fileを所有するsliceは直列化または一ownerへまとめる。
4. Luna候補、Terra候補、Lead保持をtask単位で決める。
5. Lead保持部分は、分割不能理由をrouting欄へ具体的に記録する。

Luna候補は、frozen contractに従う機械的・局所的実装、fixture/test追加、mapping、既存patternへの適用とする。Terra候補は、境界が確定した複数file実装または小規模integrationとする。Leadは要件・契約・risk判断、共有state統合、最終受入れを保持する。

### 3. Detailed worker contract

worker promptは既存contractに加え、次を明示する。

- 対応するACと、workerが証明する局所evidence
- frozen decision/invariantと変更禁止の判断
- exact write scopeと共有file owner
- happy pathだけでなく最低一つの反例
- 変更してよい実装自由度と、Leadへ戻す判断境界
- 成功コマンド、期待結果、成果形式

詳細化はコード本文の大量複製ではなく、正本への参照と不変条件を中心にする。

### 4. AC-grouped review

既存のAcceptance criteria表にある`Tasks`対応をreview groupとして再利用し、新しいreview ledgerは作らない。複数microtaskが一つまたは密接なAC群を実現する場合、統合後に一回reviewする。

AC group reviewの入力は次に限定する。

- AC本文とconcern disposition
- 統合されたattributable diff/scope manifest
- ACに対応するtest、build、format、E2E/invariant evidence
- worker auditのretry、correction、promotion、telemetry availability
- open verification failureとdeviation

reviewerは通常、各workerの全思考や全commandを再追跡しない。ACを実経路で満たし、scopeと不変条件を守るかを判定する。

### 5. Detail-review escalation

次の場合だけtask/diff単位の詳細reviewへ降りる。

- AC evidenceまたは関連testが失敗・欠落・矛盾する。
- workerがfrozen decisionを変更、または新しい設計判断を追加した。
- write scope外、共有file競合、帰属不能変更がある。
- public contract、persistence/migration、concurrency、security/privacyの実装がcontractと一致する証拠が不足する。
- worker-authored testしかなく、独立反例または既存regressionがない。
- retry、lead correction、review burdenが許容値を超える。

詳細reviewで局所原因を閉じた後、最終判定は再びAC groupへ戻して行う。

## Alternatives considered

- **全subagentをLuna defaultにする:** reviewや高risk taskまで軽量化し、model選択の誤りを増やすため採用しない。
- **microtaskごとに独立reviewする:** 品質は確認しやすいがreview costがworker節約を相殺するため採用しない。
- **change全体をSolで実装する:** 安全だが確定済みの機械作業まで高コストになるため採用しない。
- **ACだけを見てscope/securityを別確認しない:** AC不足時に重大欠陥を見逃すため採用しない。scope・不変条件・非回帰をAC contractへ含める。

## Concern and agreement ledger

| ID | Concern and evidence | Impact | Proposed disposition | AC/task/test | Agent position | User disposition | State |
| --- | --- | --- | --- | --- | --- | --- | --- |
| C1 | ACが正常系だけの場合、粗いAC reviewはscope/security/data-integrity欠陥を見逃す。 | false completion | AC freeze時に反例、不変条件、非回帰、scope evidenceを必須化する。 | AC1,AC5/T1,T3/weak-AC scenario | Agree: must resolve | Approved as proposed | Resolved in design |
| C2 | microtaskを細かくしすぎるとprompt、統合、review overheadが増える。 | 総費用悪化 | 単独verificationとwrite ownershipが成立するsliceだけ分け、reviewはAC groupへ集約する。 | AC2,AC6/T1,T3/over-fragmentation scenario | Agree with boundary | Approved as proposed | Resolved in design |
| C3 | frozen contract名目でLeadの未決定事項をworkerへ押し付ける可能性がある。 | Luna failure/rework | worker promptにfrozen decisionsとescalation boundaryを明示し、未決定なら委譲しない。 | AC3/T1,T3/ambiguous-contract scenario | Agree: must resolve | Approved as proposed | Resolved in design |
| C4 | 同一fileやmigration snapshotを複数workerへ分割すると競合する。 | attribution loss、merge defect | write scope ownershipを一つにし、共有artifactは直列化する。 | AC2,AC4/T1,T3/shared-file scenario | Agree: must resolve | Approved as proposed | Resolved in design |
| C5 | AC group reviewだけでは、worker-authored testが同じ誤解を共有する可能性がある。 | false green | material ACには既存regression、独立反例、invariant、E2Eのいずれかを要求する。 | AC5/T1,T3/circular-test scenario | Agree: must resolve | Approved as proposed | Resolved in design |
| C6 | 実モデルtelemetryが取得不能なら、低コスト化の実績を証明できない。 | routing評価不能 | requested modelを具体IDで記録し、observed不明runは品質評価のみ、model別費用統計から除外する。 | AC7/T2,T3/telemetry-gap scenario | Agree | Approved as proposed | Resolved in design |
| C7 | 一つのAC groupが大きすぎるとfailure原因と成果帰属が不明になる。 | review精度低下 | 密接なACだけをgroup化し、evidenceが独立しないgroupは分割する。failure時はtask/diffへdrill downする。 | AC4,AC5/T1,T3/oversized-group scenario | Agree with boundary | Approved as proposed | Resolved in design |

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | 承認前に各material ACが正常系、重要反例、不変条件、非回帰、scope、統合evidenceを持ち、未決定の設計判断がworkerへ渡らない。 | T1,T3 | DDD design gateとweak/complete AC challenge | Verified |
| AC2 | Lead候補taskはrouting前にdecisionとexecutionへ分けられ、単独verificationとwrite ownershipを持つ実装sliceだけがLuna/Terra候補になる。 | T1,T3 | decomposition/shared-file challenge | Verified |
| AC3 | worker contractがAC、frozen decision、invariant、write scope、禁止事項、反例、escalation、検証、成果形式を含む。 | T1,T3 | worker contract inspection | Verified |
| AC4 | 既存AC↔Task対応をreview groupとして再利用し、複数microtaskを統合後のAC evidenceで一回reviewできる。 | T1,T2,T3 | schema-2 grouped-review fixtures | Verified |
| AC5 | AC group reviewが実経路、scope、不変条件、非回帰、独立evidenceを確認し、failure時だけtask/diff詳細reviewへ移る。 | T1,T3 | pass/fail/circular-test/oversized-group challenge | Verified |
| AC6 | routing評価がworker費用に加え、prompt作成、統合、AC review、drill-down、reworkを含み、過分割で総費用が悪化すればsliceを統合またはtier昇格する。 | T1,T2,T3 | overhead schema and complete/partial fixtures | Verified |
| AC7 | requested modelが具体IDで記録され、observed model不明runはmodel別成功・費用統計から除外される。 | T1,T2,T3 | concrete/abstract model fixtures and policy inspection | Verified |
| AC8 | Lead tierを選ぶtaskは、低コストworkerへ安全に分離できなかった具体理由をrouting evidenceに持つ。 | T1,T2,T3 | blanket-vs-specific routing fixture | Verified |
| AC9 | DDD、orchestration、audit reference/validator、AGENTS.mdが同じ細粒度指示・AC単位review・escalation規則を持ち、全skill/test/format/diff gateが成功する。 | T1-T4 | validators、tests、final review | Verified |
| AC10 | coding worker指示がtest作成・更新、実装中の最小test、handoff前の関連regression、期待結果を指定し、成功実行または明示blockerを完了条件にする。 | T5 | policy inspection and skill validation | Verified |

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State | Routing | Audit | Result metrics |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | decomposition-before-routing、detailed worker contract、AC-grouped review、drill-down条件をcanonical skills/AGENTSへ追加する。AC1-AC6,AC8-AC9。 | Main | Lead | User agreement | orchestration/DDD skills、format、AGENTS.md | skill validators、policy scenarios | canonical workflow diff | Verified | Lead — process contract and integration | none | unavailable; retries 0; corrections 0; reviews 1 |
| T2 | compact audit/validatorを具体model ID、review group evidence、Lead非委譲理由、review overheadへ接続する。AC4,AC6-AC9。 | Main | Lead | T1 contract freeze | audit reference、validator、tests | positive/negative fixtures | 33 passing root validator tests | Verified | Lead — process contract integration shared with canonical policy | none | unavailable; retries 0; corrections 0; reviews 1 |
| T3 | Luna分解、Terra integration、Lead保持、粗いreview成功、詳細review escalation、過分割のforward scenariosを評価する。AC1-AC8。 | Main | Lead | T1,T2 | read-only fixtures | scenario outcomes | forward challenge table | Verified | Lead — final acceptance and independent workflow challenge | none | unavailable; retries 0; corrections 0; reviews 1 |
| T4 | 全差分、両validator、skill validation、format、audit overhead、zero-open-itemを最終確認してcommitする。AC9。 | Main | Lead | T1-T3 | change record and repository checks | final gates | Implemented record and commit | Verified | Lead — final acceptance | none | unavailable; retries 0; corrections 0; reviews 1 |
| T5 | coding workerのtest作成・実行責任とno-test-change例外をworker contract、DDD pre-implementation、AGENTSへ追加する。AC10。 | Main | Lead | User amendment approval | orchestration/DDD skills、AGENTS.md | skill validators and policy inspection | explicit test responsibility contract | Verified | Lead — process contract integration | none | unavailable; retries 0; corrections 0; reviews 1 |

## Design and task-split review

- Reviewer: Lead tier main agent.
- Inputs: current orchestration/DDD skills、agent audit workflow、recent delegated records、ユーザー提案。
- Decision: 設定変更よりdecomposition-before-routingを優先し、worker指示とreview粒度を分離する。既存AC↔Task mappingをreview groupとして再利用し、新規ledgerを増やさない。
- Cost boundary: microtaskは単独verificationとexclusive write ownershipがある場合だけ作成する。reviewはAC group一回をdefaultとし、C1/C3/C4/C5/C7のtrigger時だけ詳細化する。
- Model boundary: Leadが契約とrisk判断を保持する。frozen contract後の機械的sliceはLuna、bounded multi-file integrationはTerra候補とする。具体model利用が観測できないrunはmodel別評価に使わない。
- Agreement state: エージェントはC1-C7の処置案に合意し、ユーザーは2026-09-20にC1-C7とAC1-AC9を明示承認した。未解決の`Open decision`またはagent objectionはない。

## Documentation updates

- `.codex/skills/agent-task-orchestration/SKILL.md`: decomposition-before-routing、詳細worker contract、AC-grouped review、drill-down条件の正本として更新予定。
- `.codex/skills/agent-task-orchestration/references/execution-audit.md`: concrete requested model、review group、review overhead評価を更新予定。
- `.codex/skills/document-driven-development/SKILL.md`: AC contract freezeとAC-group checkpoint/final reviewへ更新予定。
- `.codex/skills/document-driven-development/references/change-record-format.md`: 既存AC↔Task mappingをreview groupとして扱う説明を追加予定。
- `scripts/audit_agent_execution.py` と `tests/scripts/test_audit_agent_execution.py`: delegated taskの具体model ID、Lead非委譲理由、AC review evidenceを必要な範囲で検証予定。
- `AGENTS.md`: model選択前の難易度低減とAC単位review原則を追加予定。

## Verification record

- 2026-09-20: working treeがcleanで、直近commitは`68def1b Document completion gate and agent routing review`だった。
- 2026-09-20: 現行skillは詳細worker promptを要求するが、Lead選択前の分解義務とAC-grouped review defaultを明示していないことを確認した。
- 2026-09-20: recent auditで具体model IDではなく`runtime-default`または抽象的worker名が記録され、observed modelがnullのrunがあることを確認した。
- 2026-09-20: root compact validator tests 33件、legacy skill audit tests 14件、DDD validator tests 10件が成功した。
- 2026-09-20: schema 2 positive fixtureは具体`gpt-5.6-luna`、AC membership、AC-group review、準備・統合・audit overheadを受理し、abstract model、AC mismatch、曖昧なLead理由、理由なし詳細review、不完全overheadを拒否した。
- 2026-09-20: `python scripts/audit_agent_execution.py --changed`、DDD change-record validator、orchestration/DDD quick validation、Python compile、`dotnet format HorseRacingPrediction.sln --no-restore --verify-no-changes`が成功した。

## Forward scenario review

| Scenario | Expected route/review | Evidence | Decision |
| --- | --- | --- | --- |
| identity・migration判断と確定済みmappingが混在 | Leadが判断をfreezeし、局所mappingだけLuna候補 | decomposition-before-routing rule | Pass |
| 同一snapshotを複数sliceが更新 | 一ownerへ統合または直列化 | exclusive owner/shared-file rule | Pass |
| worker promptに未決定contractが残る | RunnableにせずLeadへ返す | frozen decision/escalation contract | Pass |
| 複数microtaskがAC2を実現 | 統合後AC2 evidenceを一回review | AC mapping validator and ac-group mode | Pass |
| worker testだけがgreen | 独立反例不足としてdetail review | independent-evidence trigger | Pass |
| AC groupが原因帰属不能な大きさ | groupを分割し、failure taskへdrill down | closely-related/evidence-independent boundary | Pass |
| prompt・統合・reviewがworker節約を超える | slice統合またはtier昇格 | overhead fields and full-cost rule | Pass |
| observed modelが取得不能 | 品質結果は保持、model別成功/費用統計から除外 | telemetry-gap rule | Pass |

## Checkpoint review

- Reviewer: Lead tier main agent.
- Review groups: AC1-AC3（契約と分解）、AC4-AC5（group reviewとdrill-down）、AC6-AC8（cost/model/routing audit）、AC9（統合整合）。
- Inputs: approved AC/concerns、canonical policy diff、schema-2 validator/tests、legacy compatibility tests、forward scenarios。
- Decision: 全groupがintegrated evidenceで合格した。test implementation中のvalidator defectやscope逸脱はなく、detail-review triggerは発生しなかった。
- Cost finding: default reviewをmicrotask単位へ増やさず、schema 2は既存AC mappingを再利用する。追加記録はdelegated attemptごとのAC IDs、review mode、準備・統合・audit overheadに限定した。

## Final review

- Reviewer: Lead tier main agent.
- Scope: 承認済みC1-C7、AC1-AC9、T1-T4、全変更差分、検証結果。
- Decision: 全AC/Tが`Verified`で、未解決finding、open failure、scope外変更、受入れを阻害するexternal blockerはない。schema 1は後方互換、schema 2だけ新gateを強制する。
- Model conclusion: このprocess変更自体はcanonical contractとvalidatorを同時に統合するためLead保持が妥当。今後のcodingでは、frozen/local sliceを具体`gpt-5.6-luna`で要求し、観測不能runをmodel別評価へ混ぜない。

## Approved amendment: worker test responsibility

- Approval: ユーザーが2026-09-20に、coding workerへtest code作成とtest実行を指示できるよう明示的に追加依頼した。
- Contract: worker promptは変更に必要なtest、実装中の最小test、handoff前の関連regression、期待結果を指定する。test変更なしは理由と既存の独立証拠を必要とする。
- Completion: 指定testの成功結果がないworker taskは完了にせず、実行不能時はblockerを示してLeadへ戻す。
- Review: AC group reviewはworkerの自己申告ではなく、実行されたcommand/resultと独立証拠を確認する。

## Pre-implementation review

- Reviewer: Lead tier main agent.
- Inputs: Approved C1-C7、AC1-AC9、current orchestration/DDD/audit workflow、working-tree status。
- Decision: T1を`In progress`、T2-T4を`Dependent`とする。policy contractを先にfreezeし、validator/test、forward scenarios、final reconciliationの順に直列統合する。
- Worker decision: 本変更はcanonical policyとvalidator fixtureが同じ用語契約を共有し、変更規模も限定的なため主担当が実装する。将来のproduction changeでは本workflowによりLuna/Terra sliceを作る。
- Required evidence: weak/complete AC、ambiguous/frozen prompt、shared-file ownership、circular test、oversized review group、blanket Lead routing、runtime-default requested model、review-overhead scenarios。
- Escalation: AC group reviewがscope/security/data-integrity gateを弱める、またはmicrotask/ledger overheadが既存compact boundを超える場合は設計へ戻す。

## Deviations and follow-up

- 設計差分なし。model default設定は意図どおり変更せず、task decompositionと具体dispatch記録を変更した。
- 本変更ではsubagentを起動していないため実model/token telemetryは発生していない。次回以降のdelegated schema-2 auditで観測データを蓄積し、5件未満ではpersistent routing defaultを変更しない。

# Deploy preflight v2 collection routes

- Status: Implemented
- Change record schema: 2
- Owner: Collection platform maintainers
- Created: 2026-09-30
- Updated: 2026-09-30

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Verified | Pre-deploy state、pause、task drainを全てv2 contractへ変更し、focused testを更新した。 |
| Verification | Verified | 14 guard cases、workflow YAML parse、CI-equivalent formatting、diff/status checksが成功。 |
| Deployment/operation | Not applicable | Workflow dispatch、production deployment、pipeline resumeはこのchangeのscope外。 |

## Context

GitHub Actions `app-deploy` run `36586181257` は `deploy-collector-lambda` の `Pause and drain collection before changing deployed versions` で失敗した。workflowは `GET /api/admin/collection/pipeline` を呼び、production APIはHTTP 404を返した。直前の成功run `36579828265` はAPIをv2-only route surfaceへ更新しており、以後のpre-deploy guardにもv2 contractが必要である。

現行workflowとtestは、cutover前はv1 APIが稼働するという一回限りの前提を保持している。この前提は最初のv2 deploymentでは成立したが、v2 deployment後の次回runでは成立しない。

## Goals

- `deploy-collector-lambda` のpre-deploy pause/drainを、現在稼働するv2 Collection Platform APIで反復実行可能にする。
- 状態取得、一時停止、実行中タスク確認のmethod、path、payload、response shapeをproduction v2 contractと一致させる。
- 契約testがv1 routeへの後退を拒否するようにする。

## Non-goals

- GitHub Actionsの再実行、push、production deployment。
- 現在pause中のcollection pipelineのresume。
- API側でv1 compatibility routeを復活させること。
- post-deploy guard、Terraform、collector、application behaviorの変更。

## Hypothesis ledger

| ID | Claim | Fact/inference boundary | Supporting and contradicting evidence | Falsification check and result | Disposition |
| --- | --- | --- | --- | --- | --- |
| H1 | 最新runの直接原因はpre-deploy guardのv1 GETが404を受けたこと。 | Direct fact。404の理由がroute removalであることはsourceと直前runを結合したinference。 | Run `36586181257` は最初のcurlでHTTP 404/exit 22。current API sourceはv2 routeのみ。直前runはv2 post-deploy checksに成功。反証するv1 registrationは見つからない。 | Failed step log、current endpoint source、workflow literalをread-only照合し一致。 | Confirmed; design premise。 |
| H2 | pre-deploy guard全体をv2へ移す必要がある。 | Contract comparisonに基づくinference。 | v2はGET `/pipeline-state`、PUT `/pipeline`、GET `/tasks` page shapeを持つ。現行guardはv1 GET/POST/array shape。 | 有限な3 operationをsourceとworkflowで照合し、全3件にcontract差を確認。 | Confirmed; inventoryで全件更新。 |
| H3 | application code変更は不要。 | Source inspectionに基づくinference。 | v2 endpointsとpost-deploy v2 consumerは既に存在し、直前runで成功。 | current route sourceとsuccessful run `36579828265` のpost-deploy v2 callsを照合。 | Confirmed; workflow/test-only scope。 |

## Finite public-contract inventory

Pre-deploy guardが使用するcollection API operationは次の3件で、全件をscope内とする。

| ID | Current workflow contract | Disposition | Approved v2 target | Preserved invariant and consumer evidence |
| --- | --- | --- | --- | --- |
| PF-01 | GET `/api/admin/collection/pipeline` | Replace | GET `/api/v2/admin/collection/pipeline-state` | `isPaused` booleanを読み、元のpause状態をoutputへ保存する。`GetCollectionPipelineEndpoint`とpost-deploy guardがconsumer evidence。 |
| PF-02 | POST `/api/admin/collection/pipeline/pause` with `{reason}` | Replace | PUT `/api/v2/admin/collection/pipeline` with `{paused:true,reason}` | 未pauseの場合のみdeployment reason付きでpauseし、失敗時は後続mutationへ進まない。`SetCollectionPipelineEndpoint`がcontract evidence。 |
| PF-03 | GET `/api/admin/collection/tasks?status=Running&limit=1`, top-level array | Replace | GET `/api/v2/admin/collection/tasks?status=Running&limit=1`, paged `.items` array | Running taskが0になるまで最大60回、10秒間隔で待ち、timeout時はpipelineをpauseしたままfail closedする。`ListCollectionTasksEndpoint`とpost-deploy guardがconsumer evidence。 |

Reconciliation: discovered 3、in scope 3、replace 3、unchanged 0、out of scope 0。重複・未分類なし。

## Documentation updates

- このchange recordを新規作成する。
- `docs/changes/20260927_collection-rest-api/README.md` を確認した。v2 routeのcanonical inventoryは既に正しく、更新不要。
- `docs/changes/20260928_production-collection-starvation/README.md` を確認した。v1 pre-deploy assumptionと過去の判断を記録するhistorical recordであり、履歴は書き換えない。本recordがrepeat deploymentで判明した後続修正を所有する。
- その他のarchitecture/operator documentにこのpre-deploy route assumptionをcanonical ruleとして定義するものは見つからず、非change-record文書の更新は不要。

## Technical impact and decisions

1. `.github/workflows/app-deploy.yml` のpre-deploy baseを `/api/v2/admin/collection` にする。
2. pipeline state readをGET `/pipeline-state` にする。
3. pauseをPUT `/pipeline`、JSON `{ "paused": true, "reason": "deployment version transition" }` にする。
4. Running task responseはtop-level arrayでなく`.items`のlengthを検証する。
5. `tests/scripts/test-deploy-pipeline-state.ps1` のpre-deploy stub/assertionをv2 method/path/payload/shapeへ更新し、v1 literalを拒否する。
6. fail-closed behavior、元のpause状態の保存、60 x 10秒のdrain boundは変更しない。

Rejected alternative: v1とv2を順次fallbackする実装。404時のfallbackはroute/configuration regressionを隠し、すでにv2へcutover済みのsingle production environmentには不要な複雑性を加えるため採用しない。

## Concern and agreement ledger

| ID | Concern and evidence | Impact | Proposed disposition | AC/task/test | Agent position | User disposition | State |
| --- | --- | --- | --- | --- | --- | --- | --- |
| C1 | productionは既にv2-onlyで、v1 preflightはrepeat deploymentで404になる。 | 次回以降のdeploymentがAPI/Lambda更新前に必ず停止する。 | 3 operationをすべてv2へ置換し、v1 literalをtestで拒否する。 | AC1-AC3/T1; focused guard test。 | 推奨。 | 2026-09-30、設計全体を承認。 | Resolved in design |
| C2 | pipelineは直前成功runで既にpause状態だった。 | workflow修正とresumeを混同すると未調査のoperational stateを変更し得る。 | 本changeはcode/testのみ。resume/deployは明示的にscope外とする。 | AC4/T1; diff review。 | 強く推奨。 | 2026-09-30、設計全体を承認。 | Resolved in design |
| C3 | local stubだけではproduction reachabilityを証明しない。 | test成功のみでdeployment成功を断定できない。 | acceptanceはcontract/static verificationまでとし、remote runは別途明示依頼があった場合のみ実施する。 | AC3-AC4/T1。 | 推奨。 | 2026-09-30、設計全体を承認。 | Resolved in design |

Open decisionと未解決のagent objectionはない。残存riskは、code修正後もpush/deploymentを行うまでremote outcomeが未確認であることだが、これは本changeの明示的なnon-goalでありACを阻害しない。

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | Pre-deploy guardがPF-01とPF-02のv2 method/path/payloadを使い、元のpause状態を保存する。 | T1 | Guard testのrunning/paused/get-failure/invalid-state/pause-failure cases。 | Verified |
| AC2 | Pre-deploy guardがPF-03のpaged `.items` shapeでRunning taskを判定し、未drain時はbounded timeout後にfail closedする。 | T1 | Guard testのrunning/not-drained casesとworkflow source assertion。 | Verified |
| AC3 | Testがpre-deploy v1 base、`/pipeline/pause`、top-level task arrayへの後退を拒否し、workflow YAMLがparseできる。 | T1 | Focused PowerShell testとPython YAML parse。 | Verified |
| AC4 | 差分はworkflow、focused test、本change recordに限定され、deployment、resume、application/API code変更を含まない。 | T1 | `git diff --check`、`git status`、scoped diff review。 | Verified |

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | v2 pre-deploy guardと契約testを実装し、recordへ結果を反映する。 | Main/Lead | High capability; 単一の小さなworkflow/test契約変更で、同じ2 fileを分割するとwrite ownershipと統合costが増すため非委譲。 | User approval | `.github/workflows/app-deploy.yml`; `tests/scripts/test-deploy-pipeline-state.ps1`; 本README | Focused guard test、YAML parse、`dotnet format HorseRacingPrediction.sln --no-restore --verify-no-changes`、`git diff --check`、status/diff review。 | Commandsと結果、AC matrix、final reviewを本recordへ記録。 | Verified |

## Review gates

- **Design and task-split review (2026-09-30, Main/Lead):** 3件の有限contract inventoryをsource/run evidenceへ照合した。ACはhappy path、既pause、GET/pause failure、invalid state、undrained timeout、v1 regressionをcoverする。単一taskの同一workflow/test ownershipであり、委譲・並列化は総costを増やすため不採用。
- **Concern and agreement review (2026-09-30, Main/Lead):** requirement conflict、API contract、security/credential exposure、destructive/operational action、compatibility、recovery、test blind spotを確認。credential値は扱わず、production mutationはscope外。C1-C3によりmaterial concernはdesign内で解決し、userは2026-09-30に全dispositionを承認した。
- **Pre-implementation review (2026-09-30, Main/Lead):** Userの「お願いします」を、本recordに要約したAC1-AC4とC1-C3 dispositionへの明示承認として記録。T1は`In progress`、依存なし。exclusive write scopeはworkflow、focused test、本record。testは既存14 guard casesをv2 contractへ更新し、v1 regressionをstatic assertionで拒否する。期待結果は全case PASS、YAML parse成功、formatter/diff checks成功。外部contractまたはscope変更が必要なら`Proposed`へ戻す。
- **Checkpoint review (2026-09-30, Main/Lead):** AC1-AC3 groupについてworkflow diffを有限inventory PF-01-PF-03へ一対一照合。14 guard casesはhappy/paused、state/pause/task failures、invalid state/task shape、undrained timeoutを通過。v2 source assertionsとYAML parseも成功し、設計逸脱なし。
- **Final review (2026-09-30, Main/Lead):** T1とAC1-AC4は全てVerified。scoped diffはworkflow、focused test、本recordのみ。既存の無関係な`.codex/skills/agent-task-orchestration/SKILL.md`変更と`deployment-duration-audit.md` untracked fileは差分・commit対象から除外した。deployment/resume/application code mutationなし。未完了の承認scope、open finding、acceptance-blocking external blockerなし。

## Verification record

Design evidence:

- GitHub Actions run `36586181257`: pre-deploy GET v1 route returned HTTP 404 and curl exit 22 before infrastructure/application mutation。
- GitHub Actions run `36579828265`: preceding deployment succeeded; post-deploy v2 state/tasks calls succeeded。
- Current source exposes GET `/api/v2/admin/collection/pipeline-state`、PUT `/api/v2/admin/collection/pipeline`、GET `/api/v2/admin/collection/tasks`。

Implementation verification, 2026-09-30:

- `pwsh -NoProfile -File ./tests/scripts/test-deploy-pipeline-state.ps1 -Bash 'C:\Program Files\Git\bin\bash.exe'` — PASS、post-deploy 7 casesとpre-deploy 7 casesの計14件。
- `python -c "import pathlib,yaml; yaml.safe_load(...)"` — PASS workflow YAML parse。
- `dotnet format HorseRacingPrediction.sln --no-restore --verify-no-changes` — exit 0、変更なし。
- `git diff --check` — exit 0。line-ending conversion warningのみでwhitespace errorなし。
- `scripts/validate_change_records.py` はrepositoryに存在せず実行不能。単一・非委譲taskのためorchestration audit validatorは必須でなく、record schema/status/AC/task整合をmanual final reviewで確認した。
- CodeGraph syncはproduction symbol/call relationshipを変更しないworkflow/test/doc-only changeのため不要。

## Deviations and follow-up

承認済み設計からの逸脱なし。Remote GitHub Actions runは明示したnon-goalのまま未実施。

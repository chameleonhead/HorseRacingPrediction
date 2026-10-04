# 既存障害で収集再開を見送ってもデプロイを成功扱いにする

- Status: Implemented
- Change record schema: 2
- Owner: Collection operations
- Created: 2026-10-03
- Updated: 2026-10-05

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Complete | 再開見送り時にGitHub noticeを出してexit 0とし、既存workflow harnessの期待値も更新した。 |
| Verification | Complete | 2026-10-05にUbuntu24.04上の実workflow shell harness全14ケース、audit、diff hygieneを確認。 |
| Deployment/operation | Not applicable | このrecordのACはworkflow/harness変更。20261004_collection-outbox-query-indexの承認済みデプロイへ別checkpointとして含め、運用検証はそちらで追跡する。 |

## Context

Run [37104700043](https://github.com/chameleonhead/HorseRacingPrediction/actions/runs/37104700043) はverify、image build、
Collector Lambda deploy、API restart、HTTP 200 health checkまで成功した。失敗したのは最後の
`Restore collection pipeline state after deployment` だけであり、Actionable failureが存在したため停止を維持して
exit 1となった。2026-10-03 14:41 JSTの `SubjectNotIdentified / MultipleCandidates` が再開見送りの候補だが、当時のfailure IDは
workflow logに出ておらず特定未確認である。

workflowの安全条件は、デプロイ前に稼働中で、deployment後のrunning taskが0件、APIが正常、pipeline pause確認済み、
Actionable failureが0件なら自動再開すること。Actionable failureが残る場合にpipelineを停止したままにする挙動は維持し、
この正常な再開見送りだけをActions成功として報告する。

## Goals

- 通知が残る場合、pipeline pauseを維持したままdeployment workflowをexit 0で終える。
- GitHub Actionsのnoticeとstep logで、collection pipelineが未再開であり復旧判断待ちだと明示する。
- pipeline/API状態取得失敗、invalid payload、pause/drain失敗、health check失敗、再開API失敗は引き続きworkflow failureとする。

## Non-goals

- Actionable failureがある状態で自動的にpipelineを再開する。
- pipeline pauseを解除する、failure notificationを消す、またはfailureをretryする。
- 本番状態を変更する、workflowをdispatchする、またはデプロイする。

## Hypothesis ledger

| ID | Claim | Fact / inference boundary | Falsification and result | Disposition |
| --- | --- | --- | --- | --- |
| H1 | runはAPI restartまたはhealth check失敗で止まった。 | Logs show restart success and health check HTTP 200. | 指定jobのstep一覧/logを確認。仮説は否定。 | 再開見送りstepのみ修正。 |
| H2 | workflow failureはActionable failure検出のfail-closed branchによる。 | Step log prints exact message immediately before exit code 1. | workflow shell condition and current test harnessを照合。支持。 | この分岐はexit 0 + visible noticeとする。 |
| H3 | 残存failureは既存の過去通知だった。 | 2026-10-03 14:41 JSTに別調査対象のfailureが存在したが、runは16:12 JST。通知IDのrun-time snapshotなし。 | 当時のfailure responseは保存されておらず特定不能。 | 原因通知の同一性は未確認として残す。ACは通知IDに依存させない。 |

## Decisions

1. Actionable failureが再開確認で見つかった場合、pipelineはpausedのまま保ち、GitHub Actionsはnoticeを出して成功終了する。
2. API呼出、state validation、drain、deployment health、実際のresumeが失敗した場合は従来どおりnon-zeroとする。
3. GitHubの緑表示はdeployment成功を示すがcollectionが稼働中とは限らないため、notice本文でpause状態を明記する。
4. Production code変更と同じPR/checkpointで既存workflow harnessの期待値を更新する。Production deploymentはこの依頼に含めない。

## Concern and agreement ledger

| ID | Concern and evidence | Impact | Proposed disposition | AC/task/test | Agent position | User disposition | State |
| --- | --- | --- | --- | --- | --- | --- | --- |
| C1 | GitHub Actionsが緑でも、collection pipelineはpausedである可能性がある。 | 運用者が収集再開済みと誤解する。 | 明示的なGitHub notice/logを表示し、resume APIを呼ばずpauseを保つ。 | AC1; retained-failure counterexample | Acceptable only with explicit paused notice | ユーザーは「エラーとはせず正常に終了」と明示。noticeでpause状態を表示する | Accepted risk |
| C2 | APIやhealth checkの失敗まで成功扱いするとdeployment failureを隠す。 | 不健全なdeploymentを成功と報告する。 | 変更をActionable failure branchに限定し、その他のset -e/fail-fastは維持する。 | AC2; API get failure and resume failure counterexamples | Must preserve fail-closed behavior | ユーザー指示の範囲外として従来どおりfailure | Resolved in design |

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | Actionable failureがあるとpipelineをpausedのまま保ち、Actions noticeで未再開を示し、stepはexit 0になる。 | T1 | workflow shell harness retained-failure case | Verified |
| AC2 | pipeline read/validation、running-task check、health、pause、resumeの失敗は従来どおりnon-zeroで終わり、Actionable failureなしならresumeする。 | T1 | workflow shell harness existing fail-closed and resume cases | Verified |

## Documentation updates

- `docs/11-automation-design.md`: deployのActionable failure branchはpipeline pauseを保ちながらnotice付きの正常終了とし、通常のdeployment/API/health失敗は失敗のままにする、と記載する。
- 本change recordをこのworkflow behaviorのincident-specificな要件、concern disposition、検証状態の正本とする。

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State | Routing | Audit | Result metrics |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | 再開見送りを成功終了へ変更し、notice、既存shell harness期待値、設計文書を同期する。AC1-AC2。 | Lead — single short task | Cheap executor default requested by repository; available executor not separately observable in this environment | User instruction on desired outcome | `.github/workflows/app-deploy.yml`, `tests/scripts/test-deploy-pipeline-state.ps1`, `docs/11-automation-design.md`, this record | `pwsh ./tests/scripts/test-deploy-pipeline-state.ps1 -Bash /bin/bash` (expected retained-failure exit 0/no resume; true failures non-zero); `git diff --check` | Ubuntu24.04 LF-checkout-shaped harness14/14 pass and Lead integrated diff/AC review | Verified | Lead — single short task; read-only Luna verification helper | none | unavailable; retries 1 environment; corrections 0 source; reviews 1 |

## Readiness and pre-implementation note

- `agent-task-orchestration` was loaded in this turn. Purpose: report a policy-driven collection resume deferral as successful deployment while leaving collection paused and explaining that state.
- Repository findings: the deployment has already passed image deployment and API HTTP 200; only the `Actionable failures remain` guard returns 1. The existing shell harness extracts and executes the exact workflow script and currently models this branch as failure.
- Ordered edits: change the actionable-failure branch to emit `::notice::` and exit 0; update the existing harness's expected exit and assert no resume; update canonical automation design and this record.
- Unresolved specification questions: none; user explicitly requested successful GitHub Action completion for this case.
- Verification commands: `pwsh ./tests/scripts/test-deploy-pipeline-state.ps1 -Bash /bin/bash` expected branch behavior; `git diff --check` expected clean. Per developer instruction, tests will not be executed in this task; verification remains incomplete until authorized/run.
- Route: single lead-owned short workflow edit; no delegation because one shell branch, its existing harness, and one canonical paragraph form a tightly coupled small change. Requested/observed model telemetry is unavailable and is not inferred.
- Audit: lead-only task; `none`. Change-record validator is applicable and will be run before the first workflow implementation edit.
- User authorization: the explicit request to end normally is recorded as approval of AC1-AC2 and C1-C2 dispositions above. Production state, push, and deployment are excluded.

## Verification record

- Investigation: GitHub run `37104700043` showed API health HTTP 200; final restore step logged `Actionable failures remain; pipeline stays paused for explicit recovery` and exited 1.
- `git diff --check`: passed for the complete worktree diff.
- `python scripts/audit_agent_execution.py docs/changes/20261003_deploy-with-paused-collection/README.md`: valid before implementation and after the change.
- Workflow shell harness: intentionally not executed under current developer instruction.
- Production deployment/post-deploy observation: not requested and not performed.

## Deviations and follow-up

- The actual notification ID that triggered the run-time guard is not present in logs and remains unknown. This does not affect the requested exit-code behavior.
- Local production deploy/worktree is not updated until a deployment is separately requested.

## 2026-10-05 verification closure

The earlier test-skip and no-deployment statements above describe the original turn, not a current prohibition. Under the current approved index recovery, read-only verifier `verify_deploy_guards` was dispatched with requested gpt-6-luna/high (observed model/tokens unavailable). It ran the exact harness in official `mcr.microsoft.com/dotnet/sdk:10.0.202-noble`: Ubuntu24.04.4, PowerShell7.6.0, Bash5.2.21. All fourteen restore/pause/drain/failure cases passed, including retained-failure notice/exit0/no-resume. Repository files were not modified by the verifier.

Initial direct Windows worktree mount failed because Bash received CRLF (`pipefail\r`). `git ls-files --eol` confirmed index LF/worktree CRLF. Container-only copies normalized to Git Ubuntu-checkout LF then repeated the exact harness successfully. This is a proven environment mismatch, not a product correction. Lead accepted the integrated existing diff against AC1–AC2; audit/diff gates and final global formatter remain required before checkpoint commit. No safety behavior besides the approved notice exit status changed. This code-only record's ACs are closed; the canonical deployment and production operation evidence remain owned by the index recovery record, not silently declared done here.

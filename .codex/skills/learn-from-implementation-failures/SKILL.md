---
name: learn-from-implementation-failures
description: Analyze a demonstrated implementation or delivery failure, distinguish immediate and systemic causes, and make the smallest evidence-based updates to relevant repository skills. Use when the user asks why work went wrong, requests lessons learned, or asks Codex to improve its development process after a mistake.
---

# Learn from Implementation Failures

Turn observed failures into narrow, testable improvements to future decisions. Do not use hypothetical risks alone as justification for adding rules.

## Workflow

1. Reconstruct the intended outcome from the approved requirements, acceptance criteria, and user corrections.
2. Establish what actually happened from code, tests, tool output, commits, and review findings. Separate verified facts from inference.
3. For each material failure, identify the incorrect outcome, immediate technical cause, workflow failure that allowed it, missing check, and an observable future gate.
4. Group repeated symptoms under one systemic cause. Avoid creating one rule per bug.
5. Update the narrowest existing skill whose decisions should change. Create a specialized skill only when the capability is independently reusable.
6. Preserve authorization and approved design. A retrospective does not grant permission to change unrelated code, external systems, or user data.
7. Validate every modified skill with the skill-creator validator. Inspect the final diff for generic platitudes, duplicated policy, unfinished placeholders, and unverifiable rules.
8. Report the causes, changed skills, and the concrete behavior that will differ next time.

## Delegation-failure recovery

通常のモデル選択、委譲、並列化、worker成果の採用は
`agent-task-orchestration` の責務とする。このスキルは、委譲後に観測された失敗を
分析し、再分割またはモデル昇格が必要かを決める。

委譲タスクが失敗した、受入れ基準を満たさない、根拠不足で採用できない、または
同種の失敗を繰り返した場合は、次の順で記録する。

1. 承認済みの受入れ基準、workerの入力、diff、テスト、レビュー指摘を突き合わせ、事実と推測を分ける。
2. タスクを独立して検証できる成果単位へ再分割する。共有ファイル、未確定の設計判断、複数境界を跨ぐ処理を一つのworkerへ戻さない。
3. 再分割後も曖昧さ、統合負荷、失敗率、レビュー負荷が高い場合は主担当または上位モデルへ昇格する。
4. change recordの実行計画またはVerification recordに、原因、再分割/昇格の判断、再試行回数、レビュー指摘、再検証結果を追記する。

再分割の完了条件は、各新タスクに owner、依存、書込範囲、検証コマンド、完了証拠があり、
失敗した元タスクとの対応を追跡できることである。モデル単価だけでなく、初回受入率、
再試行、レビュー負荷、修正時間を根拠にrouting ruleを狭く更新する。

Delegated coding の失敗分析では、元の agent audit に escaped defect、原因、reviewで見逃した gate、worker/reviewer/rework usage、active review/correction/re-verification timeを追記する。worker tokenだけで安価だったと結論しない。比較可能な成功標本が5件未満ならpersistent defaultは変更せず、重大なsecurity/data/scope/false-completion failureは標本数に関係なく該当する低コストrouteの停止候補にする。

Persistent improvementはmodel、reasoning、budget、task boundaryのうち一段階だけを変更し、観測期間、独立forward test、rollback conditionを持たせる。人間工数を金額へ換算するのは利用者または組織が換算単価を明示した場合だけとし、取得不能なprovider telemetryやreview timeを推測しない。

## Repeated interruption and autonomy review

承認済みの安全な作業が残る状態でチェックポイント停止を繰り返した場合は、停止パターン自体を失敗として扱う。

- 残りの受入れ基準を依存グラフへ展開し、`Runnable`、`Dependent`、`Externally blocked` を記録する。
- runnable項目を実行できないまま停止した理由をchange recordへ記録し、同じ停止を防ぐ再分割または昇格を行う。
- 共有worktreeのビルド破壊、クロスエージェントの待ち状態、長すぎるタスクが見つかったら、独立検証可能な本番経路単位へ切り分ける。
- コミット、部分テスト、コンテキスト圧縮、サブタスク完了は停止条件ではない。次のrunnable項目と検証を実行計画へ記録する。

観測可能な修正は、複数チェックポイントをまたいで作業が継続し、実行計画と受入れ基準マトリクスに継続または阻害の理由が残ることである。

### Review-to-execution closure gate

When a numbered or unnumbered review, gap report, audit, or self-review identifies a material finding within the approved outcome and the user authorizes implementation, make it an explicit closure item. Apply the same rule to material findings from a pre-completion self-audit. Hypotheses, exploratory notes, and unrelated suggestions are not closure items.

- Copy each finding into the durable execution plan with one of: `Proposed`, `Runnable`, `In progress`, `Dependent`, `Externally blocked`, `Rejected with reason`, or `Verified`. Do not silently reinterpret “implement the proposal” as permission to implement only the highest-priority subset.
- Give every item observable completion evidence. If a finding is intentionally combined with another task, retain a mapping from both original findings to the shared evidence.
- After each checkpoint, reconcile the original review list against commits, tests, runtime verification, and the acceptance matrix. A passing build or a polished primary screen does not close findings about pagination, large-data behavior, secondary workflows, operational cutover, or browser validation.
- Before a completion response, run a zero-open-item check. If any authorized item is still runnable or dependent on another local item, continue. If an item requires external access or destructive production authority, report it as an explicit external blocker and do not describe the whole proposal as completed.
- When a new self-review finds additional defects within the approved outcome, add them to the same closure ledger before fixing them; do not leave them only in commentary or the final response.

### Pre-completion process-failure gate

If the pre-completion self-audit finds an omitted acceptance item, unsupported worker result, incorrect routing decision, or missing process gate, reconstruct the finding from the plan, diff, tests, tool output, or review evidence and separate fact from inference. Fix an isolated implementation or test defect in the implementation. Update the narrowest applicable skill only when the evidence shows a reusable decision or workflow gap likely to affect future tasks; then run the skill-creator validator and rerun the affected verification gate. Do not change a skill for hypothetical risks alone.

The observable gate is a one-to-one ledger from reported findings to completion evidence or a genuine external blocker, with no untracked “remaining work” introduced only after completion was claimed.

### Late concern and consensus failure gate

When a material concern is first surfaced after a change was marked complete, determine whether it was truly unknowable before implementation or was foreseeable from the requirements, design, code, or verification plan. If it invalidates an acceptance criterion or completion evidence, reopen the originating change record to `Approved`, move affected ACs to `Connected`, add explicit closure tasks, and identify the prior final-review decision as superseded. Do not leave the correction only in a follow-up record.

For a foreseeable concern, record which pre-approval dimension or counterexample was omitted and update the narrowest planning/review skill so future change creation surfaces it before approval. Add a concern ledger entry with the agent's technical position and the user's disposition. An `Open decision` or unresolved evidence-based objection blocks approval; low-risk changes with no material concerns may use a concise reviewed-none statement. Validate the skill and replay both a material-conflict scenario and a low-risk scenario so the gate does not become indiscriminate process overhead.

### Resume protocol after an interruption

When work resumes after a user interjection, context compaction, tool failure, process restart, or another interruption, do not reconstruct the task from memory or restart completed work.

1. Read the latest user request together with the active approved change record and durable execution plan.
2. Inspect repository evidence (`git status`, the latest relevant commits, and focused diffs), runtime evidence (health/process state when applicable), and the most recent verification results.
3. Classify each planned item as complete with evidence, currently modified, runnable next, or genuinely blocked. Preserve unrelated user changes.
4. Continue from the first runnable unmet acceptance criterion. Re-run only the smallest verification needed to establish whether previously completed work is still valid.
5. Before another possible interruption, record the exact next action, verification command, runtime identifiers, and files intentionally left uncommitted in the durable plan or change record.

For open-ended quality requests such as “iterate until it looks good,” define an observable loop before editing: inspect the rendered result, name the largest remaining usability or visual defect, make one coherent adjustment, and render again. Stop only after a full pass finds no material defect in hierarchy, density, alignment, responsive behavior, interaction feedback, or accessibility—not merely after the first successful build.

The recovery gate is that a new agent or compacted continuation can identify the next command and remaining acceptance gap from repository artifacts and current state in under one inspection pass.

## Quality bar

A correction must change a future decision and have an observable completion condition. Prefer gates that trace a requirement through its production entry point and terminal side effect, exercise the real adapter or dispatcher, prove removed infrastructure has zero runtime callers, distinguish checkpoints from completion, or verify destructive cutover ordering.

Do not add reminders such as "be careful" or "test thoroughly." Replace them with a specific artifact, query, test, or invariant.

## Observed verification failure closure gate

Treat every failure observed in an approved change's required verification commands as an open completion item, even when it is not caused by the current diff or a focused rerun later passes.

- On the first failure, add one ledger entry to the change record with the original command, failing test or step, observed output, and current classification: deterministic regression, environment mismatch, order/load-dependent failure, unrelated confirmed defect, or unknown. Do not classify from a passing rerun alone.
- Close the entry only with a verified cause and disposition: fix the product/test/environment and rerun the original failing command successfully; or prove with repository evidence that the defect is unrelated to the approved outcome and assign it to an explicit follow-up owner without blocking any acceptance criterion. “Could not reproduce,” “passed alone,” and “likely flaky” are not dispositions.
- After fixing one failure, reconcile the ledger before pushing or declaring completion. A later failure from the same original verification run is not a new surprise if it was already recorded; it remains an open item until independently closed.
- For intermittent failures, reproduce under the dimension suggested by the evidence—full-suite concurrency, repeated execution, runner OS/time zone, shared state, ordering, or network boundary—and remove the uncontrolled dependency or assert the invariant. Increasing a timeout or retry count is acceptable only when measured behavior establishes the intended bound and the test still detects a stuck operation.
- Before final handoff, require both zero open verification failures and a successful rerun of every original command that produced one. Include remote workflow runs when push health is part of delivery.

The observable correction is a failure ledger in which each observed failure has cause evidence, disposition, and a successful rerun of its original command; an isolated green rerun cannot erase an earlier red full-suite result.

## CI parity and push closure

When a pushed change fails continuous integration after local verification, treat the mismatch between the local gate and the repository workflow as the demonstrated delivery failure.

- Read the failing workflow step and its raw log before changing code or the skill. Record the exact command, runner OS, failure location, and whether the local verification omitted or changed any of them.
- Before a later push, inspect the repository workflows and run their required formatting, build, and test commands in workflow order, including configuration, filters, restore behavior, and generated-file checks. A broader-looking command is not a substitute when its flags or prerequisites differ. After source or test edits, never use `--no-build` until the workflow-equivalent build has regenerated the binaries under test.
- Treat every formatter output as a code change: run all workflow steps that follow that formatter again. Contract tests for formatted configuration must assert parsed meaning or whitespace-tolerant structure, not formatter-controlled column alignment.
- If the failure touches paths, casing, line endings, locale, time zones, process behavior, or other platform-sensitive APIs, add an OS-neutral invariant test. Use framework path APIs instead of string normalization assumptions. When a matching runner or container is available, execute the affected test there; otherwise state that cross-OS parity remains unverified.
- Treat a successful `git push` as dispatch, not completion, when the requested outcome includes a healthy GitHub Actions run. Watch the new commit's required workflows to a terminal state, inspect every failure, fix it, rerun the exact local gates, and push again until they pass or an external blocker is established.
- Add the repository's exact pre-push commands to the durable change record or contributor guidance when they were previously absent, so the next agent does not have to rediscover the CI contract from a failed run.

The observable correction is that the same commit passes the locally reproduced workflow commands and the remote required workflows, with any platform-sensitive fix covered by a test that executes on the CI runner OS.

## External adapter fidelity gate

When a production failure occurs because an external page, payload, or navigation graph differs from a test fixture, treat fixture fidelity and terminal validation as separate gates.

- Capture the smallest production-shaped evidence that distinguishes the failure: candidate count and ordering, labels/attributes, resolved URL, identified page kind, and requested resource identity. Do not reduce a multi-candidate production structure to a single ideal candidate in the regression fixture.
- When multiple elements jointly express one semantic target—such as a resource number link and a resource-type link sharing one URL—preserve those elements separately in the fixture and test their relationship. Do not merge them into one convenient synthetic label; select by the stable relation (for example, same resolved URL) and still validate the terminal resource kind and identity.
- For selection logic, include at least one ambiguous fixture where a plausible wrong candidate appears before the correct candidate. Assert the semantic selection criterion and the terminal outcome (for example, a RaceCard request produces a validated RaceCard page for the requested race), not merely that some URL was extracted or navigation returned HTTP 200.
- If browser/session state can choose a shortcut, cache, or sibling-navigation path, reproduce and verify the cold-session production entry path first, then verify the warm path separately. A warm-path success cannot close a failure observed on first navigation.
- When resolving root-relative external URLs, do not rely on `UriKind.Absolute` alone: runtime platforms can classify `/path` differently. Accept absolute HTTP(S) explicitly, otherwise resolve against an HTTP(S) base URL, and keep CI coverage on every supported deployment OS.
- Preserve an external link's raw href through the snapshot boundary and prefer it over a runtime-parsed `Uri` when later resolution depends on page origin. Normalization should occur once against the known HTTP(S) base, not implicitly while capturing platform-neutral evidence.
- Inventory whether live-site tests are excluded by the normal CI filter. If they are, record and run a bounded pre-release smoke test for the changed adapter against a current, non-destructive target, or explicitly record that live compatibility remains unverified. A passing fixture suite must not be reported as proof of current external-site compatibility.
- When production logs reveal a richer structure than the stored fixture, add or refresh a sanitized fixture and its regression test as part of the fix. Keep volatile tokens and URLs out of identity assertions; validate stable semantics and page identification instead.
- Verify failure classification as well as success: a resolved page of the wrong kind or identity must become `UnexpectedPage`/validation failure with the observed URL and page kind, rather than a generic exception that hides the selection defect.

The observable correction is a regression test that fails with the production-shaped ambiguity, passes only when the correct semantic candidate is chosen, and proves the requested resource type and identity at the workflow boundary.

## Behavior-preservation gate for infrastructure migrations

When an existing capability stops working after moving it onto a new queue, scheduler, collection platform, or execution path, do not describe an internal shortcut as an intentional specification change unless the approved change record explicitly changed that user-visible behavior.

- Inventory the old production path's observable guarantees before replacing it: semantic target selection, fallbacks, terminal validation, persistence side effects, and operator-visible diagnostics. Map each guarantee to the new path or record an approved removal.
- For optimizations such as direct URLs, caches, batching, or fewer browser navigations, test them as alternate routes to the same terminal contract. The optimization is invalid if it can return a different resource kind or identity, even when it is faster and HTTP succeeds.
- Add a migration regression test that enters through the new production handler and proves the same terminal domain result as the old path. Unit tests for intermediate URL capture, queue publication, or parser invocation do not satisfy this gate alone.
- During review, classify a changed behavior as one of `approved change`, `preserved`, or `regression`. Any unapproved change in a previously working flow is a regression and must be fixed before declaring the migration complete.
- When an implementation introduces an assumption that the old path did not require (for example, “the first anchor is the desired resource”), require production-shaped evidence for that invariant. Without evidence, retain semantic selection or validation from the old path.

The observable correction is a behavior map plus an end-to-end regression test demonstrating that infrastructure replacement preserved each approved user-visible capability.

## Boundaries

- Do not rewrite a skill solely to rationalize an isolated typo or unpredictable external failure.
- Do not weaken acceptance criteria to make prior work appear complete.
- Do not mark the underlying implementation fixed unless it was separately corrected and verified.
- Keep failure-specific narrative in the relevant change record and reusable decision rules in skills.

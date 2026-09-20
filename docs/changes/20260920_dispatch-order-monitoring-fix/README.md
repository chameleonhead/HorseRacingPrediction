# DispatchOrderViolation の偽陽性と優先度逸脱を修正する

- Status: Approved
- Change record schema: 2
- Owner: Main
- Created: 2026-09-20
- Updated: 2026-09-20

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Verified | commit `62eb745`でdispatch候補証拠、effective-priority envelope代表、group上限時のpriority admissionを修正した。 |
| Verification | In progress | 初回CI成功後、review P2のdispatch時点candidacy指摘を修正。再回帰と更新CIが残る。 |
| Deployment/operation | Externally blocked | production deployは明示的許可の対象外。配備後read-only監視で2 fingerprintの消失確認が必要。 |

## Context

2026-09-20 15:41 JSTのproduction読み取り専用監視でも`DispatchOrderViolation`の2系統、`2923d81f0c1f3296`と`dde5c066c6730424`がhigh severityで継続した。修正commitは`codex/fix-dispatch-order-monitoring`へpush済みだが、未配備なのでproduction findingの解消確認には至っていない。本recordは、基点recordを追加変更せず、修正のPR、CI、review、配備待ち、配備後監視を追跡する正本とする。

## Goals

- dispatcherが実際に選択可能な未配送taskだけを処理順違反の比較対象にする。
- group上限時にroute順よりeffective priorityを先に採用する。
- PR、CI、reviewを完了し、配備可能な状態にする。
- 将来の許可された配備後に、production read-only監視で2 fingerprintの消失を確認できる証拠を提供する。

## Non-goals

- production deploy、pipeline resume、履歴削除、データ補正。
- 基点change record `20260919_collection-monitor-root-cause-triage`の追加変更。
- dispatch以外のproduction findingの修正。

## Documentation updates

- 本recordのみを追加する。`docs/26-collection-platform-design.md`を確認し、既存のcompatible workとpriority契約を変更せず実装を契約へ一致させるため、正本文書の更新は不要。

## Decisions

- current-generationかつ未配送、due、未予約のoutboxを持つtaskだけをdispatch candidateとし、availabilityまたは直近reservation expiry以後のdispatchだけと比較する。
- envelope admissionはdispatcherと同じaging式で上位を選び、その後にroute順で実行順を決める。
- production findingの消失はdeploy前には主張せず、配備後read-only監視を独立したoperation gateとする。
- 2026-09-20の委譲指示を、既存T2の承認済み範囲でPR、CI、reviewまで進める明示承認として記録する。

## Concern and agreement ledger

| ID | Concern and evidence | Impact | Proposed disposition | AC/task/test | Agent position | User disposition | State |
| --- | --- | --- | --- | --- | --- | --- | --- |
| C1 | 未配備branchのローカル成功だけではproduction finding消失を証明できない。 | 偽の完了報告 | Code/CIとDeployment/operationを分離し、配備後read-only監視をAC3に残す。 | AC2,AC3/T2,T3 | deploy前の解消主張は不可 | Accepted by explicit delegation, 2026-09-20 | Resolved in design |
| C2 | deployはproduction状態を変更する。 | 無許可の外部変更 | PRを配備可能にするところまで進め、deployは明示許可待ちにする。 | AC3/T3 | deployしない | Accepted by explicit delegation, 2026-09-20 | Resolved in design |

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | 既配送Ready task、reservation中のhistorical dispatch、agingされたenvelope代表、group上限超過の反例で偽陽性と高priority除外が再発しない。 | T1 | focused tests、API regression、Release build | Verified |
| AC2 | 修正branchのPRで必須CIが成功し、review findingが解消され、merge可能な状態になる。 | T2 | GitHub PR checks、review、merge state | Not started |
| AC3 | 許可された配備後のproduction read-only監視で`2923d81f0c1f3296`と`dde5c066c6730424`が再観測されない。 | T3 | deployed revision照合、連続read-only monitor | Not started |

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State | Routing | Audit | Result metrics |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | dispatch monitorとadmissionを修正し反例テストを追加する。AC1 | Main | High capability | Approval | monitoring、dispatcher、tests | focused/API/solution gates | dispatch-time candidacyを含むpatchとtests | Verified | Lead — public contract and final acceptance | none | unavailable; retries 0; corrections 1; reviews 2 |
| T2 | PRを作成しCIとreview findingを閉じる。AC2 | Main | High capability | T1 | PR、必要時は同一code/test scope | required checks、review | green mergeable PR | In progress | Lead — integration and final acceptance | none | unavailable; retries 0; corrections 0; reviews 0 |
| T3 | 許可後にdeployしread-only monitorで2 fingerprint消失を確認する。AC3 | Operator | High capability | T2、明示的deploy許可 | production deployment evidence、record | deployed revision、monitor snapshots | 2系統の非再観測 | Externally blocked | Operator — destructive production boundary | none | unavailable; retries unavailable; corrections unavailable; reviews unavailable |

## Review gates

- **Design and task-split review — 2026-09-20, reviewer: Main.** 既存T2の実装契約を変更せず、code、PR/CI、deployment/monitorを分離した。production writeだけをT3へ隔離した。
- **Concern and agreement review — 2026-09-20, reviewer: Main.** 未配備証拠の限界とdeploy権限をmaterial concernとして記録し、解消主張を配備後監視まで保留する。
- **Pre-implementation review — 2026-09-20, reviewer: Main.** T1は既存承認範囲でVerified、T2はRunnableからIn progress、T3は明示許可待ちのExternally blocked。追加codingが必要な場合は既存反例とCIを再実行する。
- **Checkpoint review — 2026-09-20, reviewer: Main.** commit `62eb745`をAC1へ照合。focused 28/28、API 270 passed/1 skipped、Release build 0 warning/0 error、format gate、record validatorsが成功。solution回帰の非関連browser timing failureは単独再実行で成功した。
- **Checkpoint review — 2026-09-20, reviewer: Codex + Main.** PR review P2は、snapshot時点で候補に戻ったtaskが過去のreservation中dispatchまで遡って違反扱いされる反例を指摘した。指摘を採用し、`DispatchCandidateSince`をavailabilityと直近reservation expiryから算出してdispatchごとに比較する反例を追加。focused 29/29、API 271 passed/1 skipped、Release build 0 warning/0 error、format gate成功。基点record差分は専用recordへ移し、PRから除去した。

## Verification record

- 2026-09-20: `codex/fix-dispatch-order-monitoring`をoriginへpush。
- 2026-09-20: 15:41 JSTのproduction read-only監視で2 fingerprint継続を確認。未配備revisionの期待結果であり、解消証拠には使用しない。
- 2026-09-20: PR #64初回CI run `35494971190`成功。Codex review P2 `discussion_r4056338476`を受理し、dispatch時点candidacyの反例を実装、focused 29/29成功。
- 2026-09-20: review修正後、API 271 passed/1 skipped、Release build 0 warning/0 error、`dotnet format ... --verify-no-changes`成功。

## Deviations and follow-up

- production deployと配備後monitorは明示許可待ち。次の安全な操作はPR CIとreviewの完了である。

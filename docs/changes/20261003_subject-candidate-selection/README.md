# 主体同定候補をジョブ詳細から選択して復旧する

- Status: Approved
- Change record schema: 2
- Owner: Collection operations
- Created: 2026-10-03
- Updated: 2026-10-04

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | In progress | T1/T2の候補transport・永続化・apply API/read model、T3のJob Detail候補UI・生成元表示、T4の旧name-only task guardを実装済み。ローカルbrowserの認証後確認が残る。 |
| Verification | In progress | ローカルではAPI 427 passed/1 skipped、Collector関連187 passed、solution build 0 warnings/errors、format verify成功。pushしたSHA `938a3367` のCIはAPI migration test 2件失敗しdeployはskip。stale fixture assertionの修正とCI再実行が進行中。browser desktop/narrow keyboard確認は未実施。 |
| Deployment/operation | Externally blocked | 今回の承認範囲外。実装・テスト後の配備、本番task cleanup、`E56D934CCA551769` の復旧は別途明示依頼後に実施する。 |

## Context

`Horse/JRA/horse-bdd4ff7a-084b-582f-a6c6-79f99778f134` は、プラチナリーフの
プロフィールに表示された父名 `クリソベリル` を、旧実装がURLなしで再帰展開して作成した。
2026-10-02 04:55 JSTに依頼され、2026-10-03 14:41 JSTに実行された時点で、JRA公開検索が
2016年産と1988年産の同名2候補を返し、`SubjectNotIdentified / MultipleCandidates` で停止した。

ジョブ詳細の管理タブには既に「取得先URLを指定して再取得」がある。しかし、この操作は同じ
合成Resource IDへプロフィールを保存する通常の再取得であり、候補選択、canonical Horse IDの作成、
曖昧な元Resourceの終了、選択監査を一つの復旧操作として保証しない。このため同定候補の選択には
専用操作を設ける。

名前だけの血統Horse taskを新しく作るproducerは既に削除済みである。今回の対象は、修正前に作成され、
前回の「失敗済み12件」復旧時には未実行だった残存taskである。

## Goals

- `MultipleCandidates` の候補をジョブ詳細で比較し、JRAページを開いて選択できるようにする。
- 選択した公式URLからcanonical Horse resourceを決定し、そのResourceへ明示URL付きRecovery taskを作る。
- 曖昧な合成Resourceへ公式identityを直接書き込んだり、同名Horseを自動統合したりしない。
- 選択者、選択日時、元task、選択URL、作成されたcanonical resource/taskを監査可能にする。
- 修正前に作成された未実行・失敗済みの名前だけ血統taskを安全に分類・終了できるようにする。
- 対象を生成した元リソースを名前・種類・ID・保存された生成理由とともに表示し、元ジョブ詳細へ移動できるようにする。旧taskに関係の詳細が保存されていない場合、UIは「関連情報の発見」と表示し、血統由来などを推測しない。

## Non-goals

- 複数候補から自動的に先頭を選ぶこと。
- Horseの名前一致だけで既存集約をmergeまたはredirectすること。
- JRA以外の提供元や `NoCandidate` の手動登録フローを同時に追加すること。
- 親プロフィールの血統テキストをHorse ID参照へ変更すること。

## Hypothesis ledger

| ID | Claim | Fact / inference boundary | Falsification and result | Disposition |
| --- | --- | --- | --- | --- |
| H1 | 現行のURL指定再取得だけで安全な候補確定になる。 | UIは任意URLを同じResourceへ再依頼し、workerは取得プロフィールを同じsubject IDへ保存する。candidate selection/別canonical ID/選択監査はない。 | `JobDetail.razor`、task request、profile PUT経路を追跡。仮説は否定された。 | 専用復旧操作を追加する。 |
| H2 | 今回のIDは管理画面URL生成の文字化けである。 | task metadataは名前とHorse発見元だけで、URL・source identity・生年月日がない。依頼時刻は親プロフィール成功直後。 | 親・子resource detailと旧producerのgit履歴を照合。仮説は否定された。 | 合成IDは旧producerの出力として扱う。 |
| H3 | 候補URLは現在も失敗メッセージから安全に抽出できる。 | Scraping例外内では候補が構造化されるが、attempt永続化では文字列に連結されている。 | contracts/entity/APIの候補フィールドを検索し、attempt DTOに存在しないことを確認。 | 文字列解析せず構造化候補を永続化する。 |
| H4 | 元の曖昧なResourceを選択候補へredirectしてよい。 | 元IDは名前だけを表すため、別の同名馬にも一致し得る。 | 2016年産と1988年産の2候補が同じ名前由来IDへ競合する反例を確認。 | redirect/mergeは禁止し、canonical resourceを別作成する。 |

## Experience and interaction design

Page種別は既存のJob Detailを維持する。未解決障害が `SubjectNotIdentified` かつ
`MultipleCandidates` で、構造化候補がある場合だけ、最新障害の直下に候補選択sectionを表示する。

- 候補一覧より先に「この対象を作成したリソース」を表示し、生成元の対象名、種類、ID、
  taskに保存された生成理由、生成元ジョブ詳細へのリンクを示す。生成理由の記録がない旧taskでは「関連情報の発見」とし、対象リソースの本文や変更され得るコードから由来を推測しない。
- 生成元を取得できない旧データでも関係を隠さず、保存済みの種類・provider・IDと
  「生成元の詳細を取得できません」を表示する。
- 各候補に馬名、取得可能な公開根拠、JRA URL、「JRAで確認」リンク、radioを表示する。
- Primary Actionは「選択した候補で復旧」の1つとする。
- 選択前はPrimary Actionを無効化する。
- 実行前に、元の曖昧なIDを統合せず、選択した公式Horseを別対象として再取得することを明示する。
- 二重送信を防止し、通信失敗時は選択を保持する。
- 成功後は作成されたcanonical Horse jobへのリンクを表示する。
- 候補が保存されていない旧attemptではURL手入力へ誘導せず、「候補を再探索」または
  「旧血統参照として終了」の利用可否を表示する。
- 狭幅では候補を一列にし、名前・根拠・リンク・選択操作を省略しない。

画面構造は [ジョブ詳細・候補選択ワイヤーフレーム](mocks/job-candidate-selection.md) を参照する。

## Navigation and relationships

- 開始: `/jobs/{type}/{provider}/{resourceId}/{definition}` の未解決障害。
- 完了: 同じ詳細画面に解決メッセージとcanonical jobリンクを表示する。
- 生成元: task metadataの `discoveredFromType / discoveredFromProvider / discoveredFromId` を正とし、
  対応する収集定義を解決してJob Detailへリンクする。対象名は生成元detailのmetadataから補足するが、
  リンクの同一性には使用しない。
- JRAリンクは新しいタブで開き、`noopener noreferrer` を使用する。
- 元task/failureと新canonical resource/taskの対応を復旧ledgerから双方向に追跡可能にする。

## Documentation updates

- `docs/20-admin-ui-design.md`: Job Detailにおける複数候補の選択・確認・完了導線を追記する。
- `docs/22-collector-design.md`: 構造化候補の永続化と、選択時に曖昧なIDをmergeしない復旧規則、および旧name-only Horse Discovery taskをJRA検索前に終端化する例外とRace-origin除外条件を追記する。
- 本change recordを当該変更の要件・判断・検証の正本とする。

## Technical impact

- Scrapingの `JraSubjectIdentificationCandidate` をCollector completionへ伝播する。
- attempt persistence/contractへ最大5件の構造化候補を追加する。既存attemptは候補なしとして互換読取する。
- 選択preview/apply APIを追加し、apply時にfailureが未解決であること、候補が保存済み候補と一致すること、
  URLがJRA Horse profile identityとして正規化できること、名前が一致することを再検証する。
- Job Detail read modelへ、task metadataから解決した生成元resource summaryを追加する。生成元が削除済み、
  未登録、または対応definitionを一意に決められない場合は保存済みkeyを返し、404や画面全体の失敗にしない。
- canonical IDは選択URLを含む既存のdeterministic Horse ID規則から生成する。共通 `CollectionIdentityResolver` は単一のname-derived HorseをJRA source identityへ採用する経路を持つため、この候補選択では使用せず、選択URLから決まるIDをそのまま用いる。同名のsource-less aggregateが存在する場合も統合・採用・上書きしない。
- 選択applyは元failure notification idをキーとする永続冪等性を持たせる。選択URL・canonical resource/task・selector・選択日時を記録し、同一再送は同じ結果を返し、異なる選択の再送はconflictにする。canonical recovery taskが失敗した場合は元failureを再openする。
- 元の曖昧Resourceはmerge/redirectせず、failureを選択先参照付きで解決する。
- 名前だけ血統taskは、producer停止に加えて、実行前guardと限定的なproduction inventoryで残存を閉じる。

## Decisions

1. 新規ページではなく既存Job Detailへcontextual sectionを追加する。
2. error message文字列から候補URLを解析しない。候補は構造化して最大5件保存する。
3. URL手入力は通常の調査用再取得として残すが、複数候補の解決操作には使用しない。
4. 選択はcanonical resourceの作成・再取得であり、曖昧な元IDのmerge/redirectではない。
5. applyは一回性・再実行安全とし、同じ選択の再送は同じcanonical task/結果へ収束する。
6. 生成元表示は保存済みprovenance keyを正本とし、表示名から生成元を推測しない。

## Concern and agreement ledger

| ID | Concern and evidence | Impact | Proposed disposition | AC/task/test | Agent position | User disposition | State |
| --- | --- | --- | --- | --- | --- | --- | --- |
| C1 | 名前だけの元IDを選択候補へredirectすると、別の同名馬も同じredirectへ吸収され得る。 | silentな別馬統合。 | 元IDはredirect/mergeせず、canonical resourceを別作成する。 | AC2/T2; 同名2頭反例 | 必須 | Accepted: 「元の作業を進めてください」 | Resolved in design |
| C2 | error message解析は文言・URL形式変更に弱い。 | 誤候補、候補欠落、将来互換性低下。 | 最大5件の構造化候補をattemptへ保存する。 | AC1/T1; punctuation反例 | 必須 | Accepted: 「元の作業を進めてください」 | Resolved in design |
| C3 | 運用者が誤った同名馬を選ぶ可能性は残る。 | 正しくないcanonical Horseを収集する。 | JRAリンクと根拠を表示し、未選択送信禁止、選択内容を監査する。自動選択しない。 | AC3/T3; keyboard/誤送信test | 推奨 | Accepted: 「元の作業を進めてください」 | Resolved in design |
| C4 | 修正前の未実行taskは、失敗一覧だけのcleanupでは再び漏れる。 | 配備後も時間差で同じ障害が発生する。 | terminal/nonterminalを含む限定inventoryとworker guardを追加する。 | AC4/T2,T4; queued legacy task | 必須 | Accepted: 「元の作業を進めてください」 | Resolved in design |
| C5 | 候補選択でJRAアクセスと新規taskが増える。 | rate limitと重複実行。 | applyは冪等、1選択1canonical request、既存taskへdeduplicateする。 | AC5/T2,T4 | 必須 | Accepted: 「元の作業を進めてください」 | Resolved in design |
| C6 | 生成元が削除済みまたは旧metadata不足の場合がある。 | 親の取得失敗でJob Detail全体が表示できなくなる。 | 保存済みkeyを必ず表示し、名称取得とリンク解決はbest effortにする。 | AC6/T2,T3; missing-origin counterexample | 必須 | Accepted: 「元の作業を進めてください」 | Resolved in design |
| C7 | 認証後の実ブラウザー確認をスキップすると、実画面の狭幅reflow・focus/keyboard挙動を直接観測できない。 | bUnit/APIでは検出できない表示崩れや操作性問題が残り得る。 | Browser checkをVerifiedの根拠に使わず、API/component/build/formatの既存証拠に限定してpushし、AC3/AC6をConnectedのまま残す。 | AC3/AC6; T3/T5; 手動ログイン後のJob Detail viewport・keyboard確認 | Verifiedまでは実画面確認を推奨 | User explicitly requested skipping browser verification and pushing after disclosure that `main` push triggers production workflow. | Accepted risk |

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | `MultipleCandidates` attemptの最大5候補が名前・URL・根拠を保った構造化データとしてworker HTTP transport・API contract・storage・read-backを通過し、旧attemptも表示できる。 | T1,T2,T7,T8 | candidate worker->API HTTP->persistence->detail round-trip; v22までのmigration fixtureを含むworkflow CI | Connected |
| AC2 | 候補選択で選択URL由来のcanonical Horse resource/taskが作られ、元の曖昧Resourceや既存の名前だけnamesakeはmerge/redirectもプロフィール上書きもされない。 | T2 | 同名2頭・source-less aggregate counterexampleを含むAPI integration testとstore read-back | Verified |
| AC3 | Job Detailで候補を比較・JRA確認・単一選択・復旧でき、未選択、通信失敗、二重送信、狭幅、keyboard操作を安全に扱う。 | T3 | bUnit component testとbrowser verification | Connected |
| AC4 | 修正前に作成された名前だけ血統taskは、状態が待機中でも失敗済みでもJRA検索せず限定的に終了され、公式URL付きRace由来Horseには影響しない。 | T4,T5 | worker counterexampleとlocal inventory/guard test; production dry-run/apply/post-checkは別途許可が必要 | Verified locally; production inventory excluded |
| AC5 | 同じ選択の再送は同じcanonical resource/taskへ収束し、余分なJRA task・failure resolution・監査記録を増やさない。異なる候補での再送はconflictになる。 | T2,T5 | idempotency/concurrency test | Verified |
| AC6 | Job Detailに生成元の対象名・種類・ID・生成理由・詳細リンクが候補より先に表示され、生成元が取得不能でも保存済みkeyと説明を表示してページ全体は利用できる。 | T2,T3 | read-model/API test、bUnit、missing-origin browser state | Connected |

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State | Routing | Audit | Result metrics |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | 構造化候補contractとattempt persistenceを追加する。 | Cheap Executor | `gpt-5.6-luna`, high requested; observed unavailable | Approved | `src/HorseRacingPrediction.Contracts/Collection`; `src/HorseRacingPrediction.CollectionOperations/CollectionPlatform`; `src/HorseRacingPrediction.Collector/CollectionPlatform`; `tests/HorseRacingPrediction.Collector.Tests/CollectionPlatform`; `tests/HorseRacingPrediction.CollectionOperations.Tests` | serialization/store/migration/handler tests | T1-A1; 184 focused tests pass | Verified | Worker — cross-layer DTO/persistence is bounded; selected fallback Luna/high under updated routing policy | T1-A1 | unavailable; retries 0; corrections 0; reviews 1 |
| T2 | 候補apply/API contract mapper、URL由来canonical registration、冪等selection ledger、生成元resource read-modelを実装する。 | Cheap Executor + Lead design | `gpt-5.6-luna`, high requested | T1 | `src/HorseRacingPrediction.Api/CollectionController`; `src/HorseRacingPrediction.Api/Endpoints/Collection`; `src/HorseRacingPrediction.Api/Endpoints/Repairs`; `src/HorseRacingPrediction.Api/Endpoints/Horses`; `src/HorseRacingPrediction.Contracts/Repairs`; `src/HorseRacingPrediction.Contracts/Collection`; `src/HorseRacingPrediction.Contracts/Horses`; `src/HorseRacingPrediction.CollectionOperations/CollectionPlatform`; `tests/HorseRacingPrediction.Api.Tests`; `tests/HorseRacingPrediction.Collector.Tests/CollectionPlatform/CollectionPlatformStoreTests.cs` | API HTTP integration, migration, idempotency/concurrency and identity counterexample tests; notification recovery ordering regression | AC1/AC2/AC5/AC6 evidence | Verified | Worker — API route and store ledger are one consistency boundary; Lead freezes source-derived identity and fail-closed behavior | T2-A1, T2-A2, T2-A3 | unavailable; retries 1; corrections 1; reviews 3 |
| T3 | Job Detailの生成元・候補選択sectionを実装する。 | Lead (bounded executor) | `gpt-5.6-luna`, high requested; runtime route unavailable here | T1,T2 | `src/HorseRacingPrediction.Api/Web/Components/Pages/JobDetail.razor`; `src/HorseRacingPrediction.Api/Web/Components/Pages/JobDetail.razor.css`; `src/HorseRacingPrediction.Api/Web/ApiBrowsing/AdminApiClient.SubjectIdentificationRepair.cs`; `src/HorseRacingPrediction.CollectionOperations/CollectionPlatform/CollectionModels.cs`; `tests/HorseRacingPrediction.Api.Tests/CollectionAdministrationComponentTests.cs`; `docs/20-admin-ui-design.md` | bUnit success/error/legacy-origin/selection and disabled-submit tests; local browser desktop/narrow keyboard verification | AC3/AC6 evidence | In progress | Implementation and bUnit passed; browser desktop/narrow keyboard check awaits manual local login | Lead-only | unavailable; retries 0; corrections 0; reviews 1 |
| T4 | Worker入口でlegacy name-only horse-reference taskをJRA検索前に安全終了する。 | Lead (bounded executor) | `gpt-5.6-luna`, high requested; runtime route unavailable here | T1 | `src/HorseRacingPrediction.Collector/CollectionPlatform/JraSubjectCollectionHandlers.cs`; `tests/HorseRacingPrediction.Collector.Tests/CollectionPlatform/JraSubjectCollectionHandlerTests.cs`; `tests/HorseRacingPrediction.Collector.Tests/CollectionPlatform/CollectionPlatformStoreTests.cs`; `docs/22-collector-design.md` | legacy handler negative test, worker-to-store terminal state/no actionable failure integration test, valid race-source counterexample | AC4 guard evidence | Verified | Lead executes as bounded local task; guard excludes official Race source identity | Lead-only | unavailable; retries 0; corrections 0; reviews 1 |
| T5 | 統合検証とローカル受け入れを行う。 | Lead / verifier | Luna/high | T1-T4 | verification、change record | relevant builds/tests, browser/component checks, git diff/status | AC1-AC6 local evidence | In progress | API/Collector tests, formatter, solution build, CodeGraph sync and diff checks done; browser confirmation awaits manual local login | none | unavailable; retries 0; corrections 0; reviews 1 |
| T6 | Deploy、production cleanup、本番taskの復旧を行う。 | Operations follow-up | Luna/high | T1-T5 | production environment only | authorized deployment run and exact-membership dry-run/apply/post-check | deployed candidate selection and resolved incident | Externally blocked | Excluded follow-up — requires explicit production/deployment instruction; does not block local AC implementation | none | unavailable; retries 0; corrections 0; reviews 0 |
| T7 | push後のCI失敗を閉じる。v22 migration fixtureの期待値を直し、workflow相当のRelease testを再実行する。 | Cheap Executor + Lead acceptance | `gpt-5.6-luna`, high requested | T2 | `tests/HorseRacingPrediction.Api.Tests/CollectionDispatchStarvationReproductionTests.cs` | focused API migration tests then full API test project; Lead additionally runs workflow-equivalent Release solution build/test | local migration tests and exact Release workflow test command pass; remote Actions after push remains required to close original failure | In progress | Worker owns one file; Lead owns integration and final acceptance | T7-A1 | unavailable; retries 0; corrections 0; reviews 1 |
| T8 | 部分ステージ時の検証対象とコミット内容の不一致を防ぐCI closure ruleを追加する。 | Lead | `gpt-6-luna`, high requested; no model-selection UI exposed in this task | T7 | `.codex/skills/learn-from-implementation-failures/SKILL.md` | skill-creator `quick_validate.py`; manually test the rule against an acceptance-critical unstaged migration test and a genuinely unrelated unstaged file | rule requires explicit reconciliation and does not sweep unrelated changes into a commit | Verified | Lead — process contract correction following a confirmed CI delivery failure; accountable retrospective ownership and direct validation keep this small closure coherent | none | unavailable; retries 0; corrections 0; reviews 1 |

作業は共有contract → API → UI → 統合の順に直列化する。単一の共有状態を扱うため並列編集しない。
Pre-implementation readiness note (2026-10-04, T7): this skill was loaded. Purpose: close the observed `app-deploy` verification failure without changing production behavior and establish why required test edits were omitted from the prior commit. Investigation: GitHub Actions run `37152236724` on Ubuntu 24.04 failed two API tests in `CollectionDispatchStarvationReproductionTests`; schema migrator is v22 while the worktree-only fixture edits already expect v22 and preserve schema 19 before the schema-20 recovery case. The local solution build/API test ran with those unstaged edits, while commit `938a3367` omitted that test file; thus the local green evidence did not describe the pushed tree. Scope: only this test file, this record, and the narrow CI staging-scope rule in `learn-from-implementation-failures`. Steps: (1) retain the two focused fixture corrections; (2) validate the skill rule with `quick_validate.py`; (3) reproduce the workflow's Release build and exact filtered solution test command on the corrected tree; (4) stage and inspect every candidate-path diff before a separate checkpoint commit; (5) do not push absent an explicit new push request. Unresolved specification questions: none. Requested ordinary route: `gpt-5.6-luna/high` per repository routing policy; use one bounded executor for the sole test file, Lead retains integration and acceptance. No model telemetry is available in this turn, so observed model/usage remain unverified. Delegation is limited to a single disjoint file; no parallel tasks. Canonical pre-write audit passed: `python scripts/audit_agent_execution.py docs/changes/20261003_subject-candidate-selection` (`valid`).
Pre-implementation plan (2026-10-04): T1 implements the existing structured `JraSubjectIdentificationCandidate` from Collector completion through Contracts, attempt persistence/schema, read-back mapper, and Collector worker transport. Repository inventory: the candidate type already exists in Scraping; completion/summary records are in `CollectionModels.cs`; attempt entity/store persistence is in CollectionOperations; public completion/summary DTOs and `CollectionContractMapper` carry the API shape; `JraSubjectCollectionHandlers.cs` catches identification exceptions; focused tests exist in Collector handler and CollectionPlatformStore suites. Write scope: those production layers and their focused tests only. Ordered steps: (1) trace all completion transports and schema migration pattern; (2) add backward-compatible optional candidate list capped/validated at producer boundary; (3) persist and read it; (4) map both completion and summary contracts end-to-end; (5) test production-shaped multi-candidate payload and old-row compatibility. No unresolved spec question; decisions stay frozen (no message parsing, max five candidates, null for legacy). Verify targeted dotnet tests for Collector and CollectionOperations/API contract tests, then relevant build. Requested route `gpt-5.6-luna/high` selected as eligible Luna fallback; observed model/telemetry recorded separately after dispatch. Audit validator passed immediately before implementation: `python scripts/audit_agent_execution.py docs/changes/20261003_subject-candidate-selection`. T1 is Runnable; T2-T4 remain Dependent.

Pre-implementation plan (2026-10-04, T2): purpose is to expose structured candidates through the API, safely apply one verified candidate to its URL-derived canonical Horse, preserve selection idempotency/audit, and resolve creator-resource context without making job detail depend on origin availability. Repository findings: `CollectionContractMapper` is the public completion/read-back boundary; the existing `CollectionIdentityResolver.HorseAsync` may adopt a source-less same-name Horse and is prohibited in this flow; existing collection batch id/fingerprint deduplication is available but does not itself bind a failure notification to a chosen URL; failure notifications already persist resolution state/recovery task linkage; `RegisterHorseEndpoint` and the deterministic Horse ID generator are established registration patterns; API HTTP transport tests exist in `CollectionCompletionTransportTests` and `CollectionPlatformDiscoveryEndToEndTests`. Write scope is the exact T2 paths in the task table. Ordered steps: (1) inspect endpoint registration, schema migration, failure recovery, and job-detail read model patterns; (2) add structured candidate mapping and compatibility tests; (3) implement atomic/serialized apply keyed by failure notification with stored URL/canonical task/actor/time and reject a different replay; (4) register only the exact URL-derived canonical ID, checking identity on existing exact ID and leaving any source-less same-name ID untouched; (5) resolve origin summary best-effort from stored provenance and add missing-origin tests; (6) run targeted API/store tests and build. No unresolved specification question. Frozen decisions: never parse error text; never call the namesake-adopting resolver; never merge/redirect the ambiguous source; revalidate the submitted candidate against stored candidates; do not add production access/cleanup. Requested `gpt-5.6-luna/high` is available in the agent routing interface as the alternate Luna route. Runtime observed model and token telemetry are not exposed and will remain unknown; this is not an availability blocker. T2 is In progress; T3/T4 remain dependent. Pre-write audit is run after the active attempt record is added.

Pre-implementation readiness note (2026-10-04, T2-A3): the orchestration and DDD skills were loaded before this slice. Investigation confirmed that the existing subject-selection row has a notification-keyed primary key and resumable `CanonicalTaskId = Guid.Empty`, but `ReserveSubjectIdentificationCandidateAsync` still uses the process-local `Gates` semaphore and only treats a uniqueness error as the cross-process race. T2-A3 will make the reservation claim depend on the CollectionPlatform SQLite transaction/unique key rather than that endpoint-local gate, handle SQLite writer/constraint races with bounded re-read/retry, and add a two-independent-store regression. Files are limited to `CollectionPlatformStore.SubjectIdentification.cs` and `tests/HorseRacingPrediction.Api.Tests/SubjectIdentificationCandidateEndpointTests.cs` within the approved T2 scope; no schema change is expected. Ordered steps: (1) remove the process-local gate from the reservation claim only; (2) preserve first-writer selection and timestamp while treating a committed row as authoritative; (3) re-read the durable winner after transient/unique contention and keep a different choice conflicting; (4) test concurrent stores before any registration plus same-choice resume, then retain the HTTP side-effect assertion. Unresolved specification questions: none. Verification commands: focused `SubjectIdentificationCandidateEndpointTests`, focused API candidate/transport tests, full API test project, `dotnet build HorseRacingPrediction.sln --no-restore`, `dotnet format HorseRacingPrediction.sln --no-restore --verify-no-changes`, `git diff --check`, and `git status`; expected results are pass/clean. Requested route is `gpt-5.6-luna/high`; observed model and usage telemetry remain unavailable and are not inferred. Task state: T2-A3 `In progress`; lead review will independently verify the race and side-effect assertions. Canonical audit command passed before this implementation slice: `python scripts/audit_agent_execution.py docs/changes/20261003_subject-candidate-selection`.

## Review gates

- **Design and task-split review**: 2026-10-03 Lead。現行UI/API/worker/persistenceと本番taskを入力に、候補選択を通常URL再取得から分離し、identity判断と生成元read modelをT2へ集約した。ACとtaskは全て対応し、直列依存にした。
- **Concern and agreement review**: C1-C6を確認。redirect禁止、構造化候補、明示確認、nonterminal cleanup、冪等性、生成元取得のgraceful degradationで設計上解決した。承認時に一括受諾された。
- **Pre-implementation review**: userの「元の作業を進めてください」を候補選択/生成元表示の設計承認として記録。ユーザーが選択したのはローカル実装と検証の継続であり、push/deploy/本番task変更の明示許可ではないためT5はExcluded follow-up。後続のrouting-policy修正により、`gpt-6-luna/high` が選択不能ならhost catalogで利用可能な別Luna/highへ切替可能。モデルtelemetry不在だけではblockerにしない。実際に利用可能なLuna経路がない場合だけ外部blockerとする。
- **Checkpoint review**: T1、T2、T3、T4の各検証後にAC group単位で実施する。
- **Final review (2026-10-04)**: AC1/2/4/5 are Verified from API/worker/store counterexamples; AC3/6 are Connected by passing component/API tests but the browser viewport/keyboard check remains open pending manual local login. T1/T2/T4 are Verified; T3/T5 remain In progress for that browser evidence. The full production post-check is explicitly excluded (T6); no deployment or production cleanup was attempted.

## Verification record

- CodeGraphとsource inspectionで、現行Job DetailのURL指定が同じResourceへの通常再依頼であることを確認した。
- 本番read-only診断で、元taskにURL/source identity/birth dateがなく、親Horseから名前だけで生成されたことを確認した。
- Scraping例外は候補を構造化しているが、attempt contract/persistenceはmessage文字列しか保持しないことを確認した。
- 正規audit gate: `python scripts/audit_agent_execution.py docs/changes/20261003_subject-candidate-selection` passed before worker dispatch.
- T1 checkpoint: `dotnet test tests/HorseRacingPrediction.Collector.Tests/HorseRacingPrediction.Collector.Tests.csproj --filter "FullyQualifiedName~JraSubjectCollectionHandlerTests|FullyQualifiedName~CollectionPlatformWorkerClientTests|FullyQualifiedName~CollectionPlatformStoreTests" --no-restore` passed, 184/184. The real HTTP API mapper round-trip remains in T2 before AC1 can be marked Verified.
- Lead checkpoint review of T1: verified the change is scoped to structured failure candidates, nullable legacy-compatible storage, and worker transport; no error-string parsing or identity change. T1-A1 output and attributable diff reviewed; T2 owns the remaining API mapper/read-back evidence.
- T2 frozen identity invariant: candidate apply must use `DeterministicIdGenerator.BuildHorseId(selectedName, normalizedJraHorseIdentity)` directly; it must not call the general namesake-adopting `CollectionIdentityResolver.HorseAsync`. If that exact canonical ID already exists, verify same normalized name/source; if another same-name ID exists, leave it untouched and keep separate. Required regression includes an existing source-less name-derived Horse with the same name as selected candidate.
- T2-A1 checkpoint (2026-10-04): Lead independently ran `dotnet test tests/HorseRacingPrediction.Api.Tests/HorseRacingPrediction.Api.Tests.csproj --filter "FullyQualifiedName~SubjectIdentificationCandidateEndpointTests|FullyQualifiedName~CollectionCompletionTransportTests" --no-restore` (6 passed). AC-group review found a cross-process race not covered by the endpoint-local gate: a different candidate may register its canonical Horse before the durable selection ledger rejects it. T2-A1 therefore needs a durable notification-keyed selection reservation before Horse registration and a competing-process counterexample; no acceptance credit for that race is claimed yet.
- T2-A2 routing result (2026-10-04): the requested Luna worker produced no implementation patch or progress response after two status requests; Lead interrupted and re-routed with a concrete bounded reservation design. No files were attributed to A2.
- T2-A3 checkpoint (2026-10-04): `ReserveSubjectIdentificationCandidateAsync` no longer relies on the process-local `Gates` semaphore for its claim. The notification primary key and SQLite transaction now decide the first writer; bounded handling re-reads a committed winner after constraint/writer contention and retries only when no winner is visible. The new independent-store regression ran against two `CollectionPlatformStore` instances sharing one database and proved one candidate wins, the other conflicts before registration, and the winner remains resumable with `CanonicalTaskId = Guid.Empty`. The HTTP race regression still proved exactly one canonical Horse is registered. Evidence: focused candidate/transport run passed 8/8; full API suite passed 421/421 with 1 skip; solution build passed with 0 warnings/errors; CodeGraph `sync .` passed; `git diff --check` passed. T2-A3 implementation is ready for Lead's independent review; observed model/usage telemetry remains unavailable and is not inferred.
- T2 Lead closure (2026-10-04): API suite passed 421, skipped 1. Re-running the related Collector regression set exposed two assertions still pinned to schema version 21 after migration v22, plus a real recovery-ordering regression: reopening the prior failure after inserting the replacement allowed EF's tracked entity state to change the superseded row back to `Open`. Reordered the same transaction to reopen the recovery source before queuing/superseding with the new failure. The three focused counterexamples passed, then Collector related suite passed 184/184. This was a local closure fix within T2 acceptance, not a design change.
- T3 pre-implementation review (2026-10-04): keep the existing Job Detail route and Fluent detail/tabs. Show the origin section ahead of candidate selection from stored provenance only; show the saved origin key and unavailable note when its read model is gone, and make no origin claim when legacy metadata has no source. Show structured candidates only for an actionable `SubjectNotIdentified`/`MultipleCandidates` Horse failure; use a labeled Fluent radio group, external JRA links with `noopener noreferrer`, one disabled-until-selected action, busy protection, preserved selection after transport failure, and canonical job link after success. Extend the API client only to call the already implemented apply endpoint. Update `CollectionAdministrationComponentTests.cs` for no candidate/legacy, origin, success and failure paths; run component tests plus browser desktop/narrow keyboard verification. No manual URL entry or automatic selection. This turn has no Luna executor dispatch interface; Lead will execute as a bounded local task and will not claim Luna runtime usage. T3 is In progress, T4 remains Dependent.
- T4 pre-implementation review (2026-10-04): the safe-stop predicate is deliberately narrow: Horse `horse-profile` Discovery task whose stored origin type is Horse and whose identity evidence, birth date, and valid JRA Horse location are all absent. It returns terminal `NotApplicable` before constructing a JRA session, so it does not create a new actionable failure or write profile data. Any task with Race-source identity/URL, an existing usable profile URL, another reason/definition/resource type, or birth date continues through the regular path. Tests cover zero JRA navigator starts for a legacy Horse→Horse name-only task and a Race-origin counterexample; the existing race-reference identity regression remains in the related Collector suite. Update `docs/22-collector-design.md` with this boundary. No unresolved specification choice. T4 is In progress.
- T3/T4/T5 closure evidence (2026-10-04): component tests for candidate success, preserved selection on conflict, and legacy missing-origin/no-candidate passed 3/3. Full API test project passed 424, skipped 1; Collector handler/store filter passed 174/174 including legacy terminal/no-actionable-failure worker-to-store integration. `dotnet build HorseRacingPrediction.sln --no-restore` passed with 0 warnings/errors. `dotnet format HorseRacingPrediction.sln --no-restore --verify-no-changes` passed. `codegraph sync .` passed (already up to date); `git diff --check` and the canonical audit validator passed. Browser verification remains unfinished because the local app redirects to login and the computer-use policy requires a human to complete authentication; no credential was entered or surfaced. Next action: user manually signs in to the already-open local browser tab, then Lead performs desktop/narrow keyboard checks and updates AC3/AC6, T3/T5, and the Status. No push, deploy, or production mutation was performed.
- Observed verification failure (2026-10-04): pushed commit `938a3367ad4f72e0b8b996eecdb2b00bda226cfd`; GitHub Actions `app-deploy` run `37152236724`, Ubuntu 24.04, workflow command `dotnet test HorseRacingPrediction.sln --no-build --configuration Release --filter "TestCategory!=External"` failed `Schema19_AddsWakeIdentityWithoutChangingExistingReservationRows` (expected schema 20, actual 22) and `Schema20_ReleasesStrandedLegacyReadyEnvelope` (no schema-20 row after migration now advances through v22). Classification: confirmed staged-scope omission, not a production runtime failure; `build` and earlier workflow steps passed, deploy jobs were skipped. The same worktree already had the intended fixture fix and local API suite passed 427 with one skip because the prior Release/Debug build consumed that unstaged file. Disposition is open until the corrected file is included in a checkpoint and the original workflow test command succeeds remotely.
- T7 cause analysis (2026-10-04): immediately, the explicit `git add` allowlist for the candidate feature omitted `CollectionDispatchStarvationReproductionTests.cs`; at review time it was misclassified as unrelated dispatcher work, despite its schema-v18/v20 fixtures being exercised by the feature's v22 migration and being required for current-schema test correctness. Systemically, the local green result was accepted without reconciling the files consumed by verification against the staged commit manifest. The separate paused-deployment test and its workflow/docs changes are genuinely unrelated and remain uncommitted. The stash was created to preserve all dirty/untracked changes before rebase; `stash pop` restored them except a conflict in the dispatcher incident record, where the newer remote record was retained. The stash remains as a recovery snapshot.
- T7 local CI-parity verification (2026-10-04): after applying only the migration fixture correction to a clean detached worktree at `938a3367`, `dotnet restore HorseRacingPrediction.sln`, workflow Release build, and the exact workflow test command passed: API 427 passed/1 skipped, Collector 420 passed, remaining suites passed (full command exit 0). The shared dirty worktree's same full test command separately exposed an unrelated failure `DeployWorkflow_RestoresPipelineAfterHealthCheckWithoutLegacyMigration`: local uncommitted `.github/workflows/app-deploy.yml` now emits a new notice while the existing Collector contract test still expects the previous message. This belongs to the distinct approved change `20261003_deploy-with-paused-collection` (T1), not this candidate correction; it is preserved and not mixed into T7. The remote run at `938a3367` had the prior workflow version and its Collector suite passed, so this did not cause run `37152236724`.
- T7 remote verification remains open: the corrected migration fixture is locally verified in the exact Release configuration and included in the next local commit, but no subsequent push or remote Actions result is authorized/available in this request. Deploy did not run on `37152236724`; no new version was rolled out by that run.
- T8 process-rule forward check (2026-10-04): an acceptance-critical migration test left unstaged must either be brought into the staged diff or excluded as evidence; genuinely unrelated workflow modifications remain outside the candidate commit, with their changed worktree behavior tested separately in a clean checkout. `quick_validate.py` passed. The rule catches the exact omission without instructing broad staging.
- Local browser acceptance setup (2026-10-04): started `HorseRacingPrediction.Api` at `http://localhost:5197` with a unique temp directory (`%TEMP%\horse-racing-ui-check-1b90931c743a4a55ae98be665af9e75f`) for the event DB, collection platform DB/state, and data-protection keys; queue, background schedulers, and job-failure notifications were disabled. No repository-default or production database was used. Seeded one fictional origin horse and one `SubjectNotIdentified / MultipleCandidates` Horse task with two fictional candidate names/evidence via the local admin/internal APIs. API read-back confirmed 2 candidates, origin name `画面確認用の起点馬`, and reason `血統プロフィールの親対象として発見`. Browser navigation to the corresponding Job Detail redirected to the local login page. Authentication has not been automated. On 2026-10-04 the user explicitly requested skipping browser verification and pushing; therefore AC3/AC6 remain Connected, T3/T5 remain In progress, and no implementation-complete claim is made by this checkpoint. Do not record credentials.
- 2026-10-03時点ではCheap executorの利用可否を実際のmodel selection/dispatch経路で確認した証拠がなく、telemetry不在を利用不可と誤判定していた。後続のworkflow change `20261003_luna-routing-fallback` でこのruleを修正した。routing policy修正後、T1はRunnable、T2-T4は依存順でDependent。配備・本番操作T5はExcluded follow-upのまま。

## Deviations and follow-up

- 以前のpre-write blocker判定は誤りとして superseded: runtime model/usage telemetryの欠如は、実際のmodel-selection/dispatchからの利用不可証拠ではなかった。アプリコードは引き続き未変更。次の実装は更新後のLuna fallback policyとpassing audit gateで再開する。

# モデル階層に応じたエージェント分業と change record レビューを標準化する

- Status: Implemented
- Owner: HorseRacingPrediction maintainers
- Created: 2026-09-14
- Updated: 2026-09-14

## Context

現行の開発手順は change record、受け入れ基準、検証、チェックポイントを詳細に定めているが、通常の開発でエージェントへ何を委譲するか、どのモデル階層を選ぶか、誰が成果を統合・レビューするかを標準化していない。

`docs/changes` の44件を監査した結果、タスク単位の owner と model はほぼ記録されず、`Depends on` と `Write scope` を持つ標準的な実行計画は一部の大規模変更に限られていた。明示的な実装前レビューは4件、最終レビューは4件、Acceptance-criterion matrix は5件だった。このため、change record が存在してもタスク分割の品質、共有 worktree の競合、委譲結果の採否、完了証拠を一貫してレビューできない。

本変更では、設計・分割・統合・最終判定を高能力モデルの主担当に残し、境界が明確で独立検証できる調査・コード探索・定型実装・テスト作成をコスト効率の高いモデルへ委譲する。モデル名を固定せず、利用可能なモデルの能力、コスト、タスクの不確実性、失敗時の手戻りで選ぶ。

## Evidence and failure analysis

### 確認できた事実

- `document-driven-development` は受け入れ基準、Delivery plan、チェックポイントレビューを要求するが、通常開発の委譲計画とモデル選択を定義していない。
- `learn-from-implementation-failures` だけが、認可済みで書込範囲が非重複のタスクを並列委譲し、主担当が統合・スキル解釈・最終検証を保持するよう定めている。この規定は失敗分析時にしか適用されない。
- UI系スキルは設計レビューと検証を部分的に定めるが、change record のタスク計画やレビュー証拠への接続がない。
- `blazor-fluent-ui-design` の「小さな変更では文書化不要」は Implementation Note の省略を意図しているが、AGENTS.md の UI/UX 変更に対する change record 要件まで省略できるようにも読める。
- 既存の良い例では、`20260911_unified-collection-platform/execution-plan.md` が依存・書込範囲・検証・状態を管理し、`20260907_playwright-page-snapshot` と `20260908_jra-semantic-snapshot-cutover` が実装前後のレビューを記録している。ただしモデル階層とタスク別 owner はない。

### 原因

即時の原因は、change record の Delivery plan が成果の順序を示すだけで、委譲可能性、担当、モデル階層、競合境界、採用条件まで表現していないことにある。ワークフロー上の原因は、委譲ルールが失敗分析スキルに局所化され、通常の設計・実装フローから参照されないことである。

不足しているゲートは、承認前のタスク分割レビュー、委譲成果を主担当が独立に確認する統合レビュー、全受け入れ基準とタスクの未完了ゼロを照合する最終レビューである。

### 外部根拠

- OpenAI の現行 Model guidance は、難しいエンドツーエンド作業に高能力モデルを使い、subagent の委譲量を明示的な指示で調整できると説明している。また、スキルや `AGENTS.md` の曖昧な指示を監査することを推奨している。https://developers.openai.com/api/docs/guides/latest-model
- OpenAI のモデル一覧は、Astra を複雑な推論・コーディング、Terra を能力とコストの均衡、Luna をコスト重視・大量処理向けとしている。https://developers.openai.com/api/docs/models
- Anthropic の公式解説は、簡単で頻出の処理を小型モデル、難しい処理を高能力モデルへ送る routing、独立サブタスクの parallelization、中央モデルが分割・統合する orchestrator-workers、別評価者が基準に照らして改善する evaluator-optimizer を整理している。https://www.anthropic.com/engineering/building-effective-agents
- OpenAI の agent 構築ガイドは、すべての処理へ最上位モデルを使わず、まず品質基準を満たすモデルを確立してから小型モデルへの置換でコストとレイテンシを最適化する考え方を示している。https://cdn.openai.com/business-guides-and-resources/a-practical-guide-to-building-agents.pdf
- ソフトウェア工学向けマルチエージェントのレビュー論文は、Orchestrator、Programmer、Reviewer、Tester、Information Retriever の役割分担とクロス検証の有用性を整理する一方、通信と協調設計を課題としている。https://doi.org/10.1145/3712003

これらから、モデルの単価だけでなく、タスクの曖昧さ、独立性、検証可能性、再試行と統合の費用を含めて割り当てる必要があると判断する。

## Goals

- 通常のコーディング・調査で、独立かつ明確なサブタスクをコスト効率の高いモデルへ委譲する。
- 要件解釈、アーキテクチャ、タスク分割、境界判断、統合、レビュー、完了判定を高能力モデルの主担当が保持する。
- change record 内で各タスクの owner、model tier、依存、書込範囲、検証、完了証拠、状態をレビューできるようにする。
- 共有 worktree の競合と、安価なモデルの失敗による手戻りを早期に検出し、必要なら主担当または上位モデルへ昇格する。
- スキル間の重複と曖昧さを減らし、設計判断と検証証拠を change record へ一貫して接続する。

## Non-goals

- すべてのタスクで必ず複数エージェントを起動すること。
- 特定のモデル名や価格を恒久的な規則として固定すること。
- 安価なモデルの成果を主担当のレビューなしで採用すること。
- 同じファイルや共有状態を複数エージェントへ無条件に同時編集させること。
- 外部プロジェクト管理サービスを導入しなければ運用できない仕組みにすること。

## Decisions

### 1. 主担当は高能力モデルを使う

主担当は要件と change record の解釈、設計、依存グラフ、タスク分割、委譲指示、成果統合、設計との差分判定、最終レビューを担当する。モデル選択肢がある場合は、その時点で利用可能な最上位または複雑な長期作業に適したモデルを選ぶ。

### 2. 委譲はタスク特性で判断する

コスト効率の高いモデルへ優先的に委譲する対象は、探索範囲と出力形式が明確な調査、コード検索、独立した小規模実装、既存パターンに沿う機械的変更、テスト候補作成、ログ整理、文書の棚卸しである。

次のいずれかに当たる場合は、主担当が実行するか上位モデルへ昇格する。

- 要件が曖昧で、判断により外部仕様または受け入れ基準が変わる。
- 複数プロジェクトや永続化境界をまたぐ設計、移行、セキュリティ、データ損失リスクを含む。
- 共有ファイルの同時編集や複雑な統合が必要になる。
- 初回成果が受け入れ基準を満たさない、根拠が不足する、または同種の失敗を繰り返す。
- 独立した検証コマンドまたは観測可能な完了条件を定義できない。

### 3. 並列化は独立性を先に証明する

並列委譲前に、依存関係、入力、成果物、書込範囲、検証方法を分離する。同じファイルまたは共有状態を扱う場合は owner を一つにする。契約型を先に追加する等の順序が必要な場合は依存タスクとして直列化し、共有 build を壊した状態で待機しない。

### 4. 主担当レビューを採用条件にする

委譲成果は、主担当が diff または一次資料を読み、受け入れ基準への対応、目的外変更、証拠、共有 worktree との整合を確認してから採用する。実装者の自己申告やテスト成功だけで完了にしない。高リスクまたは広範囲の変更では、実装コンテキストを与えすぎない独立レビューを追加する。

### 5. 成功結果あたりの総費用で見直す

安価なモデルで再試行、修正、レビューが増える場合は、同種タスクを上位モデルへ昇格する。モデル単価だけを成功指標にせず、初回受入率、再試行回数、レビュー指摘数、修正時間、検証失敗を change record の Verification record に必要な範囲で記録する。

## Proposed task-plan format

大規模または複数エージェントを使う変更では、change record 本文または `execution-plan.md` に次の表を必須とする。単一の短いタスクでは同じ情報を箇条書きに圧縮してよい。

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | 要件・設計・受け入れ基準を確定する | Main | High capability | - | change record | ユーザーレビュー | 明示承認 | Proposed |
| T2 | 独立した探索・実装候補を調査する | Worker | Cost efficient | T1 | Read-only または限定パス | 指定クエリ・テスト | 出典またはdiff | Dependent |
| T3 | 成果を統合し設計適合をレビューする | Main | High capability | T2 | 統合対象 | 全差分・関連テスト | 指摘ゼロまたは修正証拠 | Dependent |

State は `Proposed`、`Runnable`、`In progress`、`Dependent`、`Externally blocked`、`Rejected with reason`、`Verified` を使う。

## Review gates

### Design and task-split review

承認依頼前に主担当が、各受け入れ基準が少なくとも1タスクと検証へ対応すること、タスク境界が独立していること、委譲先モデルの能力で完了条件を満たせること、統合責任が主担当に残ることを確認する。設計上の未決事項を実装ワーカーへ委ねない。

### Pre-implementation review

Approved へ移行した直後、コード変更前に実行計画を再確認し、各行を `Runnable`、`Dependent`、`Externally blocked`、`Rejected with reason` のいずれかにする。並列 frontier の write scope が重ならないことを確認してからエージェントを起動する。

### Checkpoint review

各チェックポイントで主担当が、委譲成果、全 diff、受け入れ基準マトリクス、テスト結果、未完了タスクを照合する。失敗または広がったスコープは、再分割、直列化、モデル昇格のいずれかを記録する。コミットは停止条件にしない。

### Final review

すべてのタスクと受け入れ基準について、完了証拠または真正な外部 blocker があることを確認する。`Runnable`、`In progress`、ローカル依存の `Dependent` が残る場合は Implemented にしない。主担当が `git diff --check`、`git status`、関連テスト、CodeGraph の必要な再同期を確認する。

## Skill changes

### 新規 `agent-task-orchestration`

通常の開発・調査で再利用できる独立スキルとして作成する。内容はモデル階層の選択、委譲可能性、並列化条件、worker prompt の必須入力、共有 worktree の所有権、昇格条件、主担当の統合・レビュー責任、コスト評価とする。

### `document-driven-development`

- Workflow に Design and task-split review、Pre-implementation review、Checkpoint review、Final review を追加する。
- 複数タスクまたは複数エージェントの場合に Proposed task-plan format を要求する。
- Acceptance-criterion matrix と task plan の双方向対応、全未完了ゼロの完了ゲートを追加する。
- `agent-task-orchestration` を使う条件を明記する。

### `learn-from-implementation-failures`

- 通常の委譲規則を新スキルへ移し、このスキルには失敗時の再分割・モデル昇格・再発防止分析だけを残す。
- 委譲失敗時は、初回受入率、再試行、レビュー負荷を根拠に routing rule を狭く更新する。

### Blazor UI skills

- `blazor-fluent-ui-design` の「小さな変更では文書化不要」を「Implementation Note は不要だが、AGENTS.md と DDD が要求する change record は省略しない」へ明確化する。
- `blazor-fluent-ui-design` は設計判断、`blazor-ui-testing` は検証証拠を担当すると明記する。
- `blazor-data-grid`、`blazor-form`、`blazor-page-patterns` は、適用中の change record がある場合、関連する受け入れ基準と検証記録へ判断を接続する短い共通規定を追加する。個別スキルへ委譲規則を重複記載しない。

### `AGENTS.md`

- 通常開発で `agent-task-orchestration` を適用する条件と、主担当が設計・分割・統合・最終レビューを保持する原則を追加する。
- change record 不要の例外を、行動を変えない誤字・コメントのみの訂正に限定して DDD と同期する。

## Documentation updates

- `AGENTS.md`: 通常開発におけるモデル階層と委譲、主担当責任、change record 例外を追加する。リポジトリ全体のエージェント運用の正本とする。
- `.codex/skills/agent-task-orchestration/SKILL.md`: タスク分割、モデル routing、並列化、worker 成果の採用・昇格条件を定義する。
- `.codex/skills/document-driven-development/SKILL.md`: change record 内のタスク計画と4段階レビューゲートを定義する。
- `.codex/skills/document-driven-development/references/change-record-format.md`: Task plan、Design review、Pre-implementation review、Final review、Completion evidence の標準欄を追加する。
- `.codex/skills/learn-from-implementation-failures/SKILL.md`: 通常委譲との責務境界と routing 失敗時の観測可能な改善を追加する。
- `.codex/skills/blazor-fluent-ui-design/SKILL.md`: 文書化例外の曖昧さと設計責務を修正する。
- `.codex/skills/blazor-ui-testing/SKILL.md`: change record へ検証証拠を記録する責務を明記する。
- `.codex/skills/blazor-data-grid/SKILL.md`、`.codex/skills/blazor-form/SKILL.md`、`.codex/skills/blazor-page-patterns/SKILL.md`: 適用中の受け入れ基準への接続を追加する。

## Tool and plugin assessment

- Collaboration tools: 本変更の中核。主担当がタスク境界とモデルを指定して worker を起動し、完了通知を待ち、成果をレビューする。
- CodeGraph: 既に利用可能であり、コード探索と影響範囲確認を安価な worker へ委譲しやすい。新規導入は不要。
- `skill-creator` validator: 変更する全スキルを `quick_validate.py` で検証する。
- 外部プラグイン: 現在のローカル開発運用は既存ツールで完結するため、導入を必須としない。Asana、ClickUp、Trello 等は外部タスク管理を正本にする場合だけ候補となるが、change record と状態が二重化するため現時点では提案しない。Codex Security はセキュリティ監査を別途行う依頼に限定して検討する。

## Acceptance criteria

| ID | Observable criterion | State |
| --- | --- | --- |
| AC1 | `AGENTS.md` が通常開発の委譲条件、モデル階層、主担当の設計・統合・最終レビュー責任を定義する。 | Verified |
| AC2 | `agent-task-orchestration` が task suitability、worker prompt、write scope、並列化、昇格、採用、コスト評価の観測可能なゲートを定義する。 | Verified |
| AC3 | DDD の change record が task owner、model tier、dependency、write scope、verification、completion evidence、state を記録できる。 | Verified |
| AC4 | Design/task-split、pre-implementation、checkpoint、final の各レビューが、担当、入力、判定、記録先を持つ。 | Verified |
| AC5 | Acceptance criteria と task plan が相互に追跡でき、未完了の runnable/local-dependent item がある状態で Implemented にできない。 | Verified |
| AC6 | UIスキルの文書化例外が DDD と矛盾せず、設計責務と検証責務が change record へ接続される。 | Verified |
| AC7 | 通常委譲と失敗分析の責務が重複せず、委譲失敗が再分割またはモデル昇格の記録へつながる。 | Verified |
| AC8 | 変更するすべてのスキルが `quick_validate.py` に成功し、placeholder、重複方針、検証不能な一般論がない。 | Verified |
| AC9 | 現在の未コミットなユーザー変更を上書きせず、本変更だけの diff としてレビューできる。 | Verified |

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | 現行スキル、change record、外部根拠を監査して設計する | Main + research workers | Main: High capability; workers: Cost efficient | - | Read-only + 本記録 | 公式資料、44件の記録、7スキルを照合 | Evidence and failure analysis | Verified |
| T2 | 本設計とタスク分割をユーザーが承認する | Main | High capability | T1 | 本記録 | ユーザーの明示承認 | Status: Approved | Verified |
| T3 | `agent-task-orchestration` と validator 対象を作る | Worker draft + Main integration | Cost efficient + High capability | T2 | `.codex/skills/agent-task-orchestration/**` | `quick_validate.py`、主担当レビュー | skill diff と検証結果 | Verified |
| T4 | DDD、format、AGENTS.md を更新する | Worker draft + Main integration | Cost efficient + High capability | T2, T3 | 指定3ファイル | validator、相互参照、diff review | AC1, AC3-AC5 | Verified |
| T5 | failure/UIスキルの責務と接続を更新する | Worker drafts with disjoint files + Main integration | Cost efficient + High capability | T2, T3 | 指定6スキル | 全 validator、重複・矛盾レビュー | AC6-AC8 | Verified |
| T6 | 独立した forward test と最終レビューを行う | Independent worker + Main | Cost efficient evaluator + High capability final review | T3-T5 | Read-only + 必要な修正 | realistic task simulation、全 validator、`git diff --check`、`git status` | 全ACの証拠、open itemゼロ | Verified |
| T7 | change record を Implemented へ更新し、本変更だけをコミットする | Main | High capability | T6 | 本記録と本変更 | diff/status/commit review | commit hash と最終記録 | Verified |

## Design and task-split review

- 各受け入れ基準は T3-T6 の少なくとも1タスクへ割り当てた。
- worker の書込範囲は新規スキル、基盤運用スキル、UIスキルに分離できる。共有する方針判断と最終文言は主担当が統合する。
- T4 と T5 は T3 の共通語彙が確定した後に開始し、相互に異なるファイルを編集できる。
- 外部調査と棚卸しは read-only の独立タスクとして実際にコスト効率モデルへ委譲し、主担当が一次資料とリポジトリ証拠を再確認した。
- モデル名は環境で変わるため tier を契約とし、具体名は実行時に利用可能なモデル一覧と公式資料から選ぶ。
- 本記録は Proposed であり、T3以降のスキル・AGENTS.md変更はユーザー承認まで開始しない。

## Pre-implementation review

- Reviewer: 高能力モデルの主担当。
- Inputs: Approved change record、T3-T5のwrite scope、利用可能モデル、既存の未コミット変更。
- Decision: T3-T5は実装可能で、各workerのwrite scopeは非重複。既存collector文書とproduction codeは対象外とする。設計・用語・最終採用は主担当が保持する。
- Follow-up: 各worker promptへ目的、対象範囲、禁止範囲、依存、検証、成果形式を記載し、cost-efficient tierで並列起動した。

## Checkpoint review

- Reviewer: 高能力モデルの主担当。
- Inputs: T3-T5の全diff、worker validator、forward test。
- Decision: 全worker成果を採用。ただし共有生成物・migration・契約凍結と、acceptance-blocking external blockerの完了判定に不足があったため、主担当が統合修正した。
- Follow-up: 統合修正後に全skill validatorとdiff/statusを再検証した。

## Verification record

- 2026-09-14: `.codegraph/` の存在、`git status`、7件のリポジトリスキル、change record format を確認した。
- 2026-09-14: 44件の既存 change record を read-only worker が監査し、owner/model/dependency/write scope/completion evidence とレビュー欄の標準化不足を確認した。
- 2026-09-14: リポジトリスキルを read-only worker と主担当で確認し、通常委譲ルールの欠落、UI文書化例外の曖昧さ、設計と検証の重複を確認した。
- 2026-09-14: 外部根拠を research worker へ委譲し、主担当が OpenAI 公式 Model guidance と Models ページを開いて現行のモデル役割と subagent 指示を確認した。
- 2026-09-14: 既存の未コミット変更 `docs/22-collector-design.md`、`docs/23-jra-scraping-redesign.md`、`docs/26-collection-platform-design.md`、`docs/changes/20260914_recent-race-detail-collection/` を確認し、変更していない。
- 2026-09-14: ユーザーが change record の対応を明示的に依頼したため、Status を Approved として Execution Mode へ移行した。
- 2026-09-14: 3つの cost-efficient worker に、(1) 新規 orchestration skill、(2) DDD・format・AGENTS.md、(3) failure/UI skills を非重複の write scope で委譲した。各workerのvalidatorと `git diff --check` は成功した。
- 2026-09-14: 主担当が全差分をレビューし、共有worktreeの生成物・migration snapshot・共有契約を共通write scopeとして扱う規則と、契約凍結後に依存workerを開始する規則を追加した。
- 2026-09-14: 主担当レビューで、acceptance-blockingな外部blockerを残したまま `Implemented` にできる曖昧さを検出し、DDDとformatを修正した。
- 2026-09-14: 独立workerのforward testで、API・永続化・Blazor UIをまたぐ共有worktree実装を題材に、task plan、tier、依存、write scope、昇格、lead reviewをスキル本文だけから導出できることを確認した。
- 2026-09-14: `PYTHONUTF8=1` を設定して全8スキルを `quick_validate.py` で再検証し、すべて `Skill is valid!` となった。Windows既定CP932では日本語スキルの読込に失敗するため、UTF-8指定を再現可能な検証コマンドとした。
- 2026-09-14: 本変更対象に対する `git diff --check` が成功した。`git status` で既存の別変更が残ることを確認し、本変更の対象へ混在させていない。

## Final review

- Reviewer: 高能力モデルの主担当。
- Inputs: Approved change record、workerの変更と検証報告、全diff、forward test、全skill validator、`git diff --check`、`git status`。
- Decision: AC1-AC9を満たす。通常委譲は新規skill、change recordのレビューはDDD、失敗後の再分割・昇格はfailure skill、UIの設計と検証は各UI skillへ分離され、重大な重複・矛盾・未完了項目はない。
- Follow-up: モデル提供状況・価格は固定せず、将来の実績でroutingを狭く調整する。外部タスク管理pluginは導入しない。

## Completion evidence

- AC1-AC2: `AGENTS.md` と `.codex/skills/agent-task-orchestration/` のdiff、およびforward test。
- AC3-AC5: DDD本体とchange-record formatのtask plan・4 review gates・zero-open-item規定。
- AC6-AC7: failure/UI skillの責務境界とchange record接続のdiff。
- AC8: UTF-8環境で全8skillのvalidator成功、placeholderはテンプレート例に限定。
- AC9: 本変更だけを明示的にstageし、既存のcollector文書変更を除外して確認する。

## Implementation result

- 新規 `agent-task-orchestration` skillが、固定モデル名に依存しないlead/worker/review tier、worker prompt contract、並列化、昇格、採用、結果単価の評価を定義した。
- DDDとchange-record formatが、タスク別owner/model tier/dependency/write scope/verification/evidence/stateと4段階レビューを標準化した。
- `AGENTS.md` が通常開発の分業原則と、設計・統合・最終判定を高能力モデルの主担当へ保持する規則をリポジトリ全体へ適用した。
- failure/UI skillsの重複と文書化例外を整理し、各判断と検証証拠をchange recordへ接続した。

## Deviations and follow-up

- T4/T5はtask plan上T3完了後としていたが、共通語彙と境界がApproved change recordで既に確定し、write scopeも非重複だったためT3と並行実行した。主担当がT3完成後に全相互参照と用語を統合レビューした。承認済みの外部仕様や受け入れ基準は変更していない。
- モデルの提供状況と価格は変わり得るため、スキル本文には固定価格を記載しない。

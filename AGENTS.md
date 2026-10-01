# Repository Guidelines

## Git Workflow

## Local development authentication

- 利用者は、このリポジトリのローカル開発環境（`localhost`）で動作確認するための認証情報入力を許可している。認証情報はローカル開発環境だけに使用し、ログ、コマンド出力、コミット、ドキュメントへ記録しない。
- 実行環境やブラウザーの安全規則が入力直前の確認を要求する場合は、その規則を優先する。

- 変更は、1つの目的として説明でき、単独でビルド・テスト・レビューできるまとまりごとにコミットする
- 機能追加、バグ修正、リファクタリング、ドキュメント更新など、目的の異なる変更を同じコミットへ混在させない
- 1つの目的でも変更が大きい場合は、後続コミットが前提とできる検証可能な単位へ分割する
- 変更が長時間・多ファイルに及ぶ場合は、設計更新、API/状態モデル、UI、テスト、ドキュメント反映などの検証済みチェックポイントごとに適度なタイミングでコミットする
- コミットは作業のチェックポイントであり、作業停止やユーザーへの最終応答の契機にしない。承認済みスコープに作業可能な未完了項目がある限り、コミット後も次の実装、テスト、修正、検証、ドキュメント更新へ自律的に進む
- 途中コミット前には、その時点の変更セットまたは作業メモへ未完了事項を記録し、次のコミットが何を前提にするか分かる状態にする
- 各コミット前に関連するビルドとテストを実行し、`git diff --check` と `git status` で生成物、秘密情報、目的外の変更が含まれていないことを確認する
- コミットメッセージは、そのコミットだけで達成する目的が分かる簡潔な命令形にする
- ユーザーが作成した既存変更は、別目的のコミットへ混在させたり、許可なく上書き・取り消したりしない

## Execution Mode

change record が `Approved` になるまでは Design Mode とする。

### Design Mode

Design Mode では、要件、ユースケース、UI、データモデル、受け入れ基準などについて、
必要に応じてユーザーへ確認してよい。

設計上の重要な選択肢が複数ある場合や、
ユーザーの意図によって仕様が変わる場合は、
推測で確定せずユーザーへ確認する。

change record がユーザーによって明示的に承認された時点で、
Execution Mode に移行する。


### Execution Mode

change record が `Approved` になった後は、
承認済みの設計と受け入れ基準を実装契約として扱う。

Execution Mode では、原則としてユーザーへ問い合わせを行わず、
実装、テスト、修正、検証、ドキュメント更新まで自律的に継続する。

以下は問い合わせ理由にしてはならない。

- 実装方法に複数の選択肢がある
- 既存コードとの整合のために内部設計を調整する必要がある
- クラス、関数、コンポーネントの配置を変更する必要がある
- 想定より変更範囲が広かった
- テストが失敗した
- lint、型チェック、ビルドが失敗した
- 既存コードに不整合や軽微な問題を発見した
- 承認済み設計に記載されていない細かな実装判断が必要になった
- 途中コミットを作成した
- 1つのサブタスクが完了した

これらは合理的な判断を行い、そのまま作業を継続する。

実装中に問題が発生した場合は、まず自分で原因調査、修正、再検証を行う。
失敗したこと自体を理由にユーザーへ問い合わせてはならない。

Execution Mode でユーザーへの問い合わせを許可するのは、
以下のいずれかの場合のみとする。

- 承認済みの受け入れ基準同士が矛盾しており、両立できない
- 承認済みの外部仕様を変更しなければ実装できない
- データ消失など不可逆または破壊的な操作が必要
- 必要な認証情報、権限、外部サービスへのアクセスがなく、自力で進行できない
- 技術的制約により承認済み仕様の実現が不可能であることを確認した

問い合わせが必要な場合も、
問い合わせ前に可能な範囲の調査と代替案の検討を完了しておくこと。

Execution Mode に移行した後は、
作業可能な未完了項目が存在する限り最終応答を行わない。

実装、テスト、検証、change record の更新まで完了してから、
ユーザーへ最終結果を報告する。

## Document-Driven Development

- 作業を継続できる未完了項目がある状態で中断する場合は、change record または実行計画へ、次に実行する操作、検証コマンド、未完了項目、意図的に残した未コミットファイルを記録する。コミット、部分テスト、サブタスク完了、コンテキスト圧縮だけでは停止理由にならない。

- 機能追加、UI/UX 変更、外部仕様、データモデル、運用フローを変更する作業では、実装前に `.codex/skills/document-driven-development/SKILL.md` を全文読み、そのワークフローに従う
- 変更ごとの企画、決定、受け入れ基準、検証結果は `docs/changes/yyyyMMdd_<change-name>/` に保存する。日付は変更セット作成日の8桁、変更名は短い kebab-case とする。企画時に作成したワイヤーフレーム、画面モック、図、比較案も同じ変更ディレクトリに含める
- change record の状態が `Approved` になるまでプロダクションコードを変更しない。承認とは、ユーザーが設計内容または当該 change record を明示的に確定したことを指す
- 実装中に承認済み設計から外れる必要が生じた場合は、先に change record を更新して再承認を得る。誤字修正や設計判断を変えない補足は再承認を要しない
- 実装完了時は、実装結果、設計との差分、実行した検証、残課題を change record に追記してから完了とする

### Agent task orchestration

- タスク状態の正規語彙は `Proposed`、`Runnable`、`In progress`、`Dependent`、`Externally blocked`、`Rejected with reason`、`Verified` とする。`Implemented` 判定前に、承認済みスコープの `Rejected with reason`、`In progress`、`Runnable`、`Dependent`、その他の未完了状態、または受け入れ基準を阻害する `Externally blocked` を残してはならない。`Externally blocked` は、承認済み受け入れ基準に影響しない明示的な除外フォローアップに限り残せる。
- 最終応答前に、承認済みタスク・受け入れ基準・重要なレビュー指摘を完了証拠または真正な外部 blockerへ追跡できることを、Leadがリスクに応じた粒度で確認する。最終受け入れの責任はLeadにあるが、通常のレビューや受け入れで高性能モデルを必須にしない。監査で将来の作業にも影響するスキル不足・委譲失敗が実証された場合は、`learn-from-implementation-failures` の事実確認、最小修正、validator、再検証を実施し、結果を change record に記録する。単発の軽微な実装ミスは実装内で修正する。

- 通常の開発・調査では `agent-task-orchestration` の Router → Cheap Planner → Cheap Executor → Verifier の流れを使う。これらは論理的な役割であり、必ず別agentとして起動する必要はない。PlannerとExecutorは同じagentでもよい。
- 適格な通常実装、調査、テスト作成、build・test・lint・format等の検証、局所修正の既定要求は `gpt-6-luna`、reasoning effort `high` とする。これはユーザー指定のrouting preferenceであり、優越性や低コストの実証ではない。高性能モデルは重要な仕様・設計判断と再計画を担うStrong Plannerに限り、通常実装のexecutorとして使わない。
- Leadは要件、承認、public contract、architecture、risk判断、タスク分割、再計画判断、統合と最終受け入れに責任を持つ。委譲成果は自己申告だけで採用せず、独立証拠で確認する。タスクの複雑さをファイル数だけで決めない。
- コード編集前にCheap Plannerは対象と類似実装、命名・DI・依存関係、テスト、適用設計資料を調べ、目的、調査結果、変更予定ファイル、手順、残る仕様上の不明点、検証コマンドを短く記録する。Repositoryで分かることを質問せず、結果に重要な影響を及ぼす仕様だけを質問する。
- Cheap Executorは承認済み計画と範囲に従い、不要なリファクタリング、新しい抽象化、ついでの改善を加えない。範囲が膨らむときは該当箇所の編集を止めて内部で再計画し、独立した承認済み作業を続ける。要件、外部契約、受け入れ基準、material riskを変える場合は `Proposed` に戻す。
- Verifierは計画で指定した機械的なbuild、test、formatter、lint、type、architecture check等を実行し、その証拠を完了条件とする。LLMの自己評価だけで検証を通過させない。
- Verifier失敗時はCheapが原因を分類し、局所的な実装ミスやRepository調査不足を修正して該当コマンドを再実行する。異なる原因の失敗数だけで昇格しない。同じ根本原因がおよそ2回続いたときRouterが再評価するが、自動昇格はしない。RouterはPlanner完了後、検証失敗後、範囲拡大時、同原因の反復時に再実行する。
- Strong Plannerは重大な曖昧さ、設計矛盾、重要なarchitecture/public-contract tradeoff、またはCheap向けへ分解できない大きさの場合に限る。Strong Plannerは整理・再分解した実行可能な順序付き指示をCheap Executorへ戻し、自ら実装しない。Cheapが利用不可でもStrongによる通常実装へ黙って切り替えず、実行を外部blockerとして記録する。
- 並列化は入力、依存、書込範囲、検証が分離でき、統合・レビュー・再作業を含む総費用が見合う場合だけ行う。単一ファイルや共有状態のownerは一つとし、速さだけで並列化しない。
- worker指示にはAC ID、frozen decision/invariant、変更禁止判断、exact write scope、反例、実装自由度、Leadへ戻す境界、検証と成果形式を含める。reviewは既存AC↔Task対応をgroupとして統合成果を一回確認し、gate failure、設計変更、scope/ownership逸脱、risk/独立証拠不足、過大なretry/review burdenの場合だけtask/diffへ掘り下げる。
- coding workerには作成・更新するtest、実装中に回す最小test、handoff前の関連regression、期待結果を指示する。test変更が不要なら理由と既存の独立証拠を示す。workerは指定testを実行して成功結果を返すまで完了扱いにせず、実行不能ならblockerを明示してtaskを未完了のまま主担当へ戻す。
- delegated coding taskはchange record配下のagent auditへ、requested/observed model、observation source、token availability、task difficulty、patch attribution、quality/scope gates、独立反証、retry/promotion、escaped defectを記録する。observed modelやtokenを取得できない場合は未確認とし、requested値から推測しない。
- 成功結果当たりのcostにはworkerだけでなく、automated reviewer、利用者が提供した場合のhuman active review、指摘対応、corrective implementation、promotion、再検証、audit overheadを含める。人間単価やprovider料金根拠がない値を通貨へ換算しない。同程度のtaskと帰属可能なpatchだけを比較する。
- ユーザー指定によるmodel/reasoning defaultの変更は、効率の実証的主張なしに適用できる。効率改善の結論には、最初の5件以上の比較可能な成功結果と既存auditの証拠を使う。重大なsecurity/data/scope/false-completion failure、または反復するretry・correction・gate failure・review burdenがあれば既定routeを再検討またはrollbackする。requested modelとobserved model、利用状況telemetryを区別し、取得できない値は未確認とする。
- DDD の Design/task-split、Pre-implementation、Checkpoint、Final review を各 change record で実施し、設計・分割・統合・完了判定の判断を記録する。
- change recordの承認依頼前に、エージェントは要件衝突、設計・データ・セキュリティ、外部制約、移行・並行性・復旧、検証blind spot、cost/review工数、caller assumption、却下案から、承認判断または成否を変えるmaterial concernを能動的に提示する。各懸念は根拠、影響、推奨処置、代替、残存risk、AC/task/反例、agent position、user dispositionを持ち、`Resolved in design`、`Accepted risk`、`Excluded follow-up`、`Open decision`で管理する。`Open decision`または根拠あるagent objectionが未解決なら承認・実装へ進まない。material concernがない小変更は確認観点と短い結論だけでよい。
- 人とエージェントの合意は、エージェントの無条件同意や利用者への責任移転ではない。安全性・受け入れ基準と衝突する場合は根拠と代替を示し、未合意のまま進めない。可逆的な好みは利用者の明示判断を尊重する。実装中に新しいmaterial design concernが出た場合は`Proposed`へ戻し、承認範囲内の局所欠陥はclosure itemと反例testを追加して自律修正する。
- 行動を変えない誤字修正またはコメントだけの明確化は change record 不要とする。それ以外の機能、UI/UX、外部仕様、データモデル、運用フロー、レビュー判断に影響する変更は、規模にかかわらず DDD の change record を作成する。

<!-- CODEGRAPH_START -->
## CodeGraph

In repositories indexed by CodeGraph (a `.codegraph/` directory exists at the repo root), reach for it BEFORE grep/find or reading files when you need to understand or locate code:

- **MCP tool** (when available): `codegraph_explore` answers most code questions in one call — the relevant symbols' verbatim source plus the call paths between them, including dynamic-dispatch hops grep can't follow. Name a file or symbol in the query to read its current line-numbered source. If it's listed but deferred, load it by name via tool search.
- **Shell** (always works): `codegraph explore "<symbol names or question>"` prints the same output.

If there is no `.codegraph/` directory, skip CodeGraph entirely — indexing is the user's decision.
<!-- CODEGRAPH_END -->

## Workflow change record

The cheap-first workflow policy change and its review evidence are tracked in [20261001_cheap-first-agent-workflow](docs/changes/20261001_cheap-first-agent-workflow/README.md).

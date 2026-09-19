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
- 最終応答前に、承認済みタスク・受け入れ基準・重要なレビュー指摘を完了証拠または真正な外部 blockerへ追跡できることを、高能力モデルの主担当がリスクに応じた粒度で確認する。監査で将来の作業にも影響するスキル不足・委譲失敗が実証された場合は、`learn-from-implementation-failures` の事実確認、最小修正、validator、再検証を実施し、結果を change record に記録する。単発の軽微な実装ミスは実装内で修正する。

- 通常の開発・調査で複数の作業へ分割できる場合は、`agent-task-orchestration` を適用する。適用対象は、入力、成果物、書込範囲、検証方法を明確に分離できる探索、調査、定型実装、テスト候補作成、文書棚卸しなどである。単一の短い作業や、分割による調整コストが成果を上回る作業には適用しない。
- 主担当は、高能力モデルで要件解釈、設計、タスク分割、依存関係、委譲条件、成果の統合、設計適合レビュー、最終レビューを保持する。明確で独立した作業は、利用可能なコスト効率の高いモデルへ委譲してよいが、委譲成果を自己申告だけで採用しない。
- タスクごとに owner、model tier、依存、write scope、verification、completion evidence、state を記録する。同一ファイルまたは共有状態を並列タスクが書き込まないよう、owner を一つにする。要件が曖昧、設計・移行・セキュリティ・データ損失リスクを含む、統合が複雑、完了条件を独立検証できない、または失敗を反復する場合は、主担当が実行するか上位モデルへ昇格する。
- 安価なモデルの利用は単価だけで評価せず、初回受入、再試行、レビュー指摘、修正時間、検証失敗を必要な範囲で記録し、成功結果あたりの総費用が悪化する場合は routing を見直す。
- 明確で局所的かつ独立検証可能なcoding taskは、現在利用可能なcost-sensitive coding workerを第一候補にする。architecture、public contract、persistence/migration、concurrency、security/privacy、破壊的操作、曖昧な要件、統合・最終判定は主担当または上位tierに保持する。1回のfocused correction後もgateを満たさない、またはscopeが拡大した場合は一段階昇格する。
- delegated coding taskはchange record配下のagent auditへ、requested/observed model、observation source、token availability、task difficulty、patch attribution、quality/scope gates、独立反証、retry/promotion、escaped defectを記録する。observed modelやtokenを取得できない場合は未確認とし、requested値から推測しない。
- 成功結果当たりのcostにはworkerだけでなく、automated reviewer、利用者が提供した場合のhuman active review、指摘対応、corrective implementation、promotion、再検証、audit overheadを含める。人間単価やprovider料金根拠がない値を通貨へ換算しない。同程度のtaskと帰属可能なpatchだけを比較する。
- persistentなmodel/reasoning/budget/task-boundary改善は、反復する比較可能evidence、重大なsecurity/data/scope/false-completion failure、または5件以上の成功標本に基づき、一段階の変更、validator、独立forward test、観測期間、rollback条件を持たせる。自発的にrecommendationと承認済み範囲内の再指示・再分割・昇格は行えるが、未承認scopeへ拡張しない。
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

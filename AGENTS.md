# Repository Guidelines

## Git Workflow

## Local development authentication

- 利用者は、このリポジトリのローカル開発環境（`localhost`）で動作確認するための認証情報入力を許可している。認証情報はローカル開発環境だけに使用し、ログ、コマンド出力、コミット、ドキュメントへ記録しない。
- 実行環境やブラウザーの安全規則が入力直前の確認を要求する場合は、その規則を優先する。

- 変更は、1つの目的として説明でき、単独でビルド・テスト・レビューできるまとまりごとにコミットする
- 機能追加、バグ修正、リファクタリング、ドキュメント更新など、目的の異なる変更を同じコミットへ混在させない
- 1つの目的でも変更が大きい場合は、後続コミットが前提とできる検証可能な単位へ分割する
- 変更が長時間・多ファイルに及ぶ場合は、設計更新、API/状態モデル、UI、テスト、ドキュメント反映などの検証済みチェックポイントごとに適度なタイミングでコミットする
- 途中コミット前には、その時点の変更セットまたは作業メモへ未完了事項を記録し、次のコミットが何を前提にするか分かる状態にする
- 各コミット前に関連するビルドとテストを実行し、`git diff --check` と `git status` で生成物、秘密情報、目的外の変更が含まれていないことを確認する
- コミットメッセージは、そのコミットだけで達成する目的が分かる簡潔な命令形にする
- ユーザーが作成した既存変更は、別目的のコミットへ混在させたり、許可なく上書き・取り消したりしない

## Document-Driven Development

- 機能追加、UI/UX 変更、外部仕様、データモデル、運用フローを変更する作業では、実装前に `.codex/skills/document-driven-development/SKILL.md` を全文読み、そのワークフローに従う
- 変更ごとの企画、決定、受け入れ基準、検証結果は `docs/changes/yyyyMMdd_<change-name>/` に保存する。日付は変更セット作成日の8桁、変更名は短い kebab-case とする。企画時に作成したワイヤーフレーム、画面モック、図、比較案も同じ変更ディレクトリに含める
- change record の状態が `Approved` になるまでプロダクションコードを変更しない。承認とは、ユーザーが設計内容または当該 change record を明示的に確定したことを指す
- 実装中に承認済み設計から外れる必要が生じた場合は、先に change record を更新して再承認を得る。誤字修正や設計判断を変えない補足は再承認を要しない
- 実装完了時は、実装結果、設計との差分、実行した検証、残課題を change record に追記してから完了とする

<!-- CODEGRAPH_START -->
## CodeGraph

In repositories indexed by CodeGraph (a `.codegraph/` directory exists at the repo root), reach for it BEFORE grep/find or reading files when you need to understand or locate code:

- **MCP tool** (when available): `codegraph_explore` answers most code questions in one call — the relevant symbols' verbatim source plus the call paths between them, including dynamic-dispatch hops grep can't follow. Name a file or symbol in the query to read its current line-numbered source. If it's listed but deferred, load it by name via tool search.
- **Shell** (always works): `codegraph explore "<symbol names or question>"` prints the same output.

If there is no `.codegraph/` directory, skip CodeGraph entirely — indexing is the user's decision.
<!-- CODEGRAPH_END -->

# CodeGraph local setup

- Status: Implemented
- Change record schema: 2
- Owner: Lead
- Created: 2026-09-30
- Updated: 2026-09-30

## Context and approval

APIクライアント実装中、利用者が「CodeGraphを導入してください。各エージェントにも使用するように伝えてください。」と明示指示した。既存`.codex/config.toml`とAGENTS.mdに設定・使用規則があり、CLIとインデックスだけが欠けていた。公式CLIの導入、既存設定の利用、初回index、稼働中エージェントへの通知を承認範囲として実施。

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Verified | アプリコード変更なし。公式CLI 1.6.1をユーザー環境へ導入 |
| Verification | Verified | version/status/explore成功、1,043 files / 13,402 nodes / 40,147 edges |
| Deployment/operation | Verified | ユーザーPATHへnpmディレクトリ追加、両稼働エージェントへCLIパス通知 |

## Decisions and documentation updates

- 公式`@colbymchenry/codegraph`を`npm.cmd install --global`で導入。実行ファイルは`C:/Users/yuto.nagano/AppData/Roaming/npm/codegraph.cmd`。
- 最新プロセスのPATHに依存しないよう当該チャットでは絶対パスを使用。将来のプロセス向けユーザーPATHにもnpmディレクトリを追加した。
- `.codex/config.toml`の既存`codegraph serve --mcp`設定は変更不要。現在のチャットでMCP再接続は確認していないため、利用可能なCLIで実行する。MCPはPATHを取り込んだアプリの再起動/接続後に利用する。
- 匿名telemetryはoff。背景daemonは当該CLI呼出で`CODEGRAPH_NO_DAEMON=1`として起動せず、必要時に明示syncする。
- `.codegraph`の生成物は既存ignore対象。バイナリ/DB/キャッシュをコミットしない。
- 正規文書更新は不要。AGENTS.mdとDDDスキルには既にCodeGraph優先調査・sync規則が存在する。APIクライアント変更記録へ利用可能になった事実を追記する。
- 参考: [公式導入手順](https://github.com/colbymchenry/codegraph#get-started)（npm package名を公式READMEで確認）。

## Concern and agreement ledger

| ID | Concern and evidence | Impact | Proposed disposition | AC/task/test | Agent position | User disposition | State |
| --- | --- | --- | --- | --- | --- | --- | --- |
| C1 | 既存プロセスは更新前PATHを保持 | bare commandやMCPが当該チャットで解決できない | 現在は絶対CLIパス。次回起動用PATHは永続追加。MCP接続成功は未主張 | AC1/AC2/T1; explore | Agree | 導入・全エージェント使用を明示指示 | Resolved in design |

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | CodeGraphを実行しrepository indexへ問い合わせできる | T1 | version 1.6.1、status up to date、IRaceQueryService.cs explore source成功 | Verified |
| AC2 | 稼働エージェントへ使用方法と変更後syncを通知する | T1 | contract_migration / inventoryへ絶対パスとsync規則を送信 | Verified |

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State | Routing | Audit | Result metrics |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | CLI導入、初回index、通知 | Lead | Lead | - | ユーザーnpm環境・PATH・telemetry設定; .codegraph | version/status/explore | 上記AC証拠 | Verified | Lead — single short task; 環境権限と進行中エージェント通知を直接処理 | none | unavailable; retries 0; corrections 0; reviews 1 |

## Verification record

- 導入前: CLI未検出、`.codegraph`はignoreファイルのみ。
- `npm.cmd install --global @colbymchenry/codegraph`: 成功。
- `codegraph --version`: 1.6.1。
- `codegraph init --yes .`: 1,043 files / 13,402 nodes / 40,147 edges。
- `codegraph status .`: up to date。
- `codegraph explore src/HorseRacingPrediction.ApiClient/IRaceQueryService.cs`: 12 symbols、現在sourceを取得。
- `codegraph install --print-config codex`: 既存project設定と一致するcommand/argsを確認。
- 最終確認: 利用者要求をCLI実行と両エージェント通知へ追跡。MCP再接続はCLI使用を阻害しないため完了条件に含めない。

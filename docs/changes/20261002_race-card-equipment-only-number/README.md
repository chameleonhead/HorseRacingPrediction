# 装具表示だけの出馬表馬番セルを未確定として扱う

- Status: Approved
- Change record schema: 2
- Orchestration schema: 2
- Owner: Main/Lead
- Created: 2026-10-02
- Updated: 2026-10-02

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Not started | 承認後、出馬表の馬番セル専用解析を修正する |
| Verification | Not started | 実DOM形状の回帰テスト、既存解析テスト、format/buildを実行する |
| Deployment/operation | Not applicable | ローカル解析コードの修正であり、本変更内でデプロイ操作は行わない |

## Context

2026-10-04 東京11Rの出馬表取得で、`HorseNumber` の生値が `ブリンカー着用` となり `JraValueParseException` が発生した。

2026-10-02に対象URLの公式HTMLを読み取り確認したところ、該当する3頭の行はいずれも次の形だった。

```html
<td class="waku"></td>
<td class="num"><span class="horse_icon blinker"><img ... alt="ブリンカー着用" /></span></td>
```

枠番・馬番は未確定で空欄だが、馬番セル内の装具アイコンには代替テキストが存在する。`JraCellView.Create` はセル本文が空の場合に最初のフラグメントの本文またはAccessibleNameで補完するため、装具説明をセル本文として採用する。その値を `RaceCardPageParser` が馬番として厳格検証し、今回の例外になる。

## Goals

- 馬番未確定で装具アイコンだけが表示される出馬表を正常に解析する。
- 装具表示を馬番として解釈せず、`HorseNumber = null` として既存の未確定番号フローへ渡す。
- 数字、`馬番N` のアクセシブル表現、`取消`、`除外`、不正値の既存契約を維持する。

## Non-goals

- JRA共通スナップショットの代替テキスト補完規則全体の変更。
- ブリンカー着用情報をドメインモデルへ保存する機能追加。
- 過去に失敗した収集タスクや永続データの手動補正、再実行、デプロイ。

## Documentation updates

変更記録以外の文書更新は不要。`docs/23-jra-scraping-redesign.md` と既存の馬番未確定・取消対応change recordを確認したが、今回の修正は既存の「未確定馬番はnull」契約を実DOMの追加形状へ適用する局所的なアダプター修正であり、正準設計を変更しない。

## Technical impact

- `RaceCardPageParser` の馬番値取得を、汎用的な補完済みセル本文の直接検証から馬番セル専用解析へ切り出す。
- セル本文またはフラグメントに明示的な馬番（数字または`馬番N`）があれば1～18として解析する。
- セル本文が空、または馬番を表さない装具フラグメントだけなら未確定としてnullを返す。
- `取消`・`除外`は参加状態として従来どおり処理する。
- 装具表示以外の未知の非空値は従来どおり例外にし、外部DOM変更を黙って無視しない。

## Hypothesis ledger

| ID | Claim | Fact/inference boundary | Supporting/contradicting evidence | Falsification and result | Disposition |
| --- | --- | --- | --- | --- | --- |
| H1 | 例外は列ずれではなく、未確定馬番セルの装具alt補完で起きる | 公式HTMLとコード経路は事実。ブラウザーでの最終スナップショット再現は実装時にテスト化する | 公式HTMLで`td.num`にブリンカー画像だけがあり、報告値がそのaltと一致。3件すべて枠・馬番が空。既存の「数字+アイコン」テストは成功するため反証にならない | 同じHTML形状をPlaywright snapshotter→parserへ通し、修正前に同例外となることを確認する。公式HTML形状自体は読取確認済み | Supported; T1で実行可能な回帰証拠へ固定 |
| H2 | 正しい結果は馬番の推測ではなくnull | 公式HTMLに番号が存在しない事実と、既存の未確定番号契約からの結論 | `RaceEntry.HorseNumber`はnullableで、番号未確定カードの既存テストがある。行位置からの仮採番は禁止されている | 装具なし空欄カードおよび装具だけのカードで、ともにnullになることを比較する | Resolved in design |
| H3 | 共通snapshotterを変更せずパーサーで意味付けするのが最小安全範囲 | 設計判断 | alt補完は他のテーブルで意味のある情報を保持する。問題はHorseNumberというフィールド固有の解釈 | パーサー専用テストとsnapshotter回帰を実行し、他用途のalt保持を変えない | Resolved in design |

## Decisions

1. 馬番はDOM上に明示された値だけを採用し、行番号・表示順・枠番から推測しない。
2. `horse_icon` 内の装具説明だけで馬番表現を持たないセルは、空セルと同じ未確定扱いにする。
3. `馬番N` のAccessibleNameによる既存の番号取得は維持する。
4. 共通スナップショット層は変更しない。JRA出馬表のフィールド意味論をパーサー側で処理する。

## Concern and agreement ledger

| ID | Concern and evidence | Impact | Proposed disposition | AC/task/test | Agent position | User disposition | State |
| --- | --- | --- | --- | --- | --- | --- | --- |
| C1 | 装具altを無条件に空扱いすると、将来そこに番号も含まれる表現を取りこぼす可能性がある | 正式番号があるのにnull化し得る | セル本文と全フラグメントから明示的な番号表現を先に探し、番号がない装具情報だけを未確定扱いにする | AC1/AC2, T1 | 推奨 | Pending | Resolved in design |
| C2 | 任意の未知文字列をnull化するとJRA DOM変更を隠す | サイレントなデータ欠落 | 許容する非番号表現は確認済み装具クラス/説明に限定し、それ以外は従来どおり例外 | AC3, T1 | 必須 | Pending | Resolved in design |
| C3 | 公式ページは時間経過で内容が変わる | live URLだけでは回帰証拠が再現不能になる | 公式HTMLで確認した最小DOM形状をテストfixtureへ固定し、live取得は補助証拠とする | AC1, T1/T2 | 推奨 | Pending | Resolved in design |
| C4 | 共通snapshotter修正は他parserへ影響する | 回帰範囲とレビュー負担が増える | snapshotterの契約は維持し、RaceCard HorseNumber専用処理だけ変更する | AC4, T1/T2 | 推奨 | Pending | Resolved in design |

material concernは上記で設計上解消しており、Open decisionはない。永続化、セキュリティ、破壊的操作、移行、並行性への影響はない。

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | 枠番・馬番が空で`horse_icon blinker`画像だけを含む公式形状の行を、例外なく`HorseNumber = null`として解析できる | T1 | Playwright HTML→snapshotter→parser回帰テスト | Not started |
| AC2 | 数字とブリンカー画像が同居するセル、および`馬番N` AccessibleName表現では正式番号を保持する | T1 | 既存テストと追加の反例テスト | Not started |
| AC3 | `取消`・`除外`は従来の参加状態を維持し、未知の非空非番号値は`JraValueParseException`になる | T1 | cancellation/publication parser tests | Not started |
| AC4 | 関連スクレイピング回帰、build、CI同等format検証が成功し、共通snapshotterの公開契約を変更しない | T2 | 関連test、build、`dotnet format ... --verify-no-changes`、diff/status監査 | Not started |

## Delivery plan

設計承認後にT1を実装し、T2で独立検証する。T1とT2は検証対象が同じため直列化する。

## Task plan

| ID | Task | Owner | Model tier | Routing | Depends on | Write scope | Verification | Completion evidence | Audit | Result metrics | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | 専用馬番解析と実DOM形状の回帰テストを実装 | Bounded coding worker | Worker tier | Luna/high — frozen parser rule、局所・可逆・独立検証可能 | Approval | `src/HorseRacingPrediction.Scraping/Jra/Parsing/RaceCardPageParser.cs`; `tests/HorseRacingPrediction.Scraping.Tests/Parsing/RaceCardPublicationTests.cs` | 対象test classとScraping tests | attributable diff、成功コマンド、[T1-A1 audit](agent-audits/T1-A1.json) | T1-A1 | unavailable; retries 0; corrections 0; reviews 0 | Runnable |
| T2 | 統合結果を反証レビューし、全gateを検証して記録 | Main/Lead | Lead tier | Lead — 独立受入・統合・最終判定は委譲しない | T1 | このchange recordのみ | counterexample確認、関連test/build/format/diff/status | Verification recordとfinal review | none | unavailable; retries 0; corrections 0; reviews 0 | Dependent |

T1 workerはテスト作成と最小・関連regressionを実行する。T2 Leadがworker自己申告とは独立にテスト、差分、スコープを確認する。T1が共通snapshotter、公開契約、永続化へ拡張を要する場合、またはfocused correction後も検証不能の場合はLeadへ戻す。

## Review gates

- **Design and task-split review (2026-10-02, Main/Lead):** 公式HTML、現行parser、snapshot投影、既存回帰テストを入力として確認。意味判断はLeadが保持し、凍結した局所実装だけをworkerへ委譲する。書込scopeは直列かつ排他的。AC↔task↔verificationは全件対応。
- **Concern and agreement review (2026-10-02, Main/Lead):** caller assumption、外部DOM、誤null化、回帰blind spot、データ・セキュリティ・移行・復旧・cost/review burdenを確認。C1～C4を設計で解消し、Open decisionなし。ユーザー承認待ち。
- **User approval (2026-10-02):** ユーザーが提示した設計、AC1～AC4、C1～C4の処置を「お願いします」と明示承認。
- **Pre-implementation review (2026-10-02, Main/Lead):** T1をRunnable、T2をT1依存のDependentと分類。T1のexclusive write scope、既存の数字+装具・AccessibleName・取消/除外・garbage反例、最小test class、Scraping regressionをworker契約へ固定した。共通snapshotter、公開契約、永続化へ変更が必要ならLeadへ戻す。
- **Checkpoint review:** T1統合後にACグループ単位で記録する。
- **Final review:** 全ACとtaskの証拠を照合後に記録する。

## Verification record

- Design evidence: 対象JRA URLはHTTP 200、公式HTML内の`ブリンカー着用`は3件。各該当行の`td.waku`は空、`td.num`はブリンカー画像のみ。
- Existing evidence: `Parse_HtmlHorseNumberWithBlinkerIcon_PreservesOfficialNumber` は「数字3 + アイコン」を対象とし、今回の「数字なし + アイコン」を覆っていない。

## Deviations and follow-up

なし。承認前のため実装は未開始。

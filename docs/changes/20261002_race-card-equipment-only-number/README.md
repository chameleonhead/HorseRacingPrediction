# 装具表示だけの出馬表馬番セルを未確定として扱う

- Status: Implemented
- Change record schema: 2
- Orchestration schema: 2
- Owner: Main/Lead
- Created: 2026-10-02
- Updated: 2026-10-02

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Complete | `439ecbec`で専用判定と実DOM形状テストを追加 |
| Verification | Complete | focused 15、Scraping non-External 364、solution non-External 1,513（1 skip）、format、Release build成功 |
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
| H1 | 例外は列ずれではなく、未確定馬番セルの装具alt補完で起きる | 公式HTMLとコード経路は事実。ブラウザーでの最終スナップショット再現もテスト化した | 公式HTMLで`td.num`にブリンカー画像だけがあり、報告値がそのaltと一致。3件すべて枠・馬番が空。Playwright snapshotter→parserテストで同じ投影経路を再現 | 公式形状の回帰テストが修正後に`HorseNumber=null`を確認 | Verified |
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
| C1 | 装具altを無条件に空扱いすると、将来そこに番号も含まれる表現を取りこぼす可能性がある | 正式番号があるのにnull化し得る | 既存の数字・`馬番N`解析を先行維持し、確認済みの装具だけの形状に限定する | AC1/AC2, T1 | 推奨 | Approved | Resolved in design |
| C2 | 任意の未知文字列をnull化するとJRA DOM変更を隠す | サイレントなデータ欠落 | `horse_icon blinker`と画像altの両方が一致する場合だけ許容し、それ以外は従来どおり例外 | AC3, T1 | 必須 | Approved | Resolved in design |
| C3 | 公式ページは時間経過で内容が変わる | live URLだけでは回帰証拠が再現不能になる | 公式HTMLで確認した最小DOM形状をPlaywrightテストfixtureへ固定した | AC1, T1/T2 | 推奨 | Approved | Resolved in design |
| C4 | 共通snapshotter修正は他parserへ影響する | 回帰範囲とレビュー負担が増える | snapshotterは変更せずRaceCard HorseNumber専用処理だけ変更した | AC4, T1/T2 | 推奨 | Approved | Resolved in design |

material concernは上記で設計上解消しており、Open decisionはない。永続化、セキュリティ、破壊的操作、移行、並行性への影響はない。

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | 枠番・馬番が空で`horse_icon blinker`画像だけを含む公式形状の行を、例外なく`HorseNumber = null`として解析できる | T1 | Playwright HTML→snapshotter→parser回帰テスト | Verified |
| AC2 | 数字とブリンカー画像が同居するセル、および`馬番N` AccessibleName表現では正式番号を保持する | T1 | publication tests 15件成功 | Verified |
| AC3 | `取消`・`除外`は従来の参加状態を維持し、未知の非空非番号値は`JraValueParseException`になる | T1 | cancellation/publication parser tests成功 | Verified |
| AC4 | 関連スクレイピング回帰、build、CI同等format検証が成功し、共通snapshotterの公開契約を変更しない | T2 | CI同等test/build/format、diff/status監査成功 | Verified |

## Delivery plan

設計承認後にT1を実装し、T2で独立検証する。T1とT2は検証対象が同じため直列化する。

## Task plan

| ID | Task | Owner | Model tier | Routing | Depends on | Write scope | Verification | Completion evidence | Audit | Result metrics | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | 専用馬番解析と実DOM形状の回帰テストを実装 | Bounded coding worker | Worker tier | Luna/high — frozen parser rule、局所・可逆・独立検証可能 | Approval | `src/HorseRacingPrediction.Scraping/Jra/Parsing/RaceCardPageParser.cs`; `tests/HorseRacingPrediction.Scraping.Tests/Parsing/RaceCardPublicationTests.cs` | 対象test classとScraping tests | `439ecbec`、成功コマンド、[T1-A1 audit](agent-audits/T1-A1.json) | T1-A1 | unavailable; retries 0; corrections 0; reviews 1 | Verified |
| T2 | 統合結果を反証レビューし、全gateを検証して記録 | Main/Lead | Lead tier | Lead — final acceptance・統合・最終判定は委譲しない | T1 | このchange recordのみ | counterexample確認、関連test/build/format/diff/status | Verification recordとfinal review | none | unavailable; retries 0; corrections 0; reviews 1 | Verified |

T1 workerはテスト作成と最小・関連regressionを実行する。T2 Leadがworker自己申告とは独立にテスト、差分、スコープを確認する。T1が共通snapshotter、公開契約、永続化へ拡張を要する場合、またはfocused correction後も検証不能の場合はLeadへ戻す。

## Review gates

- **Design and task-split review (2026-10-02, Main/Lead):** 公式HTML、現行parser、snapshot投影、既存回帰テストを入力として確認。意味判断はLeadが保持し、凍結した局所実装だけをworkerへ委譲する。書込scopeは直列かつ排他的。AC↔task↔verificationは全件対応。
- **Concern and agreement review (2026-10-02, Main/Lead):** caller assumption、外部DOM、誤null化、回帰blind spot、データ・セキュリティ・移行・復旧・cost/review burdenを確認。C1～C4を設計で解消し、Open decisionなし。ユーザー承認待ち。
- **User approval (2026-10-02):** ユーザーが提示した設計、AC1～AC4、C1～C4の処置を「お願いします」と明示承認。
- **Pre-implementation review (2026-10-02, Main/Lead):** T1をRunnable、T2をT1依存のDependentと分類。T1のexclusive write scope、既存の数字+装具・AccessibleName・取消/除外・garbage反例、最小test class、Scraping regressionをworker契約へ固定した。共通snapshotter、公開契約、永続化へ変更が必要ならLeadへ戻す。
- **Checkpoint review (2026-10-02, Main/Lead):** AC1～AC3を統合差分とPlaywright経路で確認。許容条件は`numberText=ブリンカー着用`、`horse_icon blinker`、画像altの三条件に限定され、数字・取消/除外・未知値の分岐を保持。workerの無条件full Scraping testでExternal 25件が失敗したため、CIと同じ除外条件で独立再実行し364件成功。focused correctionやLead code correctionは不要。
- **Final review (2026-10-02, Main/Lead):** T1/T2とAC1～AC4をVerifiedへ照合。production parser→snapshot projection→Playwright fixtureの実経路が接続され、共通snapshotter変更なし。Release build、solution non-External 1,513件、format、diff/status、audit validator成功を確認。承認範囲の未完了task・findingなし。

## Verification record

- Design evidence: 対象JRA URLはHTTP 200、公式HTML内の`ブリンカー着用`は3件。各該当行の`td.waku`は空、`td.num`はブリンカー画像のみ。
- Existing evidence: `Parse_HtmlHorseNumberWithBlinkerIcon_PreservesOfficialNumber` は「数字3 + アイコン」を対象とし、今回の「数字なし + アイコン」を覆っていない。
- `dotnet test ... --filter FullyQualifiedName~RaceCardPublicationTests`: 15 passed。
- `dotnet test tests/HorseRacingPrediction.Scraping.Tests/... --filter "TestCategory!=External"`: 364 passed。
- `dotnet format HorseRacingPrediction.sln --no-restore --verify-no-changes`: exit 0。
- `dotnet build HorseRacingPrediction.sln --no-restore --configuration Release`: success、0 errors、既存nullable warning 1件。
- `dotnet test HorseRacingPrediction.sln --no-build --configuration Release --filter "TestCategory!=External"`: 1,513 passed、1 skipped、0 failed。
- `git diff --check`: success。実装commit `439ecbec`はparserと対応testだけを変更。

## Deviations and follow-up

workerが無条件で実行したScraping testではExternalなJRA live navigation/page-contentテスト25件が外部ページ状態により失敗した。CI契約は`TestCategory!=External`であり、同条件のScraping 364件とsolution 1,513件は成功したためAC4を阻害しない。デプロイ・過去失敗タスクの再実行はNon-goalsどおり未実施。

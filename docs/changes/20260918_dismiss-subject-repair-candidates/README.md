# 主体識別の古い失敗通知を画面から対応不要にする

- Status: Approved
- Owner: Main
- Created: 2026-09-18
- Updated: 2026-09-18

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Complete | notification限定の状態遷移、管理API、設定画面の選択・確認Dialog・結果表示を実装した。 |
| Verification | In progress | focused/full testは通過。デプロイ後のdesktop/narrow browser確認を残す。 |
| Deployment/operation | Not started | 実装・検証後にデプロイし、本番の古い4件で表示消去と履歴保持を確認する。 |

## Context

本番 `/settings` の主体識別情報の補正には、名前を持たない古い `trainer-profile` の失敗通知が4件残っている。
対象データを後から補正しても、一覧は元の失敗taskに結び付いた `Open` のfailure notificationを表示するため、
通知が自動的には消えない。対象taskはすでに `Failed` の終端状態であり、task自体を無効化しても表示上の問題は
解消しない。

失敗taskや主体データを削除すると、障害原因と運用履歴を失い、同じ問題の追跡が困難になる。この変更では削除や
Resource全体の抑止を行わず、運用者が「この失敗通知への対応は不要」と判断した場合に、通知だけを
`Superseded` として閉じられるようにする。

## Goals

- `/settings` から主体識別失敗の候補を選び、対応不要として閉じられる。
- 元の失敗task、試行履歴、主体データを保持する。
- 古い通知を閉じても、同じResourceで将来新たに発生した失敗は通常どおり通知する。
- 誤操作、重複実行、表示中の状態変化に対して安全な操作にする。

## Non-goals

- collection task、attempt、request、主体データを物理削除しない。
- Resource全体を収集対象外にしない。
- 実行中taskの取消やpipelineの停止・再開は行わない。
- 名前欠落を自動補完して再収集する機能は、この変更へ含めない。
- `/jobs` の一般的な障害通知すべてに対応不要操作を追加しない。

## Experience and interaction design

### Primary use case

運用者は `/settings` の「主体識別情報の補正」で古い候補を選択し、一覧下部の副操作
「選択した候補を対応不要として閉じる」を押す。確認Dialogで、対象件数、対象の識別情報、閉じる理由、
履歴と主体データが削除されないこと、将来の新規失敗は再表示されることを確認し、実行する。

完了後は一覧を再読込し、閉じた通知が消えたことと、閉じた件数をsuccess messageで確認できる。

### Selection and actions

- 既存の選択checkboxを、再収集可能・要確認のどちらにも使用できる候補選択へ変更する。
- 「選択した補正を確認」は、選択対象がすべて補正可能な場合だけ有効にする。要確認を含む場合は、補正操作が
  できない理由を画面内に表示する。
- 「選択した候補を対応不要として閉じる」は、1件以上選択すると有効にする。
- 閉じる操作はPrimary Actionと競合させず、Neutralの副操作として配置する。確認Dialog内では操作名を
  「対応不要として閉じる」と明記し、単なる「OK」にしない。
- 通信中は二重実行を防ぎ、成功時は選択を解除して一覧を更新する。失敗時は選択を維持し、再読込または再試行の
  方法が分かるエラーを表示する。

### States and accessibility

- desktopのDataGridと狭幅のcard表示で同じ選択・閉じる操作を提供する。
- checkbox、Dialog、取消・実行buttonはkeyboardで操作でき、対象を識別できるaccessible nameを持つ。
- loading中、0件、API error、状態競合、成功を既存の `UiState` / alert patternで区別する。
- 色だけで「要確認」「再収集可能」または操作の危険性を表現しない。

## Mocks

- [Desktop / narrow text wireframe](mocks/settings-dismissal.md)

## Documentation updates

- `docs/26-collection-platform-design.md`: failure notificationを運用判断で閉じる際の不変条件を追記し、
  collection platformの正本として扱う。
- `docs/22-collector-design.md`: Collectorの収集・同定規則は変更しないため更新不要。
- 本change record: 画面操作、API境界、受け入れ基準、実装・本番確認の証拠を保持する。

## Technical impact

### State transition

- 対象は `SubjectNotIdentified` かつ主体種別が競走馬・騎手・調教師・馬主であるfailure notificationに限定する。
- `Open` の通知を `Superseded` にし、`ResolvedAt` を記録する。成功回復を意味する `Resolved` は使用しない。
- task、request、attempt、state、location、Resource、domain read modelは変更しない。
- 通知を閉じてもResourceを抑止しないため、将来の失敗は新しいnotificationとして作成される。

### API

- 管理APIに、選択したnotification IDを対応不要として閉じる操作を追加する。
- 空の要求、重複ID、主体識別失敗ではないIDを拒否する。
- 同じIDの再送は安全なno-opとし、閉じた件数と既に閉じていた件数を返す。
- 1件でも未知または許可対象外のIDを含む場合は、対象を一切更新しない。
- 認証済み管理画面からのみ利用できる既存の管理API境界を維持する。

### Persistence and concurrency

- notificationの検証と状態更新は同じstore transaction / 排他境界で実行する。
- 実行時に新しいRecoveryが開始済みの通知は閉じず、画面の再読込を求める競合として扱う。
- schema追加や既存データ移行は行わない。

## Decisions

1. **物理削除ではなく通知の対応不要化を採用する。** 障害調査に必要なtask/attempt履歴を保持するため。
2. **Resource抑止とは分離する。** 古い通知を閉じる判断は、対象そのものを将来収集しない判断ではないため。
3. **`Superseded` を使用する。** 正常復旧を示す `Resolved` と、運用者が不要と判断した終了を区別するため。
4. **一覧の既存選択を共用する。** 各行へ多数のbuttonを追加せず、4件など複数対象をまとめて処理できるようにする。
5. **確認Dialogを必須にする。** 一覧から通知が消える操作であり、取り消しUIを今回提供しないため。

### Rejected alternatives

- **失敗taskを削除する:** 履歴と原因を失い、参照整合性への影響も大きいため不採用。
- **Resourceを抑止する:** 将来の正当な収集まで止めるため不採用。
- **失敗taskをCancelledへ書き換える:** すでにFailedとして終了した事実を改変するため不採用。
- **確認なしの行単位button:** 誤操作しやすく、複数件の処理効率も低いため不採用。

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | 再収集可能・要確認のどちらの主体識別候補もcheckboxで選択でき、1件以上の選択で「選択した候補を対応不要として閉じる」が利用できる。 | T2,T3 | component test、desktop/narrow browser確認 | Connected |
| AC2 | 閉じる前に確認Dialogが開き、対象件数、履歴と主体データを削除しないこと、将来の新規失敗は再表示されることが示され、取消では状態が変わらない。 | T2,T3 | component test、keyboard browser確認 | Connected |
| AC3 | 実行すると選択したopen notificationだけが一覧から消え、成功件数が表示される一方、元のFailed taskとattempt履歴はジョブ詳細から確認できる。 | T1-T3 | store/API integration test、browser確認 | Connected |
| AC4 | 通知を閉じてもResource、収集state、domain dataは変更されず、同じResourceの将来の新規失敗は新しい要対応として表示される。 | T1,T3 | store integration test | Verified |
| AC5 | 同一要求の再送は重複変更せず、未知・対象外IDを含む要求およびRecovery開始との競合は一部更新を起こさず、画面に再読込可能なエラーを表示する。 | T1-T3 | store/API/component tests | Verified |
| AC6 | 要確認を含む選択では再収集操作を誤実行できず、閉じる操作との違いと次の行動が表示される。 | T2,T3 | component test、browser確認 | Connected |
| AC7 | desktopのDataGridと狭幅cardで主要情報と操作が保持され、keyboard操作、focus、accessible name、loading/empty/error状態に回帰がない。 | T2,T3 | component test、desktop/narrow browser確認 | Connected |
| AC8 | 認証境界、既存の安全な補正・名寄せ・再収集、一般のfailure notification処理に回帰がない。 | T1-T4 | focused/full tests、diff review | Verified |
| AC9 | 配備後、本番の古い調教師4通知を対応不要として閉じると `/settings` から消え、対象データと履歴が保持され、pipelineが稼働を継続する。 | T4 | production before/after確認 | Not started |

## Delivery plan

1. notification限定の冪等な対応不要化store/APIを実装する。
2. `/settings` の選択、確認Dialog、結果表示を実装する。
3. store/API/component testとdesktop/narrow browser検証を行う。
4. セルフレビュー、CI、デプロイ後に本番4件を閉じ、履歴保持とpipeline継続を確認する。

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | notificationの検証・状態遷移、管理API、契約、clientを実装する。 | Main | Lead tier | Approval | CollectionOperations、API endpoint/contracts/client | store/API integration tests | AC3-AC5,AC8の結果 | Verified |
| T2 | 設定画面の選択、確認Dialog、結果表示、responsive表示を実装する。 | Main | Lead tier | T1 | `Settings.razor`と必要最小限のstyle | component tests | AC1-AC3,AC5-AC8の結果 | Verified |
| T3 | 回帰テストと実ブラウザー検証を行い、設計適合をセルフレビューする。 | Main | Lead/review tier | T1,T2 | tests、change record | focused/full/browser gates | AC1-AC8の証拠 | In progress |
| T4 | CI、デプロイ、本番4件のoperationと事後確認を行う。 | Main | Lead/review tier | T3 | deployment、production operation、docs | CI/deploy/production evidence | Dependent |

## Review gates

- **Design and task-split review — 2026-09-18, reviewer: Main.** 対象taskがterminal Failedであること、画面がopen failure notificationを列挙すること、既存の`Superseded`/`Resolved`状態、Resource抑止の影響範囲を確認した。削除・task状態改変・Resource抑止を退け、notificationだけを原子的に`Superseded`へ遷移させる設計とした。AC1-AC9をstore、API、UI、回帰、本番operationへ追跡した。データ完全性と本番操作を含むためMainが直列で担当し、サブエージェントは使用しない。
- **Pre-implementation review — 2026-09-18, reviewer: Main.** ユーザー承認を確認した。T1をRunnable、T2-T4を依存順にDependentとした。通知検証と状態遷移を単一transactionへ閉じ、対象外ID・Recovery開始済み通知で部分更新しないことをAPI接続前にstore testで確認する。T1完了後にのみUIへ接続し、実ブラウザー操作はcomponent/API回帰後に行う。
- **Checkpoint review — 2026-09-18, reviewer: Main.** store/API/UIの接続と全diffを確認した。物理削除、task状態改変、Resource抑止、schema変更はなく、`Open` notificationだけを`Superseded`へ更新する。重複実行、将来の新規failure、Recovery競合、対象外IDの全件拒否をintegration testで確認した。Blocked選択時は再収集を無効化し、対応不要化だけを説明付きで提示する。focused 17件、Release build、全test 1081件（1 skip）が成功した。実ブラウザーと本番operationを残す。
- **Final review:** 全taskとACの検証後に記録する。

## Verification record

- 2026-09-18: 本番 `/settings` で古い調教師4件が同じtask IDの `Blocked` notificationとして残っていることを確認した。
- 2026-09-18: 対象taskはすでにFailed終端で、一覧の取得元がopen failure notificationであることを確認した。
- 2026-09-18: collection platformには成功回復用`Resolved`、新失敗による置換用`Superseded`があり、task/attemptを削除せず通知だけを一覧対象外にできることを確認した。
- 2026-09-18: 現行画面ではBlocked候補のcheckboxがdisabledで、運用者が通知を閉じる導線がないことを確認した。
- 2026-09-18: storeの履歴保持・冪等性・将来failure・Recovery競合2件、API/UI重点15件が成功した。
- 2026-09-18: `dotnet format HorseRacingPrediction.sln --no-restore --verify-no-changes`、Release build、`TestCategory!=External`の全test 1081件（1 skip）が成功した。

## Deviations and follow-up

- なし。実装中にtask削除、Resource抑止、schema変更が必要になった場合は設計変更として再承認を得る。

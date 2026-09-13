# JRA競走馬識別子の不具合修復を管理画面から実行する

- Status: Approved
- Owner: HorseRacingPrediction maintainers
- Created: 2026-09-14
- Updated: 2026-09-14

## Context

`20260913-jra-horse-identity-repair` は、今回のJRA競走馬識別子欠落不具合で生じた重複だけを対象に、dry-runと安全性再検証を経て補正する専用APIとして実装済みである。しかし、現在はAPIを直接呼ぶ必要があり、管理者が候補の馬名、根拠レース、安全性判定を画面で確認して実行できない。

本変更は、既存repairの対象条件や名寄せ規則を広げず、管理画面から安全に操作できる導線を追加する。

## Goals

- 管理者が「その他設定」ページで未処理の補正候補と根拠を確認できる。
- 安全と判定された候補だけを選択し、最終確認後にrepair applyを実行できる。
- blocked、空、読込失敗、適用競合、成功、再実行skipの状態と次の行動を画面で理解できる。
- 画面操作でも既存repair APIのapply時再検証、manifest外拒否、ledger、redirect、冪等性を迂回しない。
- 補正処理をデータ収集ジョブへ混在させず、適用後は削除済み統合元Horseの収集ジョブを無効化する。

## Non-goals

- 任意のHorse IDを入力して統合する汎用名寄せ画面。
- 名前一致だけを根拠に候補を追加・統合する機能。
- repair候補の編集、削除、安全性判定の上書き。
- JRA以外のProviderや別種のデータ補正を今回の画面から実行すること。
- 適用済みrepairの取消やredirect削除。

## Experience and interaction design

### 主な利用者と完了状態

管理画面へログインできる運用者が `/settings` を開き、今回の不具合修復候補を確認する。完了状態は、選択した安全候補が補正済みとなり、成功メッセージと最新の未処理候補一覧が表示された状態である。

### ページ構造

Settings Page patternの独立ページとし、`RaceOpsPageHeader` に「運用 / その他設定」、説明、再読込Actionを置く。本文内の設定区分「データ補正」に今回の専用repairを置く。本文は次の順とする。

1. repair名、repair ID、対象が今回の不具合に限定される説明
2. 未処理候補の件数と、安全／要確認の件数
3. 候補一覧
4. 選択件数とPrimary Action `選択した候補を補正`

候補行には選択、状態、統合元Horse、統合先Horse、JRA identity、根拠レース、判定理由を表示する。Horseとレースは既存詳細画面へのリンクにする。内部IDは判断の補助情報として名称の下へ表示し、名称より強調しない。blocked候補のcheckboxは無効とし、理由と「対象RaceCardを再取得する」等のサーバー判定文を表示する。

安全候補が複数ある場合は `安全な候補をすべて選択` を提供する。手入力でCandidate IDを追加する操作は提供しない。候補0件では「現在、補正できる候補はありません」とし、RaceCard再取得後に `再読込` できることを案内する。

### 確認と実行

Primary ActionでFluent Dialogを開き、選択件数、統合元／統合先、redirectが残ること、取り消し操作がないことを表示する。主操作は `補正を実行`、副操作は `戻って確認` とする。実行中は二重送信を防ぐ。

成功時は適用件数、既処理skip件数、無効化した収集タスク件数をMessageBarで示し、候補を再読込する。apply直前の再検証で競合・blockedになった場合は、変更されなかったこと、候補を再読込して理由を確認することを操作領域付近に表示する。技術的な例外本文はそのまま表示しない。

### Responsive and accessibility

デスクトップではFluentDataGridで走査可能な列を保つ。狭幅では候補を縦積みの行へreflowし、状態、馬名、根拠、判定理由、checkboxを失わない。長いJRA identityとIDは折り返す。checkboxと操作には可視labelを付け、Dialogは見出しとフォーカス管理をFluent標準へ委ねる。状態は色だけでなく文言でも表示する。

### Pre-implementation self-review

- 採用: repair固有の独立Page。根拠確認と複数選択、確認、結果表示が必要で、短時間の単純操作だけを扱うDialogには収まらない。
- 不採用: `/jobs` への常設操作追加。収集ジョブとデータ名寄せは対象・履歴・失敗時の意味が異なり、主要な収集操作を埋もれさせる。
- 不採用: Horse詳細から任意Horseを統合する導線。今回限定というrepairの安全境界を壊す。
- 不採用: Candidate IDの直接入力。manifest外拒否はAPIにもあるが、UIで誤操作を誘発する必要がない。

## Navigation and relationships

- 左ナビゲーションの「運用」に `その他設定` を追加し、`/settings` へ遷移する。
- 候補の統合元／統合先は `/horses/{horseId}`、根拠レースは `/races/{raceId}` へリンクする。
- 画面はCookie認証、自己ループバックAPIは既存のAPI key付与をそのまま利用する。

## Mocks

- [デスクトップ／狭幅テキストワイヤーフレーム](mocks/data-corrections-wireframe.md)

## Documentation updates

- `docs/20-admin-ui-design.md`: `/settings` の位置づけ、候補表示、確認、成功・失敗状態を追記する。管理画面操作の正本である。
- `docs/changes/20260913_jra-horse-identity-and-detail-urls/README.md`: 実装済みrepairの操作画面は本change recordで追加する旨をfollow-upとしてリンクする。

## Technical impact

- `AdminApiClient` にrepair preview/apply呼出しを追加する。UIはAPIを迂回してDbContextを直接操作しない。
- repair applyは同期の専用管理APIとして実行し、CollectionRequest、CollectionTask、collection queueを作らない。
- preview responseへ表示用のsource/target Horse名、根拠レース名、適用時に更新する参照件数を追加する。安全性判定の正本は引き続きサーバー側とする。
- `Web/Components/Pages` に `/settings` ページを追加し、既存のFluent UI、`RaceOpsPageHeader`、`RaceOpsStatusBadge`、`RaceOpsAlert`、`UiState`を再利用する。
- `NavMenu` とAPI key middlewareの管理UIルート許可リストに `settings` を追加する。認証・認可モデルは変更しない。
- Collection Platformに統合元Horse Resourceのsuppression記録を追加する。apply後、同ResourceのPending/Ready/RetryWaiting/WaitingDiscoveryはCancelledへ移し、Runningはcancel要求を記録し、配送世代を進めて未取得queue messageを無効化する。Succeeded/Failed/Cancelled/DeadLetterとattempt/request履歴は削除しない。
- suppression後は同じ `Horse/JRA/{sourceHorseId}` への新規収集要求からtaskを作らず、「補正済みのため収集対象外」と応答する。canonical Horseへの収集は妨げない。
- Event Storeのrepair ledgerとCollection Platform DBは別transactionである。apply再実行時はrepair適用済み候補についてもsuppressionを再確認・不足分を完了させ、両方が完了するまで成功を返さない。これにより途中失敗から再開できる。
- bUnitでloading、empty、safe/blocked、選択、確認、成功、API競合を検証する。repair endpointの既存統合テストも維持する。

## Decisions

1. UIは既存のversioned repairだけを操作し、汎用的なデータ名寄せコマンドを公開しない。
2. 候補選択はサーバーpreview由来に限定し、blocked候補は選択不可とする。apply時にもサーバーが再検証する。
3. 一括全適用ボタンではなく、候補選択と確認Dialogを必須にする。
4. repair IDは画面へ表示するが入力させない。画面とAPI routeは固定versionへ接続する。
5. 適用済み候補は未処理一覧から消え、成功MessageBarの件数で直前の結果を確認する。永続監査は既存ledger/redirectを正本とする。
6. repairは収集ジョブとして表現しない。統合元Horseの収集停止だけをCollection Platformへ連携し、repairの実行履歴と収集履歴を混在させない。
7. 統合元Horseの収集履歴は削除せず、suppression後の新規task作成と既存非終端taskの継続だけを禁止する。

## Acceptance criteria

| ID | Observable criterion | State |
|---|---|---|
| AC1 | 左ナビゲーションの「運用」から「データ補正」を開け、直接URL再訪でもCookie認証後に表示できる。 | Not started |
| AC2 | preview読込中、候補0件、通信失敗をそれぞれ区別し、空・失敗時に再読込方法を表示する。 | Not started |
| AC3 | 各候補でsource/targetの馬名とID、JRA identity、根拠レース、safe/blocked状態、blocked理由を確認し、Horse/レース詳細へ遷移できる。 | Not started |
| AC4 | safe候補だけを個別または一括選択でき、blocked候補や候補0件では補正実行を開始できない。Candidate IDを手入力できない。 | Not started |
| AC5 | 実行前Dialogに選択対象、redirect保持、取消不可を表示し、戻る場合は変更せず、実行中の二重送信を防ぐ。 | Not started |
| AC6 | 実行すると選択したCandidate IDだけを既存apply APIへ送り、成功時にapplied/skipped件数を表示して最新previewへ更新する。 | Not started |
| AC7 | apply時再検証の競合・blocked・通信失敗では、変更成否と次の行動を操作領域に表示し、選択外候補を変更しない。 | Not started |
| AC8 | ページはkeyboard操作可能で、状態を文言で表し、狭幅でも対象・根拠・理由・主操作を失わず横overflowを生じない。 | Not started |
| AC9 | repair applyはCollectionRequest/Taskを作らず、通常のデータ収集ジョブ一覧に補正処理を表示しない。 | Not started |
| AC10 | apply後、統合元Horseの非終端収集タスクは取消または取消要求済みとなり、キュー済み配送は取得されず、履歴は保持される。 | Not started |
| AC11 | suppression後の統合元Horseへ新規収集を要求してもtaskは作られず、canonical Horseへの収集は通常どおり作成される。 | Not started |
| AC12 | repair保存後・suppression前に失敗して再実行しても不足した無効化を完了し、redirect、ledger、suppression、監査を重複させない。 | Not started |
| AC13 | bUnit、repair API統合テスト、Collection Platformの取消・配送・再要求テスト、API全体テスト、実ブラウザーの通常・空・失敗・狭幅確認が成功する。 | Not started |

## Delivery plan

1. Collection Platformへresource suppressionと既存タスク無効化を追加し、新規要求、queue配送、実行中cancel、履歴保持、冪等性をテストする。
2. repair applyをsuppressionへ接続し、DB間partial failure/restartを含むendpointテストを追加する。
3. preview contractを表示情報付きへ拡張し、endpoint/API clientのテストを追加する。
4. `/settings` ページ、ナビゲーション、ルート認証境界を実装する。
5. bUnitで状態とinteractionを検証する。
6. ローカル管理画面を実ブラウザーで通常・空・失敗・狭幅確認し、最大の問題を修正して再確認する。
7. 全回帰テストと文書同期を行い、change recordをImplementedへ更新する。

## Verification record

- 2026-09-14: 既存のversioned repair API、管理UIのNavMenu、AdminApiClient、Cookie/API key認証境界、`/jobs` のPage patternとbUnitテストを確認した。
- 2026-09-14: 実装前セルフレビューを行い、収集管理へ混在させず、repair固有ページからサーバーpreviewに列挙された候補だけを操作する案とした。
- 2026-09-14: ユーザー指定により画面名を「その他設定」とし、補正実行はデータ収集ジョブと別系統にした。名寄せ後は削除済み統合元Horseの既存・将来の収集をsuppressionし、履歴は保持する設計へ拡張した。
- 2026-09-14: ユーザーが更新後の設計を明示的に承認したため、StatusをApprovedとしてExecution Modeへ移行した。

## Deviations and follow-up

- 本番データへのrepair適用は、実装・ローカル検証とは分離する。

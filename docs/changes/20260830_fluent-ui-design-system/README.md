# Fluent UI ベースの管理画面刷新とデザインガイドライン統合

- Status: Approved
- Owner: HorseRacingPrediction team
- Created: 2026-08-30
- Updated: 2026-08-30

## Context

管理サイト `HorseRacingPrediction.Api` と、廃止対象となる `HorseRacingPrediction.Collector` の旧運用・デバッグ画面は、合計 594 行の手書き CSS とページ固有の HTML パターンで見た目を構成している。API 管理画面は余白、密度、階層、操作の強弱が画面ごとに揃わず、全体として重く見える。Collector 側の重複画面は刷新せず削除する。

ジョブ一覧・詳細は以前 API 管理画面の `/collection-tasks`、`/collection-tasks/{jobId}` として存在したが、2026-08-30 の `3a073f9 Remove collection task management UI` でナビゲーションと 2 画面が削除された。API の照会・操作エンドポイントと Collector の `/jobs`、`/jobs/{jobId}` は残っているため、現在は正本を API が持ちながら、通常の管理サイトからジョブを発見・調査できず、別ホストの開発用 Collector UI に依存する状態になっている。

既存の [管理サイト UI / UX 再設計](../../admin-ui-design.md) は情報設計から実装計画までを広く扱い、第 4 章で OOUI モデル、第 5.1–5.6 節で `/jobs`・`/jobs/{jobId}` と対象日別の運用導線を定義している。今回のジョブ復元方針はこれらを「収集ジョブ」と「データ取得状況」へ整理する。一方、同文書の第 10 章にある暫定デザインシステム `RaceOps UI` と第 11.3 節は Fluent UI Blazor を将来比較する候補に留めており、現状の重複 CSS を解消する共通実装はない。今回の変更では、その後継として Fluent UI Blazor を採用し、デザインシステムの正本を独立したガイドラインへ集約する。

調査時点（2026-08-30）の安定版 `Microsoft.FluentUI.AspNetCore.Components` は `4.14.4` で .NET 8 を対象としている。v5 は RC のため採用しない。ライブラリは ASP.NET Core の公式構成要素ではなくベストエフォート保守であるため、ページから直接無制限に利用せず、アプリ固有パターンを共通コンポーネントへ閉じ込める。

## Goals

- Fluent 2 の視覚言語と Fluent UI Blazor の基礎コンポーネントを導入し、軽快で一貫した管理画面へ刷新する。
- API 管理画面の全ページが同じトークン、App shell、状態表現、フォーム、フィードバックを使う共通 UI 基盤を設ける。
- OOUI を画面設計と共通コンポーネントの基礎に据え、利用者がレース、馬、騎手、ジョブなどの「もの」とその関係を一貫して認識し、辿れるようにする。
- ジョブ一覧・詳細を API 管理画面の運用オブジェクトとして復元し、異常の発見、原因確認、関連オブジェクトへの移動、安全な操作を通常の管理導線で完結させる。
- 既存の画面機能、URL、情報階層を維持したまま、全画面の見た目と操作感を段階的ではなく一貫して移行する。
- 本システムのデザインガイドラインを `docs/design-guidelines.md` に一本化し、設計者と実装者の正本にする。
- 320 CSS px からデスクトップまで利用でき、キーボード、スクリーンリーダー、拡大表示で操作可能にする。

## Non-goals

- 認証方式の変更、および本変更の画面・操作を実現するために必要な範囲を超えたジョブ実行仕様の変更。
- ダークモードや利用者によるテーマ色の変更。初回はライトテーマのみとする。
- Fluent UI React や Fluent UI Blazor v5 RC の採用。
- ジョブ一覧・詳細の復元に必要な view、filter、関係導線を除き、他オブジェクトのページング方式や情報アーキテクチャなど、既存 change record で未実装の機能を同時に追加すること。
- Collector の収集実行処理自体を API プロセスへ統合すること。API は計画、ジョブ・取得状態の永続化、キュー投入、管理 API / UI を所有し、Collector は計画済みジョブの取得・実行・結果報告を担当する。

## Implementation scope clarification

承認されたモックと受け入れ基準を実現するために必要な API、ReadModel、ドメインモデル、永続化、URL の追加・変更は本変更の対象に含める。具体的には、収集ジョブの管理用照会・操作、キュー全体の一時停止・再開、データ取得状況の照会・再取得、人馬関係と賞金ランキング、馬主の名寄せ・編集、馬・騎手・調教師・馬主・レースの収集済みデータを訂正して保存する編集契約を含む。編集可能項目は現行モデル調査後に、識別子・時点履歴・関連参照の整合性を壊さない項目として本変更セットへ列挙する。既存 API と旧 URL は、変更セットで明示した互換方針に従って維持またはリダイレクトする。

## Experience and interaction design

### OOUI principles

オブジェクト、関係、属性、操作の定義は、既存の [管理サイト UI / UX 再設計「4. OOUI モデル」](../../admin-ui-design.md#4-ooui-モデル) を正本とする。この変更ではモデルを再定義せず、Fluent UI による視覚・実装規則へ次のように接続する。

1. **Objects first**: 画面名や処理手順より先に、レース、予想票、馬、騎手、調教師、馬主、出走、収集ジョブ、データ取得状況を主語にする。主ナビゲーションとページ見出しは原則としてオブジェクト名またはオブジェクトのコレクション名にする。
2. **Stable identity**: 同じオブジェクトは、一覧、検索結果、関連項目、詳細のどこでも同じ主ラベル、種類アイコン、状態表現を使う。内部 ID は識別に必要な技術情報へ置き、利用者向け主ラベルにしない。
3. **Canonical representation**: 各オブジェクトは固有 URL の詳細表現を持つ。一覧、Toast、エラー、関連項目から同じ詳細へ移動でき、戻る操作だけに依存しない。
4. **Relationships are navigation**: レースと馬、出走と騎手、収集ジョブと対象レースなどの関係は単なる文字列でなく `RaceOpsEntityLink` として表示する。対象日は同日の一覧へ絞り込む文脈リンクとし、関係の種類も「騎手」「対象レース」「親ジョブ」のように明示する。
5. **Attributes support recognition**: 一覧では識別、状態、判断に必要な主要属性 2–3 件、主要な関係だけを示す。全属性は詳細へ段階的に開示し、カードへ詰め込まない。
6. **Actions belong to objects**: 編集、リラン、再取得、収集開始などの CTA は、対象オブジェクトの状態に応じて詳細またはコレクションヘッダーに置く。処理名だけの独立メニューを増やさない。
7. **Consistent condensed grammar**: 一覧項目は `種類 / 主ラベル → 状態 → 主要属性 → 関係 → 詳細 affordance` の順を共通文法とする。desktop の DataGrid と mobile の list item は見た目が変わっても同じ情報優先度を保つ。
8. **State is part of the object**: loading / empty / error はページ装飾ではなく、コレクションまたはオブジェクト表現の状態として扱う。変更成功後は Toast だけで終えず、対象オブジェクトの属性と履歴へ反映する。

タスク指向のダッシュボードや確認ダイアログは補助表現として許可するが、そこでオブジェクトの識別と詳細への導線を失わない。デバッグツールのように独立した作業自体が対象となる画面は例外とし、入力、実行結果、関連オブジェクトを明確に分離する。

### Job collection `/jobs`

API 管理画面にジョブオブジェクトの canonical collection を復元する。利用者向け名称は、対象範囲が分かる「収集ジョブ」とする。

- 主ナビゲーションの「運用」に `収集ジョブ` と `データ取得状況` を置く。ジョブ種別、対象日、リランはナビゲーション項目にしない。
- 既定 view は `要対応` とし、`Failed`、`DeadLetter`、長時間 `Running`、解消が必要な `WaitingDependency` を優先する。
- 保存 view は `要対応`、`処理中`、`待機中`、`最近完了`、`すべて`。各 view は状態の機械名ではなく運用上の意味を持つ。
- filter はキーワード、処理種別、状態、対象日、更新期間。filter / view / page は query string に保持し、詳細から戻ったとき復元する。
- 一覧 item は `処理名 + 対象` を主ラベルとし、日本語状態、最終更新、試行回数、エラー要約、対象日・レースとの関係を表示する。`JobType` と `DeduplicationKey` は補助・技術情報へ下げる。
- item 全体または明示的な affordance から `/jobs/{jobId}` へ移動する。一覧上にリラン・取消を並べない。
- desktop は比較可能な DataGrid または list-detail、mobile は同じ情報優先度を持つ 1 job 1 item の縦リストとする。
- loading、0件、取得失敗を区別する。0件時は現在の view / filter を説明し、filter 解除を提示する。
- 新規の日次収集は収集ジョブ一覧の補助操作として対象日を指定する。完了後は対象日で絞り込んだ同じ一覧へ戻し、独立した `/collection-days` は設けない。

### Job detail `/jobs/{jobId}`

収集ジョブの canonical representation とし、通知、親子ジョブ、対象日で絞り込んだ一覧、レースからこの URL へ集約する。

1. Object header: 人が読める処理名と対象、日本語状態、最終更新。Job ID は主見出しにしない。
2. Attention summary: 失敗・デッドレター・長時間実行・依存待ちの場合に、何が起き、何に影響し、次に何ができるかを示す。
3. Primary attributes: 試行回数、初回投入、実行可能時刻、開始、最終更新。
4. Context and relationships: 対象日は同日の収集ジョブ一覧へ絞り込むリンクとし、対象レース、親ジョブ、子ジョブはオブジェクトリンクにする。
5. Timeline: 投入、各試行の開始・完了、手動リランと、実行元・理由・状態変化を時系列で統合する。
6. Technical details: Job ID、JobType、DeduplicationKey、priority、lease、payload、完全な error を disclosure 内に置く。
7. Object-scoped CTA: 最新のジョブ実行が再試行上限に達して失敗した場合だけ `リラン`。完了済みの取得対象には `再取得` を使う。個別ジョブの取消は通常UIに置かず、全体制御はジョブ一覧の一時停止・再開へ集約する。

リラン受付時は Toast だけで完了せず、object header、最新試行、累計試行回数を再読み込みする。リランは過去の試行を選ぶ操作ではなくジョブ本体に対する操作とし、手動操作1回につき同じ `jobId` に新しい試行を1件追加する。その試行も失敗した場合は、ジョブが成功して完了するまで再びリランできる。完了後はリランを表示せず、新たな取得が必要ならデータ取得状況の `再取得` から別ジョブを作成する。主画面の失敗内容は最新試行分だけを表示し、過去の全試行と各失敗内容は件数制限なしの折りたたみで確認できる。表示量が実運用上の問題になった場合は、折りたたみ内のページングではなく試行履歴専用ページを別変更で設計する。存在しない job は通常の empty state ではなく not-found state とし、一覧へ戻る導線と検索対象の Job ID を示す。

### Queue pause / resume on `/jobs`

ジョブ一覧の page header に、個別ジョブではなくキュー全体を対象とする `すべてのジョブを一時停止` を置く。停止は次の意味を持つ。

- confirmation で「SQS 本体と DLQ を purge する」「起動済み（Running）のジョブは完了まで進む」「DB上の Ready / Pending は削除せず保持する」「新しい通知の配送を止める」を明示する。
- 実行開始時に pause state を永続化し、outbox dispatcher と scheduler の送信を停止してから SQS 本体・DLQ を purge する。Purge の完了待ちと進行状態を画面に表示する。
- Running の Worker は強制終了しない。処理完了後の状態更新は受理する。Ready / Pending のジョブは実行せず、履歴・priority・AvailableAt を保持する。
- 停止中は一覧の primary action を `再開` に変え、二重実行を防ぐ。失敗時は purge の工程と再試行可能性を明示する。

`再開` は pause state を解除し、DB上の `Ready` のみを priority 降順、同順位は `AvailableAt` 昇順、`CreatedAt` 昇順、`JobId` 昇順で outbox / SQS に再投入する。再開後も `Pending` は通常のスケジュール条件を満たすまで待機する。再投入件数、除外された状態、最終実行時刻を完了メッセージと監査履歴に残す。

これは個別ジョブの `Cancelled` 化ではない。個別取消 API が既存システムに残る場合も、今回の管理UIでは公開せず、全体一時停止と再開を標準の運用操作とする。

### Job screen ownership and duplication

- API 管理画面の `/jobs`、`/jobs/{jobId}`、`/acquisition-statuses` を利用者向け canonical UI とする。
- Collector の既存 `/jobs`、`/jobs/{jobId}`、`/result-days`、`/acquisition-statuses` を含む全 Razor 画面、Web Host、HTTP endpoint、静的資産、通常運用ナビゲーションを削除する。Collector の画面 URL と HTTP API は維持しない。
- API は `ProcessingStateStore`、収集計画、Outbox / SQS 配送、収集ジョブ・日別状況・データ取得状況の状態、管理操作と監査を所有する。Collector は API の状態ストアを HTTP 経由で利用し、計画済みジョブの取得・実行・完了または失敗の報告だけを行う。通常運用の画面、停止・再開、リラン、再取得、監査の canonical 操作は API 管理画面へ集約する。
- Collector の `UseApiStateStore=false`、`ProcessingStateStore`、ローカル状態DBを削除する。実行サービスに必要な HTTP 状態ストアクライアントとジョブ実行処理だけを残し、開発・テストは API またはテストダブルを利用する。
- 旧 `/collection-tasks` と `/collection-tasks/{jobId}` は `/jobs` と対応する詳細へ redirect し、古い bookmark の 404 を避ける。通知リンクも API JSON endpoint ではなく認証済み管理 UI の `/jobs/{jobId}` を指す。

### Visual direction

「競馬らしさ」を装飾や濃いグラデーションで表現せず、芝を想起する落ち着いた緑をアクセントに限定する。背景は Fluent の neutral layer、本文は明確な文字階層、コンテナは控えめな境界と影で構成する。常時表示の大きなカード、丸すぎるボタン、絵文字アイコンを減らし、余白とタイポグラフィで区切る。

- Accent: `#0F6B52` を基準に Fluent の派生色を生成する。
- Iconography: 絵文字は使用しない。状態は文字ラベルと色で成立させ、必要な場合だけ Fluent System Icons を補助的に使う。
- Neutral: Fluent 既定の neutral palette を使い、本文・境界・背景を独自 hex 値で増やさない。
- Typography: OS の UI font stack と Fluent type ramp を使う。見出しは大きさだけでなく weight と余白で階層化する。
- Shape: コントロールは Fluent 既定、面コンテナは 8 px を標準とする。pill は status / tag に限定する。
- Elevation: ナビゲーション、dialog、popover など重なりを示す場合に限定し、通常カードへ強い影を付けない。
- Motion: 120–200 ms の状態変化だけに使用し、`prefers-reduced-motion` を尊重する。

### Layout and density

- Desktop: 240 px の左ナビゲーション、最大 1440 px の本文、24 px のページ余白。
- Tablet: 左ナビゲーションを折りたたみ可能にし、本文余白を 20 px にする。
- Mobile (`<= 720px`): 48 px の top app bar と drawer、本文余白 12 px。表形式は判断に必要な列だけ残すか縦リストへ変換する。
- コンテンツ間隔は 4 px grid（4 / 8 / 12 / 16 / 24 / 32）だけを使う。
- 通常の入力・ボタンは 32–40 px の視覚密度とし、タップ領域は 44 x 44 CSS px を確保する。

### Component policy

Fluent UI Blazor の部品を次の優先順位で使用する。

1. Fluent 標準をそのまま使う: Button、TextField、Select、Checkbox、DatePicker、DataGrid、Dialog、Toast、MessageBar、ProgressRing、Tooltip、Menu。
2. 本システム共通の意味を付ける薄いラッパー: `RaceOpsPageHeader`、`RaceOpsObjectHeader`、`RaceOpsObjectItem`、`RaceOpsStatusBadge`、`RaceOpsRelationshipList`、`RaceOpsEmptyState`、`RaceOpsEntityLink`、`RaceOpsTechnicalDetails`、`RaceOpsAppShell`。
3. ページ固有 CSS: レース出走行など、ドメイン固有の情報配置だけ。色、文字、ボタン、入力の再定義は禁止する。

Fluent component へ既存クラス名を機械的に置換するのではなく、見出し、ツールバー、本文、補助情報、操作の順序を共通パターンへ揃える。DataGrid は比較が主目的のデスクトップ一覧に使い、モバイルでは横スクロールを既定解にしない。

### States and feedback

- Loading: ページ全体は `ProgressRing` と説明、局所更新は対象コントロールの busy state を示す。
- Empty: 原因、次にできる行動、フィルター解除を一つの empty-state pattern で示す。
- Error: 復旧可能な説明と再試行を `MessageBar` で示し、例外詳細は折りたたむ。
- Success: 永続的な結果は本文へ反映し、補助的な完了通知だけ Toast を使う。
- Destructive / risky action: Danger appearance、対象を明示する Dialog、二重送信防止を組み合わせる。
- Status: 色だけに依存せず、アイコンと日本語ラベルを併記する。

### Accessibility

- WCAG 2.2 AA を基準とし、本文・アイコン・状態色のコントラストを確認する。
- DOM 順と視覚順を一致させ、見出しレベル、landmark、label、table header を保持する。
- すべての操作をキーボードで完了でき、focus-visible を消さない。
- 色、位置、アイコンだけで状態や必須を伝えない。
- 200% 拡大および 320 CSS px で主要操作へ到達できる。
- animation は reduced motion、OS high contrast / forced colors を阻害しない。

## Navigation and relationships

API 管理画面の既存 URL とナビゲーショングループは、変更セットで明示した改称・統合・redirect を除いて維持する。主ナビゲーションは「レース・予想」「データ」「運用」のオブジェクト集合で構成し、作成・編集・リラン・再取得などの動詞は対象オブジェクト内の CTA とする。`RaceOpsAppShell` は API 管理画面だけが利用する。

一覧はオブジェクトのコレクション、詳細は canonical representation、編集は同じオブジェクトの編集状態として設計する。breadcrumb と関連リンクは階層だけでなく関係を表し、例えばレース詳細から馬・騎手・調教師へ、ジョブ詳細から対象日・対象レース・親子ジョブへ直接移動できる状態を維持する。

```text
HorseRacingPrediction.Api/Web
├─ Theme / tokens
├─ App shell / navigation primitives
├─ Form and feedback patterns
├─ Domain-neutral RaceOps components
└─ Pages
```

## Mocks

- [代表画面ワイヤーフレーム](mocks/representative-screens.md): desktop / mobile の shell、一覧、詳細、状態表示
- [ジョブ一覧・詳細](mocks/jobs.md): 要対応 view、ジョブ詳細、関係、technical disclosure、mobile state
- [HTMLインタラクティブモック](mocks/jobs.html): ブラウザで確認できる desktop / mobile の一覧、詳細、最新失敗実行のリラン dialog
- [メニュー別HTMLモック一覧](mocks/index.html): 各メニューを独立ページとして確認できる入口
- [共通モックCSS](mocks/assets/raceops-mock.css): 全ページで共有するFluent風トークン、レール、行リスト、レスポンシブ規則
- 各メニューの独立モック: `races.html`、`predictions.html`、`horses.html`、`jockeys.html`、`trainers.html`、`owners.html`、`jobs.html`、`acquisition-statuses.html`

モックは階層、密度、操作の強弱を確認する仕様であり、Fluent component の最終ピクセル値を複製するものではない。

## Documentation updates

- `.codex/skills/document-driven-development/SKILL.md`: 変更セット作成時に影響する正本ドキュメントを同じ変更セットで更新し、変更記録へ明記する必須手順を追加した。
- `.codex/skills/document-driven-development/references/change-record-format.md`: `Documentation updates` セクションと、記載すべき内容を追加した。
- `docs/design-guidelines.md`: Fluent UI を visual foundation、OOUI を composition model とする本システム唯一のデザインガイドラインを新設した。トークン、component policy、state、responsive、accessibility、writing、review checklist をここへ集約した。
- `docs/admin-ui-design.md`: 第 10 章を新ガイドラインへの参照に置換し、重複していたデザインシステム規則を削除した。第 11.3 節を Fluent UI Blazor 採用後の方針へ更新した。第 4 章の OOUI モデルと第 5 章のジョブ情報設計は正本として維持した。
- `docs/admin-ui-design.md`（レース画面更新）: レース一覧を日付・開催場グループからページング可能な行一覧へ変更し、URL queryによる条件保持、日付・開催場リンク、レース詳細の上部基本情報、単勝・複勝オッズ、全出走関係者リンクを正本へ反映した。
- `docs/changes/20260830_fluent-ui-design-system/mocks/jobs.html`: OOUIのジョブ一覧・詳細、関係リンク、状態、同一ジョブへ試行を追加するリラン確認、最新試行の概要と全試行へのドリルダウンを実際のブラウザで確認できる自己完結モックとして追加した。
- `docs/changes/20260830_fluent-ui-design-system/mocks/jobs.html`（更新）: 詳細画面の「その他」を削除し、ジョブ階層に親・現在・子の詳細属性を追加した。ジョブ一覧には全体一時停止・再開を追加し、ガイドラインの24 pxページ余白へ調整した。
- `docs/changes/20260830_fluent-ui-design-system/mocks/jobs.md`（更新）: 「その他」を削除し、親子ジョブの表示をツリー形式と詳細属性へ更新した。
- `docs/changes/20260830_fluent-ui-design-system/mocks/jobs.html`（更新）: ジョブ階層の各ノードをリンク化し、アラートをヘッダー直下へ移動、関連オブジェクト欄からジョブを除外した。
- `docs/changes/20260830_fluent-ui-design-system/mocks/jobs.html`（更新）: モバイルメニューをドロワー開閉式にし、確認・停止ダイアログをブラウザー標準APIなしで表現した。
- `docs/design-guidelines.md`（更新）: 親子関係の視覚表現と、ジョブ階層と関連オブジェクトの重複を避ける規則を追加した。
- `docs/design-guidelines.md`（一覧操作更新）: 単一オブジェクトを表す行全体の詳細遷移、行内リンクとのイベント分離、キーボード操作、フォーカス表示を共通規則として追加した。
- `docs/design-guidelines.md`（情報階層更新）: 広いスコープ、オブジェクト識別、判断に重要な属性、補助属性、操作の順で表示する規則を追加した。時刻など細粒度の値を列揃えだけで先頭へ置かないことを明記した。
- `docs/changes/20260830_fluent-ui-design-system/mocks/index.html` とメニュー別HTML: 画面ごとの遷移・レイアウトを独立して確認できるように分割した。全画面で共通CSSを参照し、一覧はボックスではなく行リストに統一した。
- 詳細画面は各一覧HTML内に同居させるが、一覧の下へ連続表示しない。URLハッシュに応じてメイン領域の一覧ビューと詳細ビューを排他的に切り替え、詳細表示時も共通ナビゲーションを維持する。戻る操作では一覧のスクロール位置を復元する。
- `mocks/assets/object-page.js`: 予想票、騎手、調教師、馬主、データ取得状況で、行全体の選択、キーボード操作、一覧・詳細の排他的切り替え、スクロール復元、モバイルメニューだけを共有する。見出しや詳細内容はJavaScriptで後付けせず各HTMLに明示する。
- `mocks/assets/raceops-mock.css` と `mocks/races.html`: 一覧の補助列を内容幅へ変更し、状態バッジ右側の過剰な余白を削除した。レースの文脈属性を日付、開催場、発走時刻の粒度順へ配置した。
- `mocks/jobs.html`（一覧レイアウト更新）: レース一覧と同じ一覧文法へ揃え、主ラベルと対象、状態、更新時刻、詳細導線を独立列にした。ヘッダー、ページ余白、タブ、フィルター、行密度、モバイル折り返しも共通トークンへ統一した。
- `mocks/jobs.html` と共通モックナビゲーション: ジョブ固有のボタン型メニューを廃止し、全画面で同じグループ、リンク、選択状態へ統一した。モバイル開閉ボタンは Fluent System Icons `Navigation 24 Regular` のインラインSVGと44 pxの選択領域を使用する。
- メニュー別詳細HTML（構造更新）: 戻るリンク、パンくず、具体的なオブジェクト名の `h1`、基本情報、関連、履歴・管理情報を各HTMLへ直接記述し、共通スクリプトによるDOM組み替えを廃止した。
- `mocks/horses.html`（構造再作成）: HTML終了後へ詳細要素を追加してJavaScriptで移動する方式を廃止した。一覧・詳細を正しい文書構造内の排他的ビューとして定義し、戻るリンク、補助見出し、馬名の主見出し、基本情報、関連、出走履歴をレース・ジョブ詳細と同じ階層へ固定した。
- `mocks/horses.html`（関連表示更新）: 件数不定の騎手・調教師・馬主を3列固定ボックスから横幅いっぱいの行リストへ変更した。同じ騎手種別を複数行表示し、騎乗数、勝利数、最終日など関係の根拠を併記した。
- `docs/admin-ui-design.md` と `docs/design-guidelines.md`（関連リスト更新）: 同一種別が複数存在する関連を固定ボックスへ割り当てず、関係名、オブジェクト名、根拠から成る全幅行リストで表示する規則を正本へ追加した。
- `mocks/jockeys.html`: 関連を過去3年間の獲得賞金上位5頭へ変更し、順位、騎乗数、勝利数、最終騎乗日、獲得賞金を全幅行リストで表示した。騎乗履歴は開催日の新しい順とし、馬名・レース名、期間、開催場の検索UIを追加した。
- `docs/admin-ui-design.md` と `docs/design-guidelines.md`（騎手詳細更新）: 騎手の関連ランキング、履歴の既定順・検索条件、集計API要件、および調教師・馬主でも関連を全幅行リストへ統一する規則を追加した。
- `mocks/trainers.html` と `mocks/owners.html`: 騎手詳細と同じく、関連を過去3年間の獲得賞金上位5頭とし、順位、出走数、勝利数、最終出走日、獲得賞金を表示した。管理馬・所有馬の出走履歴を直近順にし、馬名・レース名、期間、開催場の検索UIを追加した。
- `docs/admin-ui-design.md`（調教師・馬主詳細更新）: 両詳細のランキング定義、履歴の既定順・検索条件、`PrizeMoney` を使う集計read API要件を正本へ追記した。
- `mocks/owners.html`（馬主名寄せ画面）: 馬主詳細の登録表記から開くモーダルを追加した。統合先の固定、統合元候補の検索・比較、統合による所有馬・登録表記・出走原文への影響、必須理由、不可逆性を一連の画面で確認できる。
- `docs/admin-ui-design.md`（馬主名寄せ更新）: 名寄せの画面順序、統合後の参照解決、原文スナップショット保持、旧URLの案内、監査項目を正本へ追記した。
- 馬、騎手、調教師、馬主、レースの詳細モック: 詳細ヘッダー右側へ「編集」を統一配置した。馬一覧の「馬を登録」は削除し、収集データは一覧から初期登録せず詳細から訂正する方針へ変更した。予想票はワークフロー未確定のため参照専用とし、編集は別変更セットへ分離した。
- `docs/admin-ui-design.md` と `docs/design-guidelines.md`（編集導線更新）: 初期登録ボタンを一覧へ置かない規則、編集可能なオブジェクトと運用状態オブジェクトの操作を区別する規則を追記した。
- `mocks/owners.html`（名寄せモーダル更新）: 独立ビューを廃止し、馬主詳細上の幅広い Fluent UI 相当モーダルへ変更した。候補検索に総件数、表示範囲、ページ番号、前後移動を追加し、実装負荷の高い類似理由ラベルは初期仕様から除外した。
- 運用メニューとモックの名称を「収集ジョブ」「データ取得状況」へ変更した。`collection-days.html` は削除し、対象日検索と日別の予定・完了・不完全状況を `jobs.html` へ統合した。日付の選択は `targetDate` query で収集ジョブを絞り込む。
- `docs/admin-ui-design.md` と `docs/design-guidelines.md`（運用情報設計更新）: 収集対象日の内部 read model は維持しつつ独立画面を作らないこと、データ取得状況は実行ジョブではなく対象データの現在状態であることを正本へ追記した。
- `mocks/acquisition-statuses.html`（データ取得状況詳細）: 完了済みデータを対象とする詳細ヘッダーの再取得を追加し、Fluent UI 相当の確認モーダルから新しい収集ジョブを投入する動作を表現した。関連を馬詳細と同じ全幅行リストへ統一し、取得履歴へ成功・失敗、エラー概要、試行回数、折りたたみ技術情報を追加した。過去の失敗履歴にはリラン操作を置かない。
- `docs/admin-ui-design.md`（データ取得状況更新）: 再取得の履歴保持と収集ジョブ投入、失敗履歴の情報階層、関連行の表示規則を正本へ追記した。
- `docs/admin-ui-design.md`（ジョブ関係・リラン整合性）: 現行コード調査に基づき、生成関係と集約依存関係を区別し、日次結果収集だけがレース単位の子を待つこと、集約親だけを復旧単位として失敗・未作成の子を復旧する状態遷移、同一 jobId の試行履歴を正本仕様へ追加した。
- `docs/admin-ui-design.md`（収集済みデータ訂正）: 既存ドメインの訂正イベントを基準に、馬・騎手・調教師・馬主・レースの編集可能／不可項目、必須理由、固定実行元、同時実行制御、再取得時の上書きを正本仕様へ追加した。
- `mocks/jobs.html`（ジョブ関係訂正）: 現行実装に存在しない「レース結果収集→出馬表／払戻」の関係を削除し、日次結果収集がレース単位の過去結果収集を待つ集約関係と、同一ジョブへのリラン・試行履歴へ修正した。関連図は生成経路・現在のジョブ・集約対象を別区画に分け、接続線、説明、件数要約、状態行で完了依存の有無が分かる表示へ変更した。
- `mocks/jobs.html`（生成関係の双方向状態）: 生成先から生成元だけでなく、生成元詳細から状態付きの「生成したジョブ」を辿れる画面状態を追加し、生成先の状態が生成元の完了条件ではないことを明示した。
- `mocks/horses.html`, `jockeys.html`, `trainers.html`, `owners.html`, `races.html`（訂正ページ）: 各詳細の編集から同一ファイル内の専用編集状態へ遷移できるようにし、モデル調査で確定した編集可能項目、変更不可項目、必須理由、保存確認、再取得時の上書き説明を共通レイアウトで追加した。
- `mocks/assets/raceops-mock.css`, `object-page.js`, `edit-form.js`（編集共通パターン）: 2列／モバイル1列フォーム、変更不可説明、必須理由エラー、Fluent UI相当の確認モーダルと詳細への復帰を共通化した。
- `docs/admin-ui-design.md`（馬主編集範囲）: 馬主の編集を表示名・登録表記の訂正に限定し、別 Owner の統合は名寄せだけで扱うこと、理由・変更前後・実行者を監査することを明記した。Owner API が存在しないとする古い記述も現行実装に合わせて是正した。
- レース、予想票、馬、騎手、調教師、馬主、収集ジョブ、データ取得状況の詳細モック: パンくずを `ナビゲーショングループ / オブジェクト種別` に統一した。レースの日付・開催場と収集ジョブの一覧ビュー・状態はパンくずから外し、ヘッダー本文または状態表示へ移した。
- `docs/design-guidelines.md`（パンくず更新）: パンくずへ表示する階層と、日付・状態・検索条件を含めない規則を追加した。
- `docs/admin-ui-design.md`（パンくず更新）: 関係遷移の文脈をパンくずへ連結する旧方針を改め、パンくずはナビゲーショングループとオブジェクト種別へ固定し、遷移元文脈は戻るリンクと関連セクションへ保持する方針に統一した。
- `docs/admin-ui-design.md`（馬詳細用語更新）: 馬詳細の第4セクションを利用者に馴染みやすい「関連」とし、関係名、件数、最終関係日、根拠を表示する正本仕様へ明確化した。
- `docs/design-guidelines.md`（詳細構造更新）: 戻るリンク、オブジェクトヘッダー、基本情報、関連、履歴・管理情報の共通順序と、利用者向け見出しを「関連」に統一する規則を追加した。
- `docs/changes/20260830_collection-task-cancellation-and-reset/README.md`: UI方針の更新として、個別取消ではなく全体一時停止・再開を標準UIにすることを追記した。

## Technical impact

- モックを実現するために不足する API、ReadModel、ドメインモデル、永続化を API 側へ追加・変更する。画面側で全履歴を再集計したり、Collector の DB を直接参照したりしない。Collector は API 所有の状態へ既存の HTTP 状態ストア境界からアクセスする。
- 現行の `jobs.parent_job_id` は生成関係と集約依存関係を区別できないため、関係種別を永続化し、管理 ReadModel へ返す。月次結果探索→日次結果探索、日次結果探索→日次結果収集は生成関係、日次結果収集→レース単位の過去結果収集は集約依存関係とする。
- 生成関係は双方向に照会できるようにし、生成先詳細の `生成元` と生成元詳細の `生成したジョブ` を同じ関係データから返す。生成先が複数ある場合は状態別件数と要対応優先の一覧を返すが、生成元の完了条件には使用しない。
- 集約依存関係では子にリランを提供せず、失敗した集約親を復旧単位とする。親リランは同じ親 `jobId` に試行を追加して `WaitingDependency` に戻し、失敗・デッドレターの子だけを同じ子 `jobId` で再試行し、未作成の子だけを新規作成する。成功済み・実行中・Ready の子は維持し、全子の最新状態が終端になった時点で親と日別状態を再集約する。生成関係では失敗した生成先ジョブ自身を復旧単位とし、生成元の状態は変更しない。
- 現行 `jobs` は累計 `attempt_count` と `last_error` しか保持せず全試行を表示できないため、開始・終了時刻、状態、エラー、起動種別（初回・自動再試行・手動リラン）を同じ `jobId` に紐づけて保存する試行履歴を追加する。`jobs.last_error` は最新試行の要約として維持する。
- 現行の手動再キューは試行回数を増やさず任意状態を `Ready` にできるため、管理操作を `rerun` 契約へ置き換え、未完了かつ最新試行が `Failed` / `DeadLetter` の場合だけ同じジョブへ1試行追加できるようにする。一時停止中は dispatch outbox / SQS へ送らず `Ready` を保持する。
- 収集済みデータ訂正は既存の Horse / Jockey / Trainer / Race の訂正イベントと `PATCH` 契約を再利用・強化する。馬は登録名・正規化名・性別・生年月日、騎手と調教師は表示名・正規化名・所属コード、レースはレース名・開催場・番号・グレード・芝ダート・距離・回り、馬主は表示名と同一 Owner の登録表記を編集対象とする。ID、外部別名ID、所有者参照、出走・結果・払戻、時点観測、別 Owner 統合は対象外とする。
- 既存訂正 API の任意理由を必須へ変更し、同時実行制御、固定実行元 `Admin UI`、変更前後の監査を追加する。再取得成功時は現在値を最新取得値で上書きし、訂正監査だけを保持する。
- `HorseRacingPrediction.Api` に `Microsoft.FluentUI.AspNetCore.Components` `4.14.4` と Icons package `4.14.4` を固定し、API の Web コンポーネント内に theme、shell、semantic wrapper を配置する。v5 stable への更新は別 change record とする。
- API で `AddFluentUIComponents()`、必要な provider、namespace、static web assets を登録する。
- 確認・危険操作のダイアログはブラウザー標準の `confirm()` / `alert()` を使用せず、`FluentDialog` と `FluentDialogProvider` で実装する。モバイルのメニューは `FluentDrawer` 相当のドロワーとして開閉し、Escape と backdrop で閉じる。
- `app.css` はアプリ固有 layout の置き場から、共通 token の補完、ドメイン固有 responsive layout、印刷・アクセシビリティ調整だけへ縮小する。
- 既存 Razor を共通 shell と Fluent components へ移行する。データ取得、イベントハンドラー、URL、form POST semantics は維持する。
- API 管理画面へ `Jobs.razor` と `JobDetail.razor` を追加し、`/jobs`、`/jobs/{jobId}` を canonical route とする。旧 `/collection-tasks` route は redirect component または endpoint で互換性を保つ。
- ジョブ検索 API が view、対象日、更新期間、ページング、関係表示に不足する場合は、既存 `/api/collection/tasks` の後方互換性を保って query / read model を拡張する。新しい job store や重複 endpoint は作らない。
- 失敗通知のリンク先を `/api/collection/tasks/{jobId}` から `/jobs/{jobId}` へ戻し、未認証時は login 後に元 URL へ復帰できるようにする。
- 一覧と詳細の各 Razor を既存 OOUI モデルに照合し、主ラベル、状態、主要属性、関係、CTA を共通 object components へ割り当てる。既存 API で関係 ID が得られずリンク化できない項目は文字列表示を維持し、欠落を実装結果へ記録する。
- `docs/design-guidelines.md` を新設する。既存 `docs/admin-ui-design.md` 第 10 章は内容を重ねず、同ガイドラインへの参照と変更履歴だけに縮める。
- Fluent UI Blazor がベストエフォート保守であること、version pin、更新時の visual / interaction regression test をガイドラインへ記載する。

### Documentation ownership

次のように文書を分担し、同じ規則を複数箇所に保守しない。

| 内容 | 正本 | 今回の扱い |
|---|---|---|
| 利用者・運用ユースケース、OOUI object / relationship、画面・URL・API の情報設計 | `docs/admin-ui-design.md` | 第 4 章と第 5 章を維持する。ジョブ画面復元との差異があれば、その事実だけを追記する。 |
| visual foundation、token、component usage、状態表現、responsive、accessibility、UI review | `docs/design-guidelines.md` | 新設して唯一の正本にする。 |
| Fluent UI Blazor の採用・version pin・共通 RCL・移行手順 | 本 change record | 実装完了時の結果と検証を追記する。 |

`admin-ui-design.md` 第 10 章は `docs/design-guidelines.md` への短い参照と移行履歴へ置換済みであり、第 11.3 節は本 change record の採用決定への参照へ更新済みである。第 4 章の OOUI モデルと第 5.1–5.6 節のジョブ情報設計は移動・複製しない。

## Decisions

### Adopt Fluent UI Blazor v4 stable

`.NET 8` 対応が明示された安定版 `4.14.4` を採用する。v5 RC は新 API を含むが、全画面刷新の基盤に prerelease を置くリスクが高いため採用しない。

### Keep RaceOps as a semantic layer, not a second visual system

`RaceOps UI` は Fluent と競合する独自 button / field / palette を持たない。本システム固有の status、entity link、page composition の名称としてだけ残す。これにより「Fluent UI ガイド」と「RaceOps UI ガイド」の重複を避ける。

### Use OOUI as the composition model

Fluent UI は visual foundation と interaction primitive を提供し、OOUI はそれらを何のためにどう構成するかを決める。ページ単位で Fluent components を並べるのではなく、object header、condensed object item、attribute group、relationship list、object-scoped actions の共通文法へ組み立てる。OOUI モデル自体は既存文書を正本とし、デザインガイドラインには適用規則だけを記載する。

### Restore jobs to the API management UI

2026-08-30 の UI 撤去判断を本 change record で置き換え、API 管理画面をジョブの canonical UI とする。これは `admin-ui-design.md` 第 5.1–5.6 節が既に定める `/jobs` の情報設計を実装へ戻すものであり、新しい運用モデルを作るものではない。Collector に同等画面を残して両方を発展させる案は、操作、表示語彙、権限、通知 URL が再び分岐するため採用しない。API が正本である以上、管理 UI も正本に寄せる。

### Pause the queue, not individual jobs

利用者が求める「いったん収集を止める」は、ジョブ状態を個別に `Cancelled` へ変えることではなく、配送層を一時停止することと解釈する。これにより起動済み処理は安全に完了し、Ready ジョブの意図・優先順位を失わずに再開できる。SQS purge は不可逆なため、対象キュー、Running の扱い、保持されるDB状態、再開時の順序を確認ダイアログに必ず明記する。

### Keep UI components inside the API project

UI の利用先は API 管理画面だけなので、別 Razor Class Library は作らない。token と behavior を含む共通コンポーネントは `HorseRacingPrediction.Api/Web` 内に集約し、ページ間で CSS や Razor をコピーしない。

### Migrate the complete visible surface in one approved change, with reviewable commits

新旧のボタン、入力、余白が長期間混在すると一貫性が失われるため、承認範囲は全画面とする。実装コミットは API 内の共通基盤、shell、pages、Collector のサービス化、documentation の検証可能な単位へ分ける。

### Rejected alternatives

- **CSS の見た目だけ調整**: 依存追加はないが、重複、accessibility、状態 API、ページごとの差異を解消できない。
- **MudBlazor / Radzen**: 有力だが、今回ユーザーが例示した Fluent と現在の Microsoft / Blazor stack の親和性を上回る理由がない。
- **Fluent UI をページから直接全面利用**: 最短だが、domain status と page composition がライブラリ API に散らばり、将来更新が難しくなる。
- **ダークモード同時導入**: token 設計には備えるが、visual regression と状態色検証の範囲を倍増させるため初回対象外とする。

## Acceptance criteria

- [ ] API 管理画面の全ルートが Fluent UI ベースの共通 shell、typography、spacing、surface、controls を使用し、主要な旧 `.btn` / `.card` / `.toolbar` の独自視覚定義へ依存しない。
- [ ] API 管理画面の共通 UI が `HorseRacingPrediction.Api/Web` に一元化され、同じ token / component の CSS または Razor コピーがない。
- [ ] `docs/design-guidelines.md` が、原則、token、layout、typography、color、component usage、state、responsive、accessibility、writing、review checklist を含む唯一の正本である。
- [ ] `docs/design-guidelines.md` が OOUI の適用規則を含み、オブジェクトモデルそのものは `docs/admin-ui-design.md` を参照して重複定義していない。
- [ ] レース、馬、騎手、調教師、馬主、予想票、収集ジョブ、データ取得状況の一覧項目が、主ラベル、状態、主要属性、関係、詳細 affordance の共通文法に従う。
- [ ] 各オブジェクト詳細が canonical URL、一貫した object header、関係リンク、状態に応じた CTA を持ち、内部 ID や処理名が利用者向けの主語になっていない。
- [ ] 騎手詳細の関連に、過去3年間の獲得賞金合計上位5頭が順位、騎乗数、1着数、最終騎乗日、獲得賞金とともに表示される。
- [ ] 騎手の騎乗履歴が開催日の新しい順で表示され、馬名・レース名、期間、開催場で検索でき、条件とページがURLに保持される。
- [ ] 馬、騎手、調教師、馬主の関連が固定列ボックスではなく、関係名、オブジェクト名、根拠を含む全幅行リストとして表示される。
- [ ] 調教師・馬主詳細も、過去3年間の獲得賞金合計上位5頭と直近順の検索可能な出走履歴を、騎手詳細と同じ列・期間表記・検索UIで表示する。
- [ ] 馬主詳細から名寄せモーダルを開き、統合先、統合元候補、統合前後の影響、必須理由、不可逆性を実行前に確認できる。
- [ ] 馬主詳細から名寄せモーダルを開き、ページングされた統合元候補を検索・選択できる。候補には客観的属性だけを表示し、類似理由の自動判定を前提にしない。
- [ ] 馬主名寄せ後もレース時点の馬主名原文を保持し、統合元URLから統合先を辿れ、実行者・理由・対象・影響件数を監査できる。
- [ ] 馬主の編集では表示名と登録表記だけを訂正でき、名寄せ対象の選択や所有馬参照の移動を行えない。変更理由と変更前後を監査できる。
- [ ] 馬一覧に初期登録ボタンがなく、馬、騎手、調教師、馬主、レースの各詳細ヘッダーから一貫した位置の「編集」へ到達でき、`/{objects}/{id}/edit` の専用ページで現行モデル調査により確定したプロフィール項目を収集済みデータの訂正として保存できる。保存後は同じ詳細へ戻って結果を表示する。外部ID、内部ID、時点履歴、関連参照を不整合にする項目は編集できない。訂正理由は必須で、変更前後・実行者・実行日時を監査できる。その後の再取得が成功した場合は手動訂正値を保持せず、最新の取得値で現在値を上書きする。予想票には編集ボタンがない。
- [ ] 運用メニューが「収集ジョブ」「データ取得状況」の2項目となり、収集対象日の独立メニューがない。
- [ ] 収集ジョブ一覧で対象日を検索でき、日別サマリーの日付から同日の収集ジョブへ絞り込める。
- [ ] 完了済みのデータ取得状況詳細から再取得の収集ジョブを確認後に投入でき、成功・失敗を含む既存の取得履歴を保持する。過去の失敗履歴行にはリランを表示しない。
- [ ] データ取得状況の関連が関係名、リンク可能なオブジェクト名、関係の根拠を含む全幅行リストで表示され、失敗履歴にはエラー概要と折りたたみ技術情報がある。
- [ ] レース一覧の「今日」「今週」「結果確定」「すべて」は同じ非グループ化行形式とページングを使用し、期間、日付、開催場、検索語、並び順、ページをURL queryに保持する。
- [ ] レース一覧にアプリ内の「表示条件を保存」を置かず、日付と開催場をクリックして絞り込める。
- [ ] レース一覧は日付・開催場などの個別リンクを除いた行全体を詳細への選択領域とし、マウスクリック、Enter、Spaceで遷移でき、明確なフォーカス表示を持つ。
- [ ] レース一覧とジョブ一覧が同じヘッダー、タブ、フィルター、行密度を使用し、広いスコープと主ラベルを先に、状態・時刻・詳細を後に配置する。状態バッジを主ラベル内へ混在させない。
- [ ] レース詳細は基本情報を見出し直下に表示し、出走表、オッズ、結果を主要ビューとして切り替えられる。単勝・複勝は現行 `RaceOddsResponse` に基づき、取得不能時は理由を表示する。
- [ ] レース詳細は全出走について馬、騎手、調教師、レース時点の馬主を表示し、解決済みIDから各詳細へ相互遷移できる。
- [ ] API 管理画面の主ナビゲーションから `/jobs` を開け、要対応、処理中、待機中、最近完了、すべての view を切り替えられる。
- [ ] ジョブ一覧の filter と page が URL に保持され、詳細から一覧へ戻ったとき view、filter、page が復元される。
- [ ] `/jobs/{jobId}` が人が読める処理名と対象、状態、主要時刻、失敗要約、親子ジョブ、取得可能な対象日・レース、操作 timeline、technical details を表示する。
- [ ] リランは未完了かつ最新試行が Failed / DeadLetter の収集ジョブ詳細だけに表示され、手動操作1回につき同じ jobId に新しい試行を1件追加する。追加試行が失敗した場合は成功するまで再度リランでき、完了後は表示しない。理由は任意であり、未入力でも実行者・実行時刻・対象ジョブを監査へ残して受け付ける。主画面には累計試行回数と最新の失敗内容だけを表示し、詳細内の件数制限なしの折りたたみでは過去の全試行と各失敗内容を確認できる。展開してもデスクトップ・モバイルのパネル幅を超えず、日時と長いエラー文が重ならない。過去の試行を選択する操作と個別取消は表示しない。
- [ ] ジョブ関係は生成経路・現在のジョブ・集約対象を別区画で表示し、線種だけでなくテキストで完了依存の有無を説明する。生成先から生成元、生成元から状態付きの生成したジョブを双方向に辿れ、生成元が完了を待たないことを表示する。生成先と集約対象は全件・完了・要対応を要約し、320pxでも意味順序を維持した1列になる。集約対象の子にはリランを表示せず、日次結果収集などの集約親だけを復旧単位とする。親リランでは失敗・デッドレター・未作成の子だけを復旧して WaitingDependency から再集約し、成功済み・実行中・Ready の子と生成関係の親状態は変更しない。
- [ ] ユーザー管理と操作別ロールは追加せず、管理画面へ到達できる利用者は編集、リラン、再取得、全体停止・再開、名寄せをすべて実行できる。権限による表示差はなく、状態と同時実行制御による可否だけをサーバーが返す。管理画面操作の監査上の実行元は固定値 `Admin UI` とし、操作日時・対象・理由・変更前後を記録する。
- [ ] ジョブ詳細には個別取消を表示せず、一覧の `すべてのジョブを一時停止` でキュー全体を停止できる。
- [ ] 一時停止は dispatcher の送信停止と SQS 本体・DLQ purge を行い、Running は完了まで継続、Ready / Pending はDBに保持することを確認画面で説明する。
- [ ] 一時停止中の一覧に `再開` を表示し、Ready を priority 降順、AvailableAt・CreatedAt・JobId の順で再投入できる。Pending は再投入対象外であることを表示する。
- [ ] 確認ダイアログがブラウザー標準の confirm / alert ではなく Fluent Dialog で表示され、初期フォーカス、Escape、キャンセル、フォーカス復帰を備える。
- [ ] モバイル上部の「メニュー」を押すとナビゲーションドロワーが開き、項目選択、backdrop、Escape で閉じられる。
- [ ] 旧 `/collection-tasks` と詳細 URL が対応する `/jobs` URL へ redirect し、失敗通知から認証後にジョブ詳細へ到達できる。
- [ ] Collector の Web Host、全 Razor 画面、HTTP endpoint、静的資産、ローカル状態ストアと `UseApiStateStore=false` が削除され、計画済みジョブを実行・報告するサービスだけになる。
- [ ] 失敗した収集ジョブの詳細には、関係種別を伴うジョブリンク、`対象レースを見る`、`同じ対象日の収集ジョブを見る`、`技術情報を確認` が表示され、リランと移動・診断操作が視覚的に分離される。
- [ ] job not-found、loading、empty、error、permission denied、operation conflict の各状態が 320 / 720 / 1280 CSS px とキーボード操作で確認できる。
- [ ] desktop の DataGrid / list-detail と mobile の縦リストで、同じオブジェクトの識別と情報優先度が維持される。
- [ ] `docs/admin-ui-design.md` の重複するデザインシステム記述が正本への参照に整理され、既存の情報設計と履歴は失われない。
- [ ] 320 / 720 / 1280 CSS px でナビゲーション、一覧、詳細、編集、dialog、loading / empty / error state が欠けず、主要操作へ横スクロールなしで到達できる。
- [ ] Tab / Shift+Tab / Enter / Space / Escape で navigation、forms、dialog、menu を操作でき、明確な focus indicator がある。
- [ ] status と validation は色だけに依存せず、通常テキストと UI component が WCAG 2.2 AA の contrast を満たす。
- [ ] 絵文字がUI、モック、ガイドライン例に残っておらず、アイコンを使う場合は Fluent System Icons に限定される。
- [ ] loading、empty、error、disabled、success、danger confirmation の共通表示が代表ページで確認できる。
- [ ] 既存の route、認証、検索、編集、リラン／再取得、pagination、外部リンクの回帰テストが通る。
- [ ] Fluent UI package は stable version に固定され、production assets は CDN に依存しない。
- [ ] `dotnet build HorseRacingPrediction.sln`、関連 test、`git diff --check` が成功する。

## Delivery plan

実装はオブジェクト単位の vertical slice（契約・状態・一覧・詳細・操作・テストを同じ単位で完了）で進める。Blazor の query string は `SupplyParameterFromQuery` / `GetUriWithQueryParameter` を利用し、一覧から詳細へ遷移しても検索条件を再現できるようにする。各 slice 完了時に受け入れ基準を更新し、最後に全画面の visual / keyboard 回帰を実施する。

1. API プロジェクトへ Fluent UI package、theme、providers、共通 primitives を追加する。
2. API 管理画面の App shell とナビゲーションを移行し、収集ジョブとデータ取得状況の canonical navigation と旧 API 管理画面 URL redirect を復元する。
3. API のジョブ一覧・詳細・通知導線を復元したうえで、その他の一覧、詳細、編集、memo、UI states を OOUI の object / relationship / CTA patterns へ移行する。
4. Collector の収集ジョブ、日次状況、データ取得状況の Razor 画面と通常運用ナビゲーションを削除し、必要な収集処理・状態・サービス境界だけを維持する。
5. 旧 CSS を削減し、全画面の visual / responsive / accessibility regression を実施する。
6. `docs/design-guidelines.md` を正本として完成させ、既存文書の重複を参照へ置き換える。
7. 検証結果、差分、残課題を本記録へ追記し `Implemented` にする。

## Verification record

## Remaining task breakdown - 2026-08-31

完了判定前に残る作業を、並列確認しやすい単位へ分解する。各タスクは完了時にこの change record へ検証結果を追記し、検証済みチェックポイントとしてコミット可能な状態にする。

1. UI 受け入れ監査
   - 主要画面に残る旧 `card` / `toolbar` / `btn` / `object-list` 依存を Fluent UI ベースの共通表現へ寄せる。
   - 一覧・詳細・編集・モーダル・メモ・ページングで、object header、全幅関連リスト、行クリック遷移、絶対リンク、絵文字なし、ブラウザー標準 dialog 不使用を確認する。
   - 320 / 720 / 1280 CSS px で崩れやすい CSS を静的に確認し、必要なら軽微な補正を行う。
2. API / 状態モデル監査
   - リラン、再取得、一時停止、再開、取得状況、親子ではなく生成関係・集約依存関係としてのジョブ関連を、実装と受け入れ基準で突き合わせる。
   - Collector が Web Host / Razor / HTTP endpoint / ローカル状態ストアを持たず、計画済みジョブの実行サービスに限定されていることを確認する。
   - 不足する回帰テストがあれば追加する。
3. ドキュメント / コミット境界監査
   - `docs/design-guidelines.md` と `docs/admin-ui-design.md` の正本関係、重複排除、OOUI ルール、チェックポイントコミット規則を確認する。
   - 変更セットに実装差分、残課題、検証結果を追記し、`AGENTS.md` の適度なタイミングでコミットする規則が維持されていることを確認する。
   - ビルド、関連テスト、全体テスト、`git diff --check` を通したうえで目的単位にコミットする。

実装中。初回レビュー後に `Microsoft.FluentUI.AspNetCore.Components` / Icons `4.14.4`、provider、収集ジョブ一覧・詳細、データ取得状況一覧、運用ナビゲーション、旧URL redirectを追加した。再取得は完了済みジョブを維持して新しいjobIdを生成し、親リランは親を再実行して必要な子を再列挙することで失敗・DeadLetter・前回未作成の子を復旧する。操作は固定実行元 `Admin UI` と任意理由を監査へ保存する。Collectorの旧Razor画面・静的資産・JRAテストHTTP endpointと `UseApiStateStore` 設定を削除した。新規状態テスト3件、ジョブ管理APIテスト2件を追加した。`dotnet test HorseRacingPrediction.sln --no-restore -v:q` により API 83件、Collector 88件を含む全テストが成功している。残課題は、レース詳細オッズ、関係者詳細の履歴検索・上位馬、名寄せモーダル、全画面の Fluent component 移行、visual / responsive / accessibility 回帰であり、完了条件未達のため状態は `Approved` のままとする。

## Deviations and follow-up

JRA JSON抽出サービス本体は単体テスト互換性のため保持したが、HTTP endpointは削除した。ダークモード導入は別change recordとする。

## Implementation update - 2026-08-30

今回の追加実装で、データ取得状況と収集ジョブ、馬主名寄せの未接続箇所を補強した。

- `AgentAcquisitionStatusEntity` / `AgentAcquisitionStatusReadModel` / `IProcessingStateStore.UpsertAgentAcquisitionStatusAsync` に `OriginJobId` を追加した。Collector が API 状態ストアを通じて実行中ジョブを処理している場合、`HttpProcessingStateStoreProxy` のタスクスコープから元ジョブ ID を取得状況へ記録する。
- 既存 SQLite 状態 DB 向けに `agent_acquisition_statuses` テーブル作成と `origin_job_id` 列追加の起動時補正を追加した。
- `/api/collection/acquisitions/{acquisitionKey}` をテスト用アプリにも登録し、取得状況詳細が `OriginJobId` を返す回帰テストを追加した。
- API 管理画面の `データ取得状況` 詳細に、元ジョブリンク、作成日時、再取得ボタンを追加した。再取得は元ジョブが存在する場合に限り、既存の完了済みジョブ再取得 API へ接続する。
- データ取得状況の再取得は Fluent Dialog で確認してから実行する。確認文には、元ジョブを残したまま新しい収集ジョブを投入し、既存の取得履歴と失敗履歴を保持することを明示した。
- データ取得状況詳細に `関連` セクションを追加し、取得対象、関連レース、元ジョブを関係名・リンク・根拠の全幅行として表示する。
- 馬主詳細の名寄せを OwnerId 手入力中心から、モーダル内の候補検索、候補選択、50件ページング、統合先・統合元確認へ更新した。自動の類似理由表示は行わない。
- 馬主表示名更新 API の回帰テストを追加し、表示名訂正後も既存登録表記が同一 Owner の表記として残ることを確認した。
- ジョブ詳細の関連表示を `生成経路`、`現在のジョブ`、`集約依存関係` に分け、生成元から生成したジョブ、生成先から生成元を双方向に辿れるリンクへ整理した。集約対象の子単独ではなく、集約親を復旧単位とする説明も画面に追加した。
- 騎手・調教師の関連ランキングで、獲得賞金合計の集計対象を直近3年の出走へ絞る API 集計を追加した。履歴一覧自体は直近順の全期間表示を維持する。
- 詳細ページ内の相対リンクの一部を `/jobs/...`、`/owners/...`、`/races/...` などの絶対パスへ修正し、詳細画面配下で誤った相対遷移にならないようにした。
- 旧インライン編集用の残存状態変数を派生状態として扱い、Razor コンパイル警告を解消した。
- Collector の `appsettings.json` から `StateDirectory` / `JobStoreFileName` を削除し、Collector 起動設定からローカル状態ストアを想起させる項目をなくした。
- 失敗通知のリンク先を `/api/collection/tasks/{jobId}` から canonical UI の `/jobs/{jobId}` へ修正した。
- dispatcher は一時停止中に outbox を送信しないことを単体テストで固定した。
- 再開時は停止状態のまま SQS purge で失われた可能性がある配送済み Ready ジョブを再投入してから停止解除する。再投入対象は配送可能な Ready のみで、priority 降順、AvailableAt、CreatedAt、JobId の順に outbox から取り出される。
- 既存状態 DB に `agent_acquisition_statuses` テーブルがない場合も起動時に作成できることを単体テストで固定した。
- `docs/admin-ui-implementation-audit.md` のジョブ API 記述を `/api/admin/jobs` canonical と旧 `/collection-tasks` redirect の表現へ更新し、古い「親子ジョブ」表現を生成関係・集約依存関係に合わせた。

追加検証:

- `dotnet build HorseRacingPrediction.sln --no-restore -v:minimal`: 成功、警告 0。
- `dotnet test tests/HorseRacingPrediction.Api.Tests/HorseRacingPrediction.Api.Tests.csproj --no-build -v:minimal`: 成功、87 件。
- `dotnet test tests/HorseRacingPrediction.Collector.Tests/HorseRacingPrediction.Collector.Tests.csproj --no-build -v:minimal`: 成功、90 件。
- `dotnet test HorseRacingPrediction.sln --no-build -v:minimal`: 成功、全 566 件。
- `git diff --check`: 成功。作業ツリー上の複数ファイルで CRLF 変換警告のみ。

## Implementation update - 2026-08-31 collector ownership pass

変更セットと実装を再監査し、Collector のサービス境界に関する残存不整合を修正した。

- ジョブ状態ストア、状態 ReadModel、ジョブ関係、試行履歴、状態管理 endpoint のソース配置を `HorseRacingPrediction.Collector/Scheduling` から `HorseRacingPrediction.CollectionOperations/Scheduling` へ移動した。名前空間は既存互換のため維持するが、物理的な所有元は API / CollectionOperations 側に寄せ、Collector は実行サービスと HTTP 状態ストア proxy の利用に限定する。
- `HorseRacingPrediction.CollectionOperations.csproj` の旧リンク設定を削除し、移動後の実体ファイルを通常のプロジェクトソースとして扱うようにした。
- `HorseRacingPrediction.Collector.csproj` から、旧 Web / 状態管理由来の `Microsoft.AspNetCore.App` FrameworkReference、状態ストア実装除外リスト、`Microsoft.EntityFrameworkCore.Sqlite` 参照を削除した。
- `docs/collector-design.md` から旧 Blazor Server 画面一覧と「ジョブ永続ストアの API 集中管理は今後課題」という古い記述を削除し、Collector は Web Host / 管理画面 / 通常運用 endpoint を持たないサービスであることを明記した。

追加検証:

- `dotnet build HorseRacingPrediction.sln --no-restore -v:minimal`: 成功、警告 0。
- `dotnet test tests/HorseRacingPrediction.Api.Tests/HorseRacingPrediction.Api.Tests.csproj --no-build -v:minimal`: 成功、88 件。
- `dotnet test tests/HorseRacingPrediction.Collector.Tests/HorseRacingPrediction.Collector.Tests.csproj --no-build -v:minimal`: 成功、92 件。

## Implementation update - 2026-08-31

レビューで見つかった運用系の不整合を修正した。

- API のメンテナンス middleware が停止中の通常 mutation を 503 にする一方で、`POST /api/admin/jobs/resume` だけは通すようにした。これにより、ジョブ一覧の `すべてのジョブを一時停止` 後に `再開` が実運用でも実行できる。
- テスト用ホストへ本番と同じメンテナンス middleware を追加し、停止中は再取得などの mutation が止まり、`resume` だけ通ることを API テストで固定した。
- リラン API を集約親専用から `RerunJobAsync` へ整理した。集約親は失敗・DeadLetter の集約対象だけを再投入し、単独ジョブまたは生成先ジョブは同じ `jobId` のまま自身を再投入する。集約対象の子ジョブ単独リランは拒否する。
- `AttemptCount` は自動再キューや手動リラン要求時ではなく、成功・失敗・DeadLetter の試行記録時に `job_attempts` と同期して更新するようにした。主画面の試行回数と折りたたみ内の試行履歴が同じ意味になる。
- データ取得状況詳細は `/acquisition-statuses` と `/acquisition-statuses/{key}` の同一コンポーネント内遷移でも `OnParametersSetAsync` で詳細を読み直すようにした。
- データ取得状況の `再取得` CTA は、元ジョブが完了済みの場合だけ表示する。元ジョブが未完了・失敗中の場合は、完了後に実行できる旨を表示する。
- 馬主名寄せ API の監査実行元を、ユーザー管理なしの前提に合わせて固定値 `Admin UI` に統一した。

追加検証:

- `dotnet build HorseRacingPrediction.sln --no-restore -v:minimal`: 成功、警告 0。
- `dotnet test tests/HorseRacingPrediction.Api.Tests/HorseRacingPrediction.Api.Tests.csproj --no-restore -v:minimal`: 成功、88 件。
- `dotnet test tests/HorseRacingPrediction.Collector.Tests/HorseRacingPrediction.Collector.Tests.csproj --no-restore -v:minimal`: 成功、92 件。
- `dotnet test HorseRacingPrediction.sln --no-build -v:minimal`: 成功、全 568 件。
- `git diff --check`: 成功。作業ツリー上の複数ファイルで CRLF 変換警告のみ。

## Implementation update - 2026-08-31 additional UI pass

追加監査で見つかった API 管理画面 UI の残存不整合を修正した。

- 馬・騎手・調教師・馬主の一覧を FluentCard / FluentTextField / FluentButton と共通 `data-list` / `selectable-row` パターンへ寄せ、旧 `toolbar` / `btn` / `object-list` に依存しない表示へ更新した。
- 馬・騎手・調教師・馬主の一覧で query / page を URL に保持し、詳細から戻ったときに検索条件を復元できるようにした。行全体を詳細への選択領域とし、Enter / Space による遷移を追加した。
- 主ナビゲーション、詳細ページ、編集ページ、履歴内の主要リンクを `/races/...`、`/horses/...`、`/jockeys/...`、`/trainers/...`、`/owners/...`、`/predictions/...` のルート基準 URL に統一した。
- 馬主一覧 `/owners` を Fluent UI 版として復元し、主ナビゲーションから到達できる canonical collection を維持した。
- UI / design guideline 範囲で絵文字、ブラウザー標準 `confirm()` / `alert()`、主要オブジェクトへの相対リンクが残っていないことを `rg` で確認した。

追加検証:

- `dotnet build HorseRacingPrediction.sln --no-restore -v:minimal`: 成功。既存の型重複警告 6 件と、実行中 testhost による一時コピー再試行警告 1 件あり。
- `dotnet test tests/HorseRacingPrediction.Api.Tests/HorseRacingPrediction.Api.Tests.csproj --no-build -v:minimal`: 成功、88 件。
- `dotnet test tests/HorseRacingPrediction.Collector.Tests/HorseRacingPrediction.Collector.Tests.csproj --no-build -v:minimal`: 成功、92 件。
- `git diff --check`: 成功。作業ツリー上の複数ファイルで CRLF 変換警告のみ。

残課題は、編集ページ・一部詳細ページ・予想票ページに残る旧 `card` / `toolbar` / `btn` 表現の完全 Fluent 移行、ならびに 320 / 720 / 1280 CSS px の実機相当 visual / keyboard 回帰である。完了条件未達のため状態は `Approved` のままとする。

## Implementation update - 2026-08-31 follow-up

変更セットの未達項目のうち、ジョブ詳細と一覧、レース詳細の導線不備を追加修正した。

- 収集ジョブ一覧に対象日 `targetDate` フィルターを追加し、query string に保持するようにした。行にも deduplication key から抽出できる対象日を表示し、日別サマリーなどから同日の一覧へ絞り込む導線の受け口を補強した。
- 収集ジョブ詳細に `対象レースを見る`、`同じ対象日の収集ジョブを見る`、`技術情報を確認` を関係リストとして追加した。対象レースと対象日は payload の `RaceId` / `RequestedByRaceId` / `RaceDate` / `TargetDate` と deduplication key から取得できる範囲で表示する。
- 収集ジョブ詳細の技術情報 disclosure に Job ID、JobType、DeduplicationKey、AvailableAt、lease、payload、最新エラーを表示するようにした。
- レース詳細とレース編集、予想票一覧の相対リンクを絶対パスへ修正し、詳細画面配下で関連オブジェクトや編集画面への遷移が壊れないようにした。
- 予想票一覧の RaceId リンクを null-safe にし、Razor コンパイル警告を解消した。

追加検証:

- `dotnet build src/HorseRacingPrediction.Api/HorseRacingPrediction.Api.csproj --no-restore -v:minimal -p:OutDir=%TEMP%/hrp-codex-build-api/`: 成功、警告 0。通常の solution build は Visual Studio と起動中の `HorseRacingPrediction.Api` が `src/HorseRacingPrediction.Api/bin/Debug/net8.0/HorseRacingPrediction.CollectionOperations.dll` をロックして失敗したため、同じ API プロジェクトを一時出力先で検証した。
- `dotnet test tests/HorseRacingPrediction.Api.Tests/HorseRacingPrediction.Api.Tests.csproj --no-restore -v:minimal`: 成功、88 件。
- `dotnet test HorseRacingPrediction.sln --no-restore -v:minimal`: 成功、全 568 件。
- `git diff --check`: 成功。作業ツリー上の複数ファイルで CRLF 変換警告のみ。

残課題は、全受け入れ基準を満たすための visual / responsive / accessibility 実機確認、全画面 Fluent component 移行の細部確認、権限 denied 表示の代表確認である。完了条件は未達のため、状態は `Approved` のままとする。

## Implementation update - 2026-08-31 (acceptance follow-up)

変更セットの未達項目を再確認し、コード上で安全に閉じられる範囲を追加対応した。

- 騎手詳細、調教師詳細、馬主詳細の履歴検索条件を `historySearch` query に保持するようにした。`次の10件を読み込む` は `historyPage` query を進めるため、詳細 URL の共有、戻る操作、再読み込みで検索語と読み込み済みページを復元できる。
- 騎手詳細、調教師詳細、馬主詳細に残っていた相対リンクを、詳細画面配下でも誤解釈されない絶対パスへ修正した。
- レース一覧の状態バッジ class 判定が `RaceStatus` enum を受け取れるようにし、Razor ビルドエラーを解消した。
- 一時的に Razor 生成キャッシュが削除済み `Owners.razor` を参照してビルド失敗したため、API プロジェクトを clean して再ビルドした。clean 中、一部 bin/obj 生成物は実行中の `HorseRacingPrediction.Api` / Visual Studio にロックされ削除できなかったが、再ビルドは成功した。

追加検証:

- `dotnet build HorseRacingPrediction.sln --no-restore -v:minimal`: 成功、警告 0。
- `dotnet test tests/HorseRacingPrediction.Api.Tests/HorseRacingPrediction.Api.Tests.csproj --no-build -v:minimal`: 成功、88 件。
- `dotnet test tests/HorseRacingPrediction.Collector.Tests/HorseRacingPrediction.Collector.Tests.csproj --no-build -v:minimal`: 成功、92 件。
- `dotnet test HorseRacingPrediction.sln --no-build -v:minimal`: 成功、全 569 件。
- `git diff --check`: 成功。作業ツリー上の複数ファイルで CRLF 変換警告のみ。

Acceptance criteria のうち、レース詳細の単勝・複勝オッズ表示、履歴検索条件の URL 保持、Collector の service-only 化、ユーザー権限による表示差を設けない方針、Fluent UI package の stable pin、既存 route / 操作 API の回帰テストは実装または自動検証済みとして扱える。一方、全画面の Fluent component 統一、Fluent Dialog の初期フォーカス・Escape・フォーカス復帰、mobile drawer の Escape / backdrop、permission denied / operation conflict / loading / empty / error の全状態、320 / 720 / 1280 CSS px と keyboard / contrast / 200% zoom の visual / accessibility 回帰は、ブラウザー上の手動または自動 UI 検証が未実施である。よって本変更セットはまだ `Implemented` にせず、状態は `Approved` のままとする。

## Implementation update - 2026-08-31 final consistency pass

サブエージェントの変更を統合した後、変更セットと実装を再度突き合わせ、遷移と操作露出の不整合を追加修正した。

- 馬詳細の `関連` と履歴内リンク、馬・騎手・調教師の編集画面の戻り先、旧 `/collection-tasks` redirect をすべてルート基準の絶対パスへ統一した。詳細画面配下で相対 URL として解釈され、誤った画面へ遷移するリスクを解消した。
- 予想票詳細から `データ訂正` と `取り下げ` の画面操作を外した。予想業務の扱いが未確定のため、現時点では読み取り専用に近い表示とし、編集・取り下げは画面に露出しない。
- 主要な詳細・一覧リンクについて、`href="horses..."` や `NavigateTo("jobs...")` のような相対ルートが実装側に残っていないことを `rg` で確認した。
- UI / change record / design guideline 範囲で、絵文字とブラウザー標準 `confirm()` / `alert()` 呼び出しが残っていないことを確認した。

追加検証:

- `dotnet build HorseRacingPrediction.sln --no-restore -v:minimal`: 成功、警告 0。
- `dotnet test tests/HorseRacingPrediction.Api.Tests/HorseRacingPrediction.Api.Tests.csproj --no-build -v:minimal`: 成功、88 件。
- `dotnet test tests/HorseRacingPrediction.Collector.Tests/HorseRacingPrediction.Collector.Tests.csproj --no-build -v:minimal`: 成功、92 件。
- `dotnet test HorseRacingPrediction.sln --no-build -v:minimal`: 成功、全 569 件。
- `git diff --check`: 成功。作業ツリー上の複数ファイルで CRLF 変換警告のみ。

## Implementation update - 2026-08-31 checkpoint audit

コミット前の厳しめ監査として、残タスクを `UI 受け入れ監査`、`API / 状態モデル監査`、`ドキュメント / コミット境界監査` の3系統に分解し、並列確認可能な作業単位として本 change record に記録した。

追加対応:

- `AGENTS.md` に、長時間・多ファイル作業では設計更新、API / 状態モデル、UI、テスト、ドキュメント反映などの検証済みチェックポイントごとに適度なタイミングでコミットする規則が維持されていることを確認した。
- API 管理画面の主要実装に残っていた旧 `card` / `toolbar` / `btn` / `object-list` 依存を再監査し、共通の `detail-card` / `action-row` / `app-button` 系の表現へ整理済みであることを確認した。
- 未使用の旧 `object-list` CSS 定義を削除し、旧 `.btn` / `.card` / `.toolbar` / `.object-list` セレクターが API 管理画面 CSS に残っていない状態にした。
- 主要ページの相対 `href` が残っていないこと、実装コードでブラウザー標準 `confirm()` / `alert()` を呼び出していないことを静的に確認した。

追加検証:

- `rg -n -e 'class="btn' -e 'class="toolbar' -e 'class="card' -e 'class="object-list' -e '\.btn' -e '\.toolbar' -e '\.card' -e '\.object-list' src/HorseRacingPrediction.Api/Web src/HorseRacingPrediction.Api/wwwroot/app.css`: 該当なし。
- `rg -n -P 'href="(?!/|#|@|mailto:|https?:)' src/HorseRacingPrediction.Api/Web/Components/Pages`: 該当なし。
- `rg -n -e 'confirm\(' -e 'alert\(' src/HorseRacingPrediction.Api/Web docs/changes/20260830_fluent-ui-design-system docs/design-guidelines.md`: 実装呼び出しは該当なし。change record の禁止事項説明のみ該当。
- `dotnet build HorseRacingPrediction.sln --no-restore -v:minimal`: 成功、警告 0。
- `dotnet test tests/HorseRacingPrediction.Api.Tests/HorseRacingPrediction.Api.Tests.csproj --no-build -v:minimal`: 成功、88 件。
- `dotnet test tests/HorseRacingPrediction.Collector.Tests/HorseRacingPrediction.Collector.Tests.csproj --no-build -v:minimal`: 成功、92 件。
- `dotnet test HorseRacingPrediction.sln --no-build -v:minimal`: 成功、全 569 件。
- `git diff --check`: 成功。

残る未自動検証項目は、実ブラウザー相当の 320 / 720 / 1280 CSS px、keyboard、focus、contrast、dialog focus return、mobile drawer backdrop / Escape の視覚・操作確認である。現在の公開ツールでは新規ブラウザー操作サブエージェントを起動できないため、コード上で確認できる不整合は閉じ、change record は引き続き `Approved` のままとする。

## Remaining implementation tasks - 2026-08-31

完了前に次のサブタスク単位で残件を閉じる。

1. **文書整合監査**: `docs/admin-ui-design.md`、`docs/collector-design.md`、`docs/admin-ui-implementation-audit.md`、本変更セットを照合し、旧 Collector UI、旧 `CollectionTasks.razor`、ローカル状態ストア、予想票編集、ジョブ親子表現の古い記述を是正する。完了条件は、現在の実装方針と矛盾する記述が残らず、変更セットに修正箇所が記録されること。
2. **UI 共通化監査**: API 管理画面の主要 route と共有部品を確認し、旧 `.card` / `.toolbar` / `.btn` / `.input` 依存を Fluent UI component または共通 `data-list` / `detail-section` / `filter-card` パターンへ置き換える。完了条件は、主要画面で一覧・詳細・編集・dialog の余白、行密度、状態バッジ、戻る導線が同じ文法になること。
3. **API / ジョブ状態モデル監査**: `/api/admin/jobs`、一時停止・再開、リラン、再取得、生成関係・集約依存関係、Collector service-only 化を変更セットと突き合わせる。完了条件は、集約対象子ジョブ単独リランが露出せず、生成元から生成先を辿れ、Collector が計画済みジョブ実行サービスとしてのみ動作することをテストで固定すること。
4. **検証・コミット**: 上記完了後、`dotnet build HorseRacingPrediction.sln --no-restore -v:minimal`、関連テスト、`dotnet test HorseRacingPrediction.sln --no-build -v:minimal`、`git diff --check` を実行し、検証済みチェックポイントとして目的別にコミットする。途中で大きな差分が残る場合は、文書、API/状態モデル、UI、テストの単位で分ける。

## Implementation update - 2026-08-31 checkpoint commit preparation

残タスクをサブタスク化し、チェックポイントコミット前の追加監査で見つかった不整合を修正した。

- `docs/admin-ui-design.md` の旧 `CollectionTasks.razor` / Collector UI 前提の記述を、API 管理画面 `/jobs` を正本とし Collector は service-only とする現在方針へ更新した。
- API 管理画面実装に残っていた旧 `.card` / `.toolbar` / `.btn` / `.input` クラス依存を、共通 `detail-card` / `action-row` / `app-button` / `text-input` パターンへ機械的に置き換えた。既存の表示構造は維持しつつ、旧命名へ依存しない状態にした。
- 変更セットの残タスクを、文書整合監査、UI 共通化監査、API / ジョブ状態モデル監査、検証・コミットの4単位へ整理した。

追加検証:

- `rg -n 'class="(toolbar|btn|card|input)|\.(toolbar|btn|card|input)' src/HorseRacingPrediction.Api/Web/Components src/HorseRacingPrediction.Api/wwwroot/app.css`: 該当なし。
- `dotnet build HorseRacingPrediction.sln --no-restore -v:minimal`: 成功、警告 0。
- `dotnet test tests/HorseRacingPrediction.Api.Tests/HorseRacingPrediction.Api.Tests.csproj --no-build -v:minimal`: 成功、88 件。
- `dotnet test tests/HorseRacingPrediction.Collector.Tests/HorseRacingPrediction.Collector.Tests.csproj --no-build -v:minimal`: 成功、92 件。
- `dotnet test HorseRacingPrediction.sln --no-build -v:minimal`: 成功、全 569 件。
- `git diff --check`: 成功。`src/HorseRacingPrediction.Api/wwwroot/app.css` の CRLF 変換警告のみ。

## Subtask dispatch - 2026-08-31

実装完了判定前の残タスクを、競合を避けるため次の単位へ分割して投げる。各サブタスクは、修正した場合に本 change record へ変更箇所、検証結果、残課題を追記し、検証済みの一目的単位でコミットする。

1. **API / ジョブ状態モデル最終監査**
   - 担当候補: `api_route_job_final_audit`
   - 対象: `src/HorseRacingPrediction.Api/Security`、`src/HorseRacingPrediction.Api/Web/Components/Pages/*Job*`、`src/HorseRacingPrediction.CollectionOperations/Scheduling`、関連 API / Collector テスト。
   - 完了条件: 管理 UI route が API key middleware で阻害されないこと、旧 `/collection-tasks` が canonical `/jobs` へ誘導されること、停止・再開・リラン・再取得・生成関係・集約依存関係が本 change record と一致すること、Collector が Web / ローカル状態ストアを持たない service-only 境界であることを確認またはテストで固定する。
   - 競合注意: 現時点で `ApiKeyApplicationBuilderExtensions.cs` と `AdminAuthenticationTests.cs` に未コミット差分があるため、先に `git diff` を確認し、同じ箇所を変更する場合は root 側の差分を前提にする。
2. **UI 受け入れ・レイアウト監査**
   - 担当候補: `ui_acceptance_final_audit`
   - 対象: `src/HorseRacingPrediction.Api/Web/Components`、`src/HorseRacingPrediction.Api/wwwroot/app.css`、可能ならローカル API の実ブラウザー確認。
   - 完了条件: Fluent UI / 共通 wrapper / 共通 CSS 文法に反する旧 `.card` / `.toolbar` / `.btn` / `.input` 依存が主要画面に残らないこと、一覧・詳細・編集・モーダル・関連リスト・バッジ・戻る導線が統一されていること、絵文字とブラウザー標準 `alert()` / `confirm()` を使用していないこと、代表画面で横スクロール崩れがないことを確認する。
   - 競合注意: API route 修正の検証でローカル API が起動している場合があるため、bin / obj ロックを避け、必要なら一時出力先 build を使う。
3. **文書整合・正本同期監査**
   - 担当候補: `docs_acceptance_final_sync`
   - 対象: `docs/admin-ui-design.md`、`docs/design-guidelines.md`、`docs/collector-design.md`、`docs/admin-ui-implementation-audit.md`、本 change record。
   - 完了条件: 旧 Collector UI、旧 `CollectionTasks.razor`、独立した収集対象日画面、予想票編集、曖昧な親子ジョブ表現など、現在方針と矛盾する記述が残らないこと。重複するデザイン規則は `docs/design-guidelines.md` を正本に集約し、本 change record に変更箇所を記録する。
   - 競合注意: 実装修正が発生した場合は、文書だけで完結するコミットと混ぜず、実装担当の検証結果を待って同期する。
4. **最終検証・コミット整理**
   - 担当候補: `final_integration_verification`
   - 対象: すべてのサブタスク結果と作業ツリー。
   - 完了条件: 生成物を除去し、`dotnet build HorseRacingPrediction.sln --no-restore -v:minimal`、関連テスト、`dotnet test HorseRacingPrediction.sln --no-build -v:minimal`、`git diff --check`、`git status --short` を通す。問題がなければ目的別にコミットし、本 change record の Status を `Implemented` にできるか判断する。
   - 競合注意: SQLite `eventstore.db-shm` / `eventstore.db-wal` など実行時生成物は、サーバー停止後にワークスペース内の該当ファイルだけを確認して削除する。

## Implementation update - 2026-08-31 admin UI route access pass

API 管理画面の実ブラウザー確認で、API key middleware が一部の管理 UI route と Fluent UI 静的資産を匿名アクセス対象として扱わず、cookie 認証画面へ到達する前に redirect loop を起こし得る不整合を修正した。

- `src/HorseRacingPrediction.Api/Security/ApiKeyApplicationBuilderExtensions.cs` の管理 UI route 免除対象へ `/owners`、`/jobs`、`/collection-tasks`、`/acquisition-statuses` を追加した。
- Fluent UI Blazor の静的資産を配信する `/_content` を API key middleware の匿名アクセス対象へ追加した。
- `tests/HorseRacingPrediction.Api.Tests/AdminAuthenticationTests.cs` に、上記 route / 静的資産が API key 未指定でも middleware によって `401 Unauthorized` にされないことを固定する回帰テストを追加した。
- ローカル API 起動時に生成された `src/HorseRacingPrediction.Api/eventstore.db-shm` と `src/HorseRacingPrediction.Api/eventstore.db-wal` は実行時生成物であり、コミット対象に含めない。

追加検証:

- `dotnet build HorseRacingPrediction.sln --no-restore -v:minimal`: 成功、警告 0。
- `dotnet test tests/HorseRacingPrediction.Api.Tests/HorseRacingPrediction.Api.Tests.csproj --no-build -v:minimal`: 成功、93 件。
- `dotnet test tests/HorseRacingPrediction.Collector.Tests/HorseRacingPrediction.Collector.Tests.csproj --no-build -v:minimal`: 成功、92 件。
- `dotnet test HorseRacingPrediction.sln --no-build -v:minimal`: 成功、全 574 件。

## Implementation update - 2026-09-01 UI routing and owner migration pass

追加確認で、一覧ページの query string 同期と馬主一覧の既存 SQLite schema に関する不整合を修正した。

- `src/HorseRacingPrediction.Api/Web/Components/Pages/Races.razor`、`Horses.razor`、`Jockeys.razor`、`Trainers.razor`、`Owners.razor`、`Jobs.razor`、`Predictions.razor` の `UpdateUrl` に同一 URL では `NavigateTo(..., replace: true)` を呼ばないガードを追加した。初期表示時に query string を正規化するだけで同一 route へ自己 redirect し続けるリスクを防ぐ。
- `src/HorseRacingPrediction.Infrastructure/Persistence/Migrations/20260830090000_AddOwnerDisplayName.cs` に EF Core migration metadata を追加し、`EventStoreDbContextModelSnapshot.cs` へ `OwnerAliasMappings.IsDisplayName` を反映した。既存 DB で `OwnerAliasMappings` は存在するが `IsDisplayName` 列がない状態でも、起動時 migration で列が追加される。
- `tests/HorseRacingPrediction.Infrastructure.Tests/SqliteDbContextProviderTests.cs` を更新し、`AddOwnerDisplayName` migration が新規作成 DB と既存 `EnsureCreated` DB の双方で migration history に記録されることを固定した。
- ローカル既存 DB に対して API 起動時に `20260830090000_AddOwnerDisplayName` が適用され、`/api/owners` と `/owners` が 200 を返すことを HTTP で確認した。
- ChatGPT の in-app browser は安定性問題があるため使用せず、今回の最終確認は HTTP と静的監査で行った。今後ブラウザー実機確認が必要な場合は、通常プロファイルではなく一時プロファイル / InPrivate の外部ブラウザーで行う。

追加検証:

- `Invoke-WebRequest` による cookie 認証後の主要 UI route 確認: `/races?from=2026-08-01&to=2026-08-31&page=1`、`/jobs`、`/owners`、`/acquisition-statuses`、`/horses`、`/jockeys`、`/trainers`、`/predictions` はすべて 200、共通エラー表示なし。
- `rg -n -e 'confirm\(' -e 'alert\(' src/HorseRacingPrediction.Api/Web docs/changes/20260830_fluent-ui-design-system docs/design-guidelines.md`: 実装呼び出しは該当なし。change record の禁止事項説明のみ該当。
- `rg -n -P 'href="(?!/|#|@|mailto:|https?:)' src/HorseRacingPrediction.Api/Web/Components/Pages`: 該当なし。
- `rg -n 'class="(toolbar|btn|card|input)|\.(toolbar|btn|card|input)' src/HorseRacingPrediction.Api/Web/Components src/HorseRacingPrediction.Api/wwwroot/app.css`: 該当なし。
- `dotnet build HorseRacingPrediction.sln --no-restore -v:minimal`: 成功、警告 0。
- `dotnet test tests/HorseRacingPrediction.Api.Tests/HorseRacingPrediction.Api.Tests.csproj --no-build -v:minimal`: 成功、93 件。
- `dotnet test tests/HorseRacingPrediction.Infrastructure.Tests/HorseRacingPrediction.Infrastructure.Tests.csproj --no-build -v:minimal`: 成功、10 件。
- `dotnet test tests/HorseRacingPrediction.Collector.Tests/HorseRacingPrediction.Collector.Tests.csproj --no-build -v:minimal`: 成功、92 件。
- `dotnet test HorseRacingPrediction.sln --no-build -v:minimal`: 成功、全 574 件。

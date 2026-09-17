# 主体識別エラーを分類し安全に自動復旧する

- Status: Approved
- Owner: Main
- Created: 2026-09-18
- Updated: 2026-09-18

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Complete | 名前正規化、誤参照抑止、非該当終端、revision単位の冪等復旧を実装した。 |
| Verification | Complete | parser、handler、store、recovery、UI guidance、API/Collector回帰を通過した。 |
| Deployment/operation | Not started | デプロイ後に既存245件を分類し、安全対象だけを一度再投入して解消結果を確認する。 |

## Context

2026-09-18 00:26 JST の本番 `/jobs` には要対応が245件あり、内訳は競走馬
`SubjectNotIdentified` 232件、調教師 `SubjectNotIdentified` 32件、騎手
`JraCollectionException` 3件、騎手 `SubjectNotIdentified` 1件だった。画面上の要対応総数はResource単位、障害群は
active failure notificationの分類単位で集計されるため、群件数の単純和は要対応総数と一致しない。

代表例を確認すると、同じエラーコードの中に次の異なる原因が混在していた。

- 競走馬プロフィールの父・母欄から `パネットーネ 産駒` 等の説明文を馬名として派生登録している。
- 公式プロフィール見出しの `マル外`（抽出表示では `マルガイ`）、`マル地`（同 `マルチ`）等の登録区分表示を馬名の一部として比較し、正しいURLでも不一致にしている。
- 同名馬を名前だけでは一意に決められないもの、現行の公開検索に存在しない種牡馬・繁殖牝馬を探索しているものがある。
- 調教師名 `尾形 和幸（美浦）` のように所属表示を含む名前を公開名簿の氏名と完全一致させている。名前欠落の旧taskも4件確認した。
- 騎手 `小谷 哲平` のようなJRA公開名簿の対象外主体を、JRAプロフィール必須対象としている。
- 騎手プロフィールの見出し未検出が3件あり、最大18試行まで同じ構造エラーを繰り返している。

このため、全件一括の手動再取得または無条件の自動再試行は、同じ失敗とジョブ流入を増やす。生成元を修正し、
安全に補正できる既存対象だけを一度復旧し、対象外と曖昧対象を別の終端へ分類する必要がある。

## Goals

- JRAの表示装飾や所属表記を主体名から分離し、同一人物・同一馬の比較に使える正規名を作る。
- 血統欄の説明文を新しい競走馬Resourceとして登録しない。
- JRA名簿対象外、検索結果なし、複数候補、ページ構造エラーを区別する。
- コード修正で解消可能と証明できる既存失敗だけを、新しいcollection revisionで冪等に一度自動復旧する。
- 自動で同定できない対象は誤ったプロフィールへ結び付けず、理由付きの要対応または対象外終端にする。
- `/jobs` の推奨対応を実際の分類に合わせ、再取得しても直らない障害へ一括再取得を勧めない。

## Non-goals

- 名前の類似度だけによる主体の自動統合は行わない。
- 複数候補から生年月日、source identity、発見元リンク等の一意な根拠なしに選択しない。
- JRA以外のプロフィール提供元を今回追加しない。
- HTML構造エラーを無制限または定期的に再試行しない。
- 既存の競走馬履歴lane変更案は本変更に混在させない。
- ブラウザー待機方式、NetworkIdle、Lambda間ブラウザーキャッシュ共有は変更しない。

## Documentation updates

- `docs/22-collector-design.md`: 無条件の自動再試行禁止を維持しつつ、revisionで修正済みと証明できる失敗だけを一度復旧する例外と、対象外終端を追記する。
- `docs/26-collection-platform-design.md`: 主体名正規化、対象外、構造エラー、自動復旧の不変条件を追記する。
- 本change record: 本番調査、分類、設計、受け入れ条件、実装・運用証拠を保持する。

## Technical impact

### 1. 発見時の正規化

- RaceCard由来の調教師名は末尾の所属表示（全角・半角括弧）を主体属性から分離する。表示用所属は必要なら別属性に保持する。
- 馬プロフィール見出しの登録区分表示は、JRAが提示する既知の装飾トークンとして分離する。正規化前後の名前と
  source identityを照合し、単なる部分一致にはしない。
- 父・母の関連発見は、プロフィールの構造化セルから主体名と実リンクを取得できる場合だけ作る。`～産駒`、見出し、
  空値、リンクのない説明文字列からHorse taskを作らない。
- 既存の深度3、ancestor cycle防止、Background laneは維持する。

### 2. 結果分類

- `CorrectableNormalization`: 既知装飾を除いた正規名とsource identity等で同一性を証明できる。
- `ProviderNotApplicable`: 地方・海外等、JRA公開名簿の収録対象外であることを発見元の構造化情報または名簿応答で確認できる。
- `AmbiguousIdentity`: 複数候補または一意根拠不足。要対応に残し、自動選択しない。
- `InvalidDiscoveryReference`: `～産駒`、空名、見出し等の誤生成。元Resourceを無効化し、再登録を止める。
- `StructuralPageFailure`: 期待見出しや必須構造の欠落。同じrevisionではterminalとし、全体停止規則と要対応表示を維持する。
- `TransientAccessFailure`: timeout、明示的な一時HTTP/アクセス制限だけを既存backoffで再試行する。

`ProviderNotApplicable` は収集不能ではなく収集適用範囲外として成功と区別できる終端状態にし、failure notificationを
作らない。業務主体データは保持し、JRAプロフィールがないことだけを記録する。

### 3. 自動復旧planner

- activeな主体識別failureを読み、失敗taskのrevision、failure kind、属性、候補、URL、発見元を再評価する。
- 現revisionより新しい修正revisionがあり、同一性または誤生成が決定的に判定できるものだけを対象にする。
- `(failure notification, target revision)` を冪等キーにし、自動Recoveryは最大1回。既存active taskがあれば再利用する。
- `CorrectableNormalization` はcanonical主体を登録・解決してRecoveryする。旧Resourceと別IDになる場合は、既存の
  同一性根拠付きmerge/redirect境界を通し、参照を切り替えてから旧taskを抑止する。
- `InvalidDiscoveryReference` と `ProviderNotApplicable` は再取得せず理由付きで閉じる。
- `AmbiguousIdentity`、名前欠落、`StructuralPageFailure` は自動Recovery対象外とする。
- planner自体の部分失敗は対象ごとに記録し、成功済み対象を巻き戻さず、次回も同じ冪等キーで重複作成しない。

### 4. 運用表示

- 障害群をerror codeだけでなくfailure kindで分け、原因と推奨対応を表示する。
- 自動復旧済み、対象外として終了、手動確認待ち、構造修正待ちの件数を確認できるようにする。
- 構造修正待ちには通常再取得を主操作として出さず、revision更新後の復旧対象であることを示す。

## Decisions

1. 生のエラーコード単位ではなく、決定的な原因分類単位で復旧可否を決める。
2. 自動復旧は「時間が経てば直る」という推測ではなく、「新revisionが既知の原因を修正した」という証拠で解禁する。
3. JRA名簿の対象外は要対応へ蓄積せず、プロフィール非該当の終端として保持する。
4. 曖昧な同名主体は安全性を優先して自動選択しない。
5. ページ構造エラーは同revision内で繰り返さず、修正revision配備後にだけ再実行する。

### Rejected alternatives

- **245件を一括再取得:** 決定的な入力・parser不具合を同じ条件で再現するため不採用。
- **`SubjectNotIdentified`を一定間隔で自動再試行:** 対象外と曖昧対象を永久に回し続けるため不採用。
- **名前の部分一致で成功扱い:** 同名馬・同姓同名を誤結合するため不採用。
- **すべて手動補正:** 件数が収集拡大に比例して増え、同じ既知原因へ運用者判断を繰り返すため不採用。

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | `マル外 アジアエクスプレス`等の表示装飾付き見出しを、source identityを維持したまま正規名の同一馬として取得できる。 | T1,T3 | parser/handler fixtures | Verified |
| AC2 | `尾形 和幸（美浦）`等は所属を氏名から分離し、正規名で調教師名簿を照合できる。 | T1,T3 | race-card/navigation tests | Verified |
| AC3 | `パネットーネ 産駒`等の説明文字列、空名、リンクのない血統表示から新しいHorse taskが作られない。 | T1,T3 | subject discovery tests | Verified |
| AC4 | JRA対象外と確認できる騎手等は業務データを残したままプロフィール非該当で終端し、要対応と再試行を増やさない。 | T2,T3 | handler/store integration tests | Verified |
| AC5 | 複数候補、同一性根拠不足、名前欠落は自動選択・自動Recoveryされず、具体的理由で要対応に残る。 | T2,T3 | planner safety tests | Verified |
| AC6 | 見出し欠落等の構造エラーは同revisionで繰り返されず、新revision配備までは1件の要対応として残る。 | T2,T3 | retry/revision integration tests | Verified |
| AC7 | 新revisionが既知原因を修正した場合だけ、対象failureごとに最大1件のRecoveryが作成または既存task再利用される。 | T2,T3 | idempotency/concurrency tests | Verified |
| AC8 | plannerの中断・再起動・重複起動・部分失敗でも、成功済み補正とRecovery taskが重複しない。 | T2,T3 | restart/concurrency tests | Verified |
| AC9 | `/jobs` で原因分類、件数、実行した自動処置、残る手動対応を確認でき、直らない群に一括再取得を勧めない。 | T4 | component/browser tests | Verified |
| AC10 | 既存のtimeout・アクセス制限backoff、全体停止、安全な手動Recovery、identity厳密照合は維持される。 | T3,T5 | regression/diff review | Verified |
| AC11 | 本番既存245件をpreviewし、分類別件数と実行予定を確認してから安全対象だけをapplyできる。 | T5 | production preview/apply evidence | Not started |
| AC12 | apply後、補正可能群と対象外群は要対応から減り、曖昧・名前欠落・構造修正待ちだけが理由付きで残る。 | T5 | production `/jobs` before/after | Not started |

## Delivery plan

1. 名前・血統参照の正規化と誤生成防止を実装する。
2. 対象外・曖昧・構造・一時障害の結果モデルとstore遷移を実装する。
3. revision-gatedで冪等なpreview/apply plannerを実装する。
4. `/jobs` の分類・推奨対応を更新する。
5. focused/full gatesとセルフレビュー後にデプロイし、previewから安全対象だけをapplyする。

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | 主体名正規化と血統参照の誤生成防止を実装する。 | Main | Lead tier | Approval | Scraping parser、Collector discovery | parser/handler tests | AC1-AC3のfixtures | Verified |
| T2 | 結果分類、revision gate、冪等plannerを実装する。 | Main | Lead tier | T1 | Collection operations、API repair | integration tests | AC4-AC8の証拠 | Verified |
| T3 | 失敗・再起動・並行実行を含む回帰テストを追加する。 | Main | Lead/review tier | T1,T2 | tests | focused/full tests | AC1-AC8,AC10 | Verified |
| T4 | `/jobs` の原因別表示と操作制御を実装する。 | Main | Lead tier | T2 | API Web/API contracts | component/browser tests | AC9 | Verified |
| T5 | 正本同期、セルフレビュー、CI、デプロイ、preview/apply、本番確認を行う。 | Main | Lead/review tier | T1-T4 | docs、deployment/operation | repository/production gates | AC10-AC12 | Dependent |

## Review gates

- **Design and task-split review — 2026-09-18, reviewer: Main.** 本番4障害群、代表対象、既存subject handler、
  repair preview/executeを照合した。入力不具合、対象外、曖昧、構造エラーを同じretry policyへ入れない設計とし、
  AC1-AC12を生成、解析、状態遷移、冪等復旧、UI、本番applyへ追跡した。identityと既存データ補正が密接に関係するためMainが直列で担当する。
- **Pre-implementation review — 2026-09-18, reviewer: Main.** ユーザー承認と優先順位変更の同時実施を確認。T1をRunnable、T2-T5を依存順にDependentとした。誤生成抑止を先に接続し、結果分類・冪等復旧・UI・本番applyの順で進める。主体同一性を証明できない場合は自動補正せずエスカレーションする。
- **Checkpoint review — 2026-09-18, reviewer: Main.** revision 2への昇格、既知装飾のcanonical化、`～産駒`抑止、JRA名簿外の非該当終端、曖昧対象の安全停止を確認した。復旧はfailure/target revisionの安定キーで冪等化し、個別失敗を隔離してAPI起動と後続対象を継続する。
- **Final review:** pending.

## Verification record

- 2026-09-18: 本番 `/jobs` で要対応245件と4障害群を確認した。
- 2026-09-18: 競走馬群で `～産駒` の誤生成、登録区分付き取得名、同名複数候補、検索結果なしを確認した。
- 2026-09-18: 調教師群で所属付き対象名と名前欠落旧task、騎手群でJRA名簿対象外と最大18試行の見出し欠落を確認した。
- 2026-09-18: 現行handlerは主体同定失敗を `ResourceNotFound/SubjectNotIdentified` にし、既存repairはactive failureを
  previewしてRecoveryを作れるが、修正revisionによる自動適格判定と対象外終端を持たないことを確認した。
- 2026-09-18: `DiscoverHorseReferencesAsync` が父母フィールド文字列をリンク根拠なしでHorseとして登録し、
  `SubjectProfilePageParser` がプロフィール見出しの登録区分を除去しないことを確認した。
- 2026-09-18: parser、subject handler、store、revision-gated recovery、UI guidanceのfocused testsを追加し、API 228件、Collector 247件、CI相当Releaseテストをすべて通過した。
- 2026-09-18: セルフレビューで個別復旧失敗がAPI起動全体を止める経路を検出し、対象単位の例外隔離、失敗件数記録、後続継続を追加した。

## Deviations and follow-up

- 現時点では調査・設計のみで、再取得、failure close、データ統合、production code変更は行っていない。
- 競走馬履歴laneの提案は `20260917_bound-realtime-discovery` で独立して管理する。

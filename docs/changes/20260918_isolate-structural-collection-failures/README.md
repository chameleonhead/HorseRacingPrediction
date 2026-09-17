# プロフィール画面を確実に待機し、既存エラーを安全に復旧する

- Status: Implemented
- Owner: Main
- Created: 2026-09-18
- Updated: 2026-09-18

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Verified | 意味的な画面成立待ち、型付きfailure impact、対象単位隔離、revision 3 recoveryを実装した。 |
| Verification | Verified | build、format、CI、focused/full tests、JRA実サイトE2E、本番画面を確認した。 |
| Deployment/operation | Verified | run 35285889932でAPI/Lambdaを配備し、旧構造障害群5件の消滅とpipeline継続を確認した。 |

## Context

2026-09-18 07:40 JST、本番の調教師プロフィール収集で「調教師情報の見出しを確認できません。」という
`JraCollectionException` が発生し、失敗対象1件だけで収集全体が安全停止した。対象は
`松永 幹夫（栗東）` で、HorseプロフィールからBackground laneへ発見された調教師だった。同じ障害群には別の
調教師1件も存在する。

追加調査では、JRA上に対象の調教師プロフィールと正しい見出しが実在することを確認した。クリック前の一覧画面と
クリック後のプロフィール画面が同じURLを使う一方、現在のブラウザー待機はクリック後500msの固定待機と、本文が
空でないこと等の汎用条件だけで完了する。一覧画面の本文が既に存在するため、プロフィール画面の描画前に待機を
通過して一覧画面をsnapshotし、parserがプロフィール見出し欠落と判断する競合が直接原因である。JRAのデータ欠落や
恒久的な画面変更ではない。

主体プロフィールhandlerは識別不能だけをdomain結果へ変換する一方、見出し欠落の
`JraCollectionException`をworkerまで送出する。共通例外classifierはこれを`PermanentFailure`へ変換し、storeは
`ResourceNotFound`以外のterminal failureをすべて全体停止にする。このため、既知で対象単位に閉じる構造エラーと、
データ整合性を守るため即時停止すべき未知の恒久障害を区別できない。

## Goals

- 主体プロフィールの見出し欠落を、同revisionでは再試行しない対象単位の構造エラーとして記録する。
- 対象を要対応に残しつつ、無関係な収集ジョブを停止しない。
- 未分類の恒久障害、内容検証失敗等に対する既存の全体安全停止を維持する。
- handlerからstoreまで、停止影響を文字列やerror codeの推測ではなく型付き契約で伝える。
- URLや通信静止ではなく、期待するプロフィール見出しと対象名が表示されたことを画面成立条件にする。
- 現在の要対応群を原因別にpreviewし、安全性を証明できる対象だけを自動復旧する。

## Non-goals

- 見出しがないページを成功扱いしない。
- 名前の部分一致や別ページからの推測で調教師を自動同定しない。
- すべての`UnexpectedPage`を一律に非停止へ変更しない。
- timeout、アクセス制限、HTTP障害のbackoffを変更しない。
- 既存の要対応群を無条件に一括再取得しない。
- 同名候補が複数ある競走馬を名前だけで自動選択しない。
- JRAの検索対象外の海外馬・種牡馬等を、現役競走馬プロフィールとして成功扱いしない。

## Documentation updates

- `docs/01-lambda-collector-architecture.md`: 承認候補として、画面固有の成立待ち、対象単位の隔離、安全なrevision recoveryの境界を追記する。
- `docs/26-collection-platform-design.md`: 同一URL遷移の画面成立条件、構造エラーの隔離、要対応群の復旧分類、未分類障害の既定安全停止を追記する。
- 本change record: 本番事象、設計、受け入れ条件、実装・配備証跡の正本とする。

## Technical impact

1. 主体リンクのクリック後は、調教師・騎手・競走馬ごとの期待見出しと期待対象名が可視になるまで、取消可能な
   上限時間内で待つ。同一URL、warm cache、cold cacheのいずれでも成立し、`NetworkIdle`や長い固定sleepには依存しない。
2. `CollectionAttemptCompletion` に型付きのfailure impactを追加する。既定値は現在と同じ安全停止とし、明示的な
   `Isolated`だけが対象単位で終端する。既存callerや欠落payloadが非停止へ倒れない後方互換を保つ。
3. Collectorから内部complete APIまでfailure impactを伝搬し、storeがfailure notificationを作る際の
   `pausePipeline`判定に使う。error codeや日本語messageによる判定は行わない。
4. `JraSubjectProfileCollectionHandler` はプロフィール探索・検証中の`JraCollectionException`を
   `UnexpectedPage / StructuralPageFailure / Isolated`へ変換する。taskはFailed、stateはFailed、通知はOpenとし、
   active taskを外して同revisionの自動再試行を行わない。
5. `PermanentFailure`、`ParseFailure`、`ValidationFailure`、明示されていない`UnexpectedPage`は従来どおり
   pipelineを停止する。
6. 復旧plannerは本番要対応を次のように扱う。
   - 見出し欠落5件（騎手3、調教師2）: 修正版revisionで一度だけ再取得する。再失敗しても対象単位に隔離する。
   - 名前欠落4件（調教師）: 元発見情報から一意に補完できる場合だけ修復する。できなければ再試行せず要対応に残す。
   - 競走馬同定不能112件: 候補なし、複数候補、提供元対象外、誤生成参照へpreview分類する。一意な公式識別根拠が
     得られたものだけ修復し、複数候補を自動選択しない。提供元対象外・誤生成参照は理由付き終端とする。
   applyはfailure group、target、revisionの冪等キーを使い、重複taskを作らず既存の高い優先度を維持する。

## Decisions

1. `UnexpectedPage`全体の停止規則は変更せず、完了単位の明示的impactを採用する。
2. `ResourceNotFound`へ偽装しない。公開対象がないのではなく構造を検証できないため、Failed/要対応を維持する。
3. message/error code allowlistは採用しない。文言変更で安全性が変わるためである。
4. revision recovery以外の自動再試行は追加しない。
5. 汎用的な「本文が存在する」待機は補助条件に留め、各navigatorが遷移先の意味的な成立条件を指定する。
6. 復旧はpreviewとapplyを分離し、曖昧な対象を安全側に残す。要対応件数をゼロにすることより誤結合防止を優先する。

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | 同じURL内で一覧からプロフィールへ遷移しても、期待する見出しと対象名が表示されるまでsnapshotせず、cold/warm条件の双方で正しいプロフィールを取得する。 | T1 | browser/navigation integration tests | Verified |
| AC2 | AC1の待機は取消・上限時間を持ち、`NetworkIdle`または長い固定sleepを正常系の成立条件にしない。 | T1 | timeout/cancellation and timing tests | Verified |
| AC3 | 調教師・騎手・競走馬プロフィールの見出し欠落は1件のFailed/要対応として残り、同revisionで再試行されない。 | T2,T3 | handler/store integration tests | Verified |
| AC4 | AC3の失敗後もpipelineは稼働状態を保ち、別のdue taskを取得・実行できる。 | T2,T3 | store/worker integration test | Verified |
| AC5 | 未分類の恒久障害と内容検証失敗は従来どおりpipelineを停止し、alertを1回発行する。 | T3 | pipeline alert regression tests | Verified |
| AC6 | failure impactはCollector→complete API→storeへ型付きで伝搬し、欠落時は安全停止側へ倒れる。 | T2,T3 | serialization/API integration tests | Verified |
| AC7 | 復旧previewが現在の全要対応を、構造待機5件、名前欠落4件、競走馬の候補なし・複数候補・提供元対象外・誤生成参照に分類し、各対象の処置理由を表示する。 | T4 | planner tests and production preview evidence | Verified |
| AC8 | applyは構造待機5件と一意性を証明できる対象だけを一度処置し、名前欠落や複数候補を推測で再試行・結合せず、既存の高い優先度とlaneを維持する。 | T4 | idempotency/priority tests and production apply evidence | Verified |
| AC9 | `/jobs`では残存する要対応理由を確認でき、全体収集は処理中または待機消化中で、復旧による重複taskがない。 | T5 | component test and production browser verification | Verified |
| AC10 | 配備後、見出し欠落5件を修正版revisionで復旧し、同じ構造エラーが再発しても他の収集が停止しない。 | T5 | production `/jobs` before/after evidence | Verified |

## Delivery plan

1. 主体プロフィール画面固有の成立待ちを実装する。
2. 型付きfailure impactとtransport伝搬、構造エラー隔離を実装する。
3. store・alert・handler・API・browser境界の回帰テストを追加する。
4. 全要対応を安全に分類する復旧preview/applyを拡張する。
5. CI、デプロイ後に安全な対象をrevision recoveryし、pipeline継続と残存理由を本番確認する。

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | 同一URL遷移でも成立する主体プロフィール固有の待機を実装する。 | Main | Lead tier | Approval | browser abstraction、JRA navigator、tests | navigation/timeout tests | AC1,AC2 | Verified |
| T2 | failure impact契約と主体構造エラー分類を実装する。 | Main | Lead tier | T1 | CollectionOperations、Collector、API transport | focused contract/handler tests | AC3,AC6 | Verified |
| T3 | store停止判定と安全停止回帰を実装・検証する。 | Main | Lead tier | T2 | CollectionPlatformStore、API tests | store/alert integration tests | AC4-AC6 | Verified |
| T4 | 要対応全件のpreview分類と安全な冪等applyを実装・検証する。 | Main | Lead tier | T1-T3 | recovery planner、operation API/tests | classification/idempotency/priority tests | AC7,AC8 | Verified |
| T5 | UI回帰、CI、配備、対象revision recovery、本番継続確認を行う。 | Main | Lead/review tier | T1-T4 | UI tests、docs、production operation | component/full tests、deployment runs、production evidence | AC9,AC10 | Verified |

## Review gates

- **Design and task-split review — 2026-09-18, reviewer: Main.** 本番停止理由、JRA上の実プロフィール、同一URL遷移、browser待機、parser、handler例外境界、worker classifier、store停止判定、全要対応群を追跡した。意味的な画面成立待ち、明示的impact、安全側の復旧分類によりAC1-AC10と既存安全停止を両立する。browser・契約・store・本番復旧が直列依存するためMainが担当する。
- **Pre-implementation review — 2026-09-18, reviewer: Main.** ユーザーがAC1-AC10を承認した。T1をRunnable、T2-T5を依存順のDependentとした。画面待機、型付き契約、store、復旧planner、配備が共有契約と本番状態へ直列に影響するため、Lead tierのMainが書込ownerを保持する。委譲は調整コストと共有範囲が利益を上回るため行わない。
- **Checkpoint review — 2026-09-18, reviewer: Main.** T1-T3を実装し、固定500ms待機を削除して意味的な成立条件へ置換した。隔離impactは既定を安全停止とし、handler→HTTP→storeを型付きで接続した。Collector 249件、API 231件が合格し、調教師の実サイトプロフィール取得も合格した。Scraping全体278件中、今回と無関係な実サイト馬主欠落2件のみ失敗した。T4はrevision 3、旧構造エラー選別、冪等request、元lane/priority維持まで接続済みで、本番preview/apply証跡待ち。
- **Final review — 2026-09-18, reviewer: Main.** AC1-AC10を実装・テスト・本番経路へ追跡した。旧騎手構造障害群`02D78CBC10B10F67`と旧調教師構造障害群`FBF7553D84F38EFB`はいずれも本番で「見つかりません（再取得が開始され、要対応ではなくなった可能性）」となり、一覧から消滅した。pipelineは稼働中で別taskを処理している。残存群は競走馬同定不能と調教師名前欠落だけで、曖昧対象を自動結合していない。全taskはVerified、承認範囲に未完了なし。委譲なし、rework 1回（不足using追加）、利用量・費用は取得不能。

## Verification record

- 2026-09-18: 本番で停止理由 `Task=55ba65be-5ea2-4818-bfc1-a5c62d0db504; Error=JraCollectionException; 調教師情報の見出しを確認できません。` を確認した。
- 2026-09-18: 障害群`FBF7553D84F38EFB`に調教師2件があり、最新対象は`松永 幹夫（栗東）`、Background/priority 40、試行1回、取得先未記録であることを確認した。
- 2026-09-18: handlerで構造例外が未捕捉、workerで`PermanentFailure`化、storeで`ResourceNotFound`以外のterminal failureが全体停止となる実行経路を確認した。
- 2026-09-18: JRAの現行調教師一覧から`松永 幹夫`を開き、同一URLの遷移後に正しい`調教師情報`見出しが表示されることを確認した。汎用待機が遷移前本文で早期成立し得るため、見出し欠落は画面変更ではなくsnapshot時機の競合と確定した。
- 2026-09-18 07:57 JST: 本番pipelineは稼働し、要対応98、処理中1、待機中4192、最近完了1570を確認した。障害群は競走馬同定不能112対象、調教師名前欠落4対象、騎手見出し欠落3対象、調教師見出し欠落2対象だった。要対応集計と障害群内対象数は集計単位が異なるため単純合計しない。
- 2026-09-18: 競走馬群には公開検索の候補なしと同名の複数候補が混在し、調教師名前欠落群は全4件で主体名がないことを確認した。盲目的な一括再試行では解消せず、分類previewと一意性根拠に基づく処置が必要である。
- 2026-09-18: `dotnet build HorseRacingPrediction.sln --no-restore` 成功（警告0、エラー0）。focused tests 6件、Collector tests 249件、API tests 231件、調教師実サイトE2E 1件が合格した。Scraping全体は276件合格、1件skip、既存の実サイト馬主名欠落2件が失敗し、今回変更経路とは独立している。
- 2026-09-18: GitHub Actions `app-ci` run 35285889847と`app-deploy` run 35285889932が成功し、API、Collector Lambda、remote stackのhealth checkが完了した。
- 2026-09-18 08:25 JST: `/jobs`で「すべて一時停止」操作が表示される稼働状態、処理中2、待機中4187、最近完了1576、要対応96を確認した。配備前の要対応98から2減少し、構造障害群5対象はすべて要対応一覧から消滅した。残存障害群は競走馬`SubjectNotIdentified` 113対象と調教師`SubjectNotIdentified` 4対象だけである。
- 2026-09-18: 最初の復旧依頼でpipeline再開と進行確認だけを「復旧」と報告し、根本原因と恒久対策案を同じ作業内で提示しなかった。運用再開を完了条件と誤認したworkflow gapとして整理し、`.codex/skills/production-incident-recovery/SKILL.md`を追加した。以後は暫定復旧、根本原因、対策設計、実装、配備、本番検証を別々に追跡する。

## Deviations and follow-up

- 承認前のためproduction codeは変更していない。workflow改善はユーザーから別途明示承認されたため、製品修正に先行して適用した。

# 構造エラーを対象単位に隔離して収集全体を継続する

- Status: Proposed
- Owner: Main
- Created: 2026-09-18
- Updated: 2026-09-18

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Not started | 承認後、完了通知へ明示的な影響範囲を追加し、主体プロフィールの構造エラーを隔離する。 |
| Verification | Not started | handler、transport、store、pipeline alertの回帰テストと本番確認が必要。 |
| Deployment/operation | Not started | 配備後に失敗対象をrevision recoveryし、他の収集が停止しないことを確認する。 |

## Context

2026-09-18 07:40 JST、本番の調教師プロフィール収集で「調教師情報の見出しを確認できません。」という
`JraCollectionException` が発生し、失敗対象1件だけで収集全体が安全停止した。対象は
`松永 幹夫（栗東）` で、HorseプロフィールからBackground laneへ発見された調教師だった。同じ障害群には別の
調教師1件も存在する。

主体プロフィールhandlerは識別不能だけをdomain結果へ変換する一方、見出し欠落の
`JraCollectionException`をworkerまで送出する。共通例外classifierはこれを`PermanentFailure`へ変換し、storeは
`ResourceNotFound`以外のterminal failureをすべて全体停止にする。このため、既知で対象単位に閉じる構造エラーと、
データ整合性を守るため即時停止すべき未知の恒久障害を区別できない。

## Goals

- 主体プロフィールの見出し欠落を、同revisionでは再試行しない対象単位の構造エラーとして記録する。
- 対象を要対応に残しつつ、無関係な収集ジョブを停止しない。
- 未分類の恒久障害、内容検証失敗等に対する既存の全体安全停止を維持する。
- handlerからstoreまで、停止影響を文字列やerror codeの推測ではなく型付き契約で伝える。

## Non-goals

- 見出しがないページを成功扱いしない。
- 名前の部分一致や別ページからの推測で調教師を自動同定しない。
- すべての`UnexpectedPage`を一律に非停止へ変更しない。
- timeout、アクセス制限、HTTP障害のbackoffを変更しない。
- 既存の要対応群を無条件に一括再取得しない。

## Documentation updates

- `docs/01-lambda-collector-architecture.md`: 承認候補として、対象単位に閉じることをhandlerが明示したterminal failureは通知を残して全体停止しない境界を追記する。
- `docs/26-collection-platform-design.md`: 構造エラーの隔離と、未分類障害の既定安全停止という不変条件を追記する。
- 本change record: 本番事象、設計、受け入れ条件、実装・配備証跡の正本とする。

## Technical impact

1. `CollectionAttemptCompletion` に型付きのfailure impactを追加する。既定値は現在と同じ安全停止とし、明示的な
   `Isolated`だけが対象単位で終端する。既存callerや欠落payloadが非停止へ倒れない後方互換を保つ。
2. Collectorから内部complete APIまでfailure impactを伝搬し、storeがfailure notificationを作る際の
   `pausePipeline`判定に使う。error codeや日本語messageによる判定は行わない。
3. `JraSubjectProfileCollectionHandler` はプロフィール探索・検証中の`JraCollectionException`を
   `UnexpectedPage / StructuralPageFailure / Isolated`へ変換する。taskはFailed、stateはFailed、通知はOpenとし、
   active taskを外して同revisionの自動再試行を行わない。
4. `PermanentFailure`、`ParseFailure`、`ValidationFailure`、明示されていない`UnexpectedPage`は従来どおり
   pipelineを停止する。

## Decisions

1. `UnexpectedPage`全体の停止規則は変更せず、完了単位の明示的impactを採用する。
2. `ResourceNotFound`へ偽装しない。公開対象がないのではなく構造を検証できないため、Failed/要対応を維持する。
3. message/error code allowlistは採用しない。文言変更で安全性が変わるためである。
4. revision recovery以外の自動再試行は追加しない。

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | 調教師・騎手・競走馬プロフィールの見出し欠落は1件のFailed/要対応として残り、同revisionで再試行されない。 | T1,T2 | handler/store integration tests | Not started |
| AC2 | AC1の失敗後もpipelineは稼働状態を保ち、別のdue taskを取得・実行できる。 | T1,T2 | store/worker integration test | Not started |
| AC3 | 未分類の恒久障害と内容検証失敗は従来どおりpipelineを停止し、alertを1回発行する。 | T2 | pipeline alert regression tests | Not started |
| AC4 | failure impactはCollector→complete API→storeへ型付きで伝搬し、欠落時は安全停止側へ倒れる。 | T1,T2 | serialization/API integration tests | Not started |
| AC5 | `/jobs`では構造エラーを再取得非推奨の要対応として表示し、全体収集は処理中または待機消化中と確認できる。 | T3 | component test and production browser verification | Not started |
| AC6 | 配備後、対象revisionだけを安全に復旧し、同じ構造エラーが発生しても他の収集が停止しない。 | T3 | production `/jobs` before/after evidence | Not started |

## Delivery plan

1. 型付きfailure impactとtransport伝搬を実装する。
2. 主体プロフィールhandlerで構造エラーを明示的に隔離する。
3. store・alert・handler・API境界の回帰テストを追加する。
4. CI、デプロイ後に対象をrevision recoveryし、pipeline継続を本番確認する。

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | failure impact契約と主体構造エラー分類を実装する。 | Main | Lead tier | Approval | CollectionOperations、Collector、API transport | focused contract/handler tests | AC1,AC4 | Proposed |
| T2 | store停止判定と安全停止回帰を実装・検証する。 | Main | Lead tier | T1 | CollectionPlatformStore、API tests | store/alert integration tests | AC2-AC4 | Proposed |
| T3 | UI回帰、CI、配備、対象revision recovery、本番継続確認を行う。 | Main | Lead/review tier | T1,T2 | UI tests、docs、production operation | component/full tests、deployment runs、production evidence | AC5,AC6 | Proposed |

## Review gates

- **Design and task-split review — 2026-09-18, reviewer: Main.** 本番停止理由、対象詳細、handler例外境界、worker classifier、store停止判定を追跡した。全`UnexpectedPage`の停止解除やmessage allowlistを避け、明示的impactだけを非停止にすることでAC1-AC6と既存安全停止を両立する。契約・store・本番経路が密結合のためMainが直列で担当する。
- **Pre-implementation review — approval待ち。** 承認後にT1をRunnable、T2-T3を依存順に分類する。
- **Checkpoint review — 未実施。**
- **Final review — 未実施。**

## Verification record

- 2026-09-18: 本番で停止理由 `Task=55ba65be-5ea2-4818-bfc1-a5c62d0db504; Error=JraCollectionException; 調教師情報の見出しを確認できません。` を確認した。
- 2026-09-18: 障害群`FBF7553D84F38EFB`に調教師2件があり、最新対象は`松永 幹夫（栗東）`、Background/priority 40、試行1回、取得先未記録であることを確認した。
- 2026-09-18: handlerで構造例外が未捕捉、workerで`PermanentFailure`化、storeで`ResourceNotFound`以外のterminal failureが全体停止となる実行経路を確認した。
- 2026-09-18: 最初の復旧依頼でpipeline再開と進行確認だけを「復旧」と報告し、根本原因と恒久対策案を同じ作業内で提示しなかった。運用再開を完了条件と誤認したworkflow gapとして整理し、`.codex/skills/production-incident-recovery/SKILL.md`を追加した。以後は暫定復旧、根本原因、対策設計、実装、配備、本番検証を別々に追跡する。

## Deviations and follow-up

- 承認前のためproduction codeは変更していない。workflow改善はユーザーから別途明示承認されたため、製品修正に先行して適用した。

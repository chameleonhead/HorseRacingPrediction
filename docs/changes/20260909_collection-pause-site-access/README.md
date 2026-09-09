# 収集全体停止中も管理サイトを利用可能にする

- Status: Completed
- Owner: HorseRacingPrediction maintainers
- Created: 2026-09-09
- Updated: 2026-09-09

## Context

収集ジョブの手動停止は `collectorOnly: true` で開始され、配送と新規リースだけを停止する。一方、DLQ閾値到達時の自動停止は `CollectionMaintenanceState.TryBegin()` を既定値で呼ぶため、DB全体メンテナンスとして扱われる。

APIのメンテナンスmiddlewareは、DB全体メンテナンス中の変更系HTTPリクエストを503にする。この対象にはBlazor Serverの接続確立・対話通信に必要なPOSTも含まれるため、収集全体停止後に管理サイトへアクセス・操作できなくなる。停止理由の確認や再開操作そのものを妨げる状態である。

## Goals

- 収集全体停止は、発生元が手動、タイムアウト、watchdog、DLQのいずれでも収集の配送・新規リースだけを停止する。
- 収集停止中も管理サイト、読み取りAPI、管理操作、停止理由確認、再開操作を利用できる。
- 実行中Workerによる完了・失敗・保留応答を受け付け、停止を理由に処理結果を失わない。
- 破壊的なDB初期化中の書き込み保護は維持する。

## Non-goals

- 停止中に新しい収集ジョブを実行すること。
- API認証・管理者権限モデルを変更すること。
- DB初期化中に通常のデータ変更を許可すること。
- 停止バナーを全管理画面共通へ拡張すること。

## Experience and interaction design

収集停止中も通常どおり管理サイトを開き、画面間を移動できる。収集ジョブ一覧では既存の「収集全体を停止中」バナーに、停止理由、日時、原因ジョブへのリンク、Primary Actionの「再開」を表示する。

収集停止はサイト停止やメンテナンス画面への切替ではない。通常データの閲覧・編集、ジョブ詳細確認、個別保留・解除、リラン・再取得依頼は受け付けるが、新しい収集実行は停止解除までリースされない。停止中に依頼したジョブは待機状態と理由を確認できる。

DB初期化中は別状態として扱う。UIシェル、Blazor接続、初期化状態、収集停止状態、再開・復旧に必要な経路は利用可能に保ち、通常のデータ変更だけを503で拒否する。技術例外を画面へ直接表示せず、既存のエラー状態で操作不能理由を説明する。

## Documentation updates

- `docs/22-collector-design.md`: 収集停止とDB初期化の境界、および収集停止中に許可する処理を正本仕様として追記する。
- `docs/20-admin-ui-design.md`: 収集停止中もサイト全体へアクセスでき、ジョブ画面の停止バナーと再開操作が機能することを明記する。
- `docs/changes/20260908_collection-timeout-hold/README.md` は実装当時の履歴として書き換えず、本記録で判明したDLQ停止経路の逸脱を是正する。

## Technical impact

- `CollectionMaintenanceState` の状態を、少なくとも「収集のみ停止」と「DB初期化中」で明示的に区別し、bool既定値に依存しないAPIへ変更する。
- 手動停止、永続停止マーカー復元、タイムアウト、watchdog、DLQサーキットブレーカーはすべて「収集のみ停止」を設定する。
- 収集のみ停止は `ProcessingStateStore` のdispatch/lease境界で実施し、HTTP middlewareでは通常APIやBlazor通信を遮断しない。
- DB初期化中のmiddlewareは、`/_blazor`等のUI接続、静的資産、GET/HEAD/OPTIONS、初期化状態確認、ジョブqueue-state、resume/resetの復旧経路を許可する。その他の変更系APIは503を返す。
- `TestApplicationFactory` のmiddlewareをproductionと同じ判定へ同期する。

## Decisions

### 採用: 収集停止とDB初期化を別モードにする

収集停止の目的は外部サイトへの新規アクセスとジョブ実行を止めることであり、管理サイト・業務データベースを停止することではない。DB初期化は排他的な書き込み保護が必要な別操作である。

### 採用: 収集停止はHTTP遮断ではなくdispatch/lease境界で強制する

ジョブ登録、閲覧、監査、実行中Workerの結果報告は停止中も必要である。実行開始を原子的に防ぐ既存store境界を正本にし、HTTPメソッドやパスによる広域遮断を収集停止へ流用しない。

### 採用: DB初期化中もBlazor接続と復旧経路は許可する

状態確認や復旧操作まで遮断すると、利用者がサイトから回復できない。通常のデータ変更は拒否しながら、UIの成立と復旧に必要な最小経路を許可する。

## Acceptance criteria

1. 手動停止、タイムアウト、watchdog、DLQ閾値到達のいずれでも収集のみ停止状態になる。
2. 収集停止中も管理サイトのページを開き、Blazorの対話操作と画面遷移を利用できる。
3. 収集停止中も業務データのGET、管理API、ジョブ詳細、queue-state、再開操作を利用できる。
4. 収集停止中に新規dispatchと新規leaseは発生しない。
5. 収集停止前から実行中のWorkerは状態照会と結果・中断報告を完了できる。
6. 収集停止中に登録したジョブは待機し、再開後に既存の再配送契約で実行可能になる。
7. DB初期化中は通常の変更系APIを503で拒否する一方、管理サイト接続、状態確認、復旧経路は利用できる。
8. 停止バナーは理由・日時・原因ジョブを色だけに依存せず表示し、keyboardで再開できる。
9. productionとAPIテストホストのmiddleware判定が一致する。
10. API、CollectionOperations、Blazor component、ブラウザー回帰、solution build、`git diff --check` が成功する。

## Delivery plan

1. メンテナンス状態を明示的なスコープへ変更し、全呼び出し元を分類する。
2. HTTP middlewareをDB初期化専用へ限定し、Blazorと復旧経路の許可規則を共通化する。
3. 手動・タイムアウト・watchdog・DLQ停止、UI接続、API利用、dispatch/lease拒否、再開をテストする。
4. 実ブラウザーで停止中の画面遷移、停止バナー、再開操作を確認する。

## Verification record

- Solution build成功（警告0、エラー0）。DLQ停止テスト2件、Apiテスト121件が成功し1件がスキップされた。

## Deviations and follow-up

- 設計どおり、DLQ経路もcollection-only停止へ統一し、停止時のキュー消去を廃止した。

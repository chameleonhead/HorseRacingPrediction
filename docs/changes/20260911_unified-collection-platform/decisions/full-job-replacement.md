# 既存収集ジョブ実装の完全置換

## Decision

`AgentJobType`, `ProcessingJobEntity`, `JobAttemptEntity`, `IProcessingStateStore`, `ProcessingStateStore`, `CollectionExecutionService` を中心とする既存収集ジョブ実装は、互換アダプターで恒久利用せず、新しい Resource 中心の request/task/attempt 基盤へ完全に置換する。

既存 Parser / Page / Navigator / scraping workflow、Api domain write、SQS/Lambda という配置、outbox・lease・watchdog 等から得た安全要件は再利用する。ただし既存 job schema/API/store/runner/scheduler のクラスやテーブルを新モデルの内部に残さず、新モデルの責務と名前で実装し直す。

## Why

既存 job type は収集対象、処理手順、取得理由、集約、計画、予想を混在させている。これを adapter で包み続けると、新しい Resource/Definition/Revision/State と旧 JobType/用途別 status の二つの正本が残り、状態遷移、active uniqueness、再取得理由、revision impact、lane fairness の規則が二重化する。

## 解決が必要な点

### 1. 収集と予想の状態ストア分離

`PredictionExecution` は収集ではないが、現在は `AgentJobType` と `ProcessingStateStore` を共有する。旧 store を削除する前に Predictor 専用の execution state または既存 Predictor scheduling へ移し、収集 task model に混入させない。予想候補の enqueue/acquire/complete/requeue と関連設定・テストの移管先を確定する。

### 2. 新しい所有境界とプロジェクト依存

CollectionOperations に新しい domain-neutral collection contracts/store を置き、Api が永続化・計画・管理 API を、Collector が handler 実行を所有する。Api が Collector 実装を参照しない境界を維持し、現在 `HorseRacingPrediction.Collector.Scheduling` namespace にある共有契約を新 namespace へ移す。

### 3. 新基盤の初期状態構築

旧 job row、attempt、outbox、用途別 status、job ID、deduplication key は新 task row へ移行しない。新基盤の Resource/State/Location は保存済み Domain Data と source citation から再構築し、不足分と未完了 work は新 Scheduler/Discovery が新しい CollectionRequest として再生成する。旧 Pending/Ready/Retryable job は引き継がず、Running job だけは cutover 前に drain または期限切れまで待機して書き込み競合を防ぐ。

### 4. 旧ジョブデータの削除

cutover 前に旧 DB の状態別件数、Running lease、outbox、SQS/DLQ 件数を検証記録へ集計するが、旧 job row、attempt、audit、marker、outbox、failure notification、用途別 status、job ID、deduplication key を保持・archive・migration しない。新基盤の smoke test が成功した同じ maintenance window 内で旧 table または旧 job DB を削除する。競馬の Domain Data、source citation、認証情報は削除対象外とする。

### 5. Active task と冪等性

新 task table は terminal 履歴の重複を許し、別 guard により Resource + Definition の active execution を一件にする。SQLite の partial unique index または guard row の競合試験を行い、Api 多重要求、SQS 重複、lease expiry、再起動、manual/revision/schedule 同時要求を検証する。

### 6. 通知契約とデプロイ順

旧 `{jobType, deduplicationKey, dispatchGeneration}` 通知を廃止し、新 `{taskId, dispatchGeneration}` 契約へ置換する。Api、Collector/Lambda image、SQS event handler、DLQ reconciler、Terraform、local `--once` を同じ rollout version で合わせる。混在 version は acquire を拒否し、Poison/DLQ ループを起こさない。

### 7. Queue cutover

maintenance mode で新規計画を停止し、旧 Running lease を drain する。新 notification contract 専用の新 SQS/DLQ queue を先に作成し、Api/Collector/Lambda を新 queue へ切り替える。smoke test 成功後、同じ maintenance window 内で旧 main queue と旧 DLQ を purge ではなく queue 自体の削除により廃止する。実行前に Terraform state と AWS から旧/新 queue の ARN/URL を照合し、新 queue を誤削除しない。利用者は本 change record の承認により、旧収集ジョブデータと旧 queue のこの削除を明示的に承認する。

### 8. Scheduler と公平性の置換

`CollectionPlanningScheduler`, `ScrapingRegistrationService`, job-type loop、固定 priority sort を停止し、due-state scheduler、request planner、lane-aware dispatcher/worker allocator に置換する。二つの scheduler が同じ Resource を計画する期間を作らない。

### 9. 安全制御の再実装

pause/resume、per-task hold/cancel、lease heartbeat/expiry、dispatch generation、watchdog、DLQ circuit breaker、failure notification、deadline、site rate limit、race mutation guard を新 ID/状態遷移で実装する。既存クラスの流用ではなく、同等以上の受け入れテストを移植する。

### 10. API/UI と運用手順

`/api/admin/jobs`, internal acquire/report endpoints、subject/race reacquisition endpoints、Jobs Razor UI を Resource/Collection API/UI に置換する。旧 endpoint は cutover と同時に削除または明示的 `410 Gone` とし、呼び出し元を先に更新する。runbook、監視、アラーム、dashboard、opaque old job link を更新する。

### 11. 型別 handler と dispatch

巨大な `switch (AgentJobType)` を新しい `ICollectionDefinitionHandler` registry に置換する。起動時に definition、revision、handler、locator、parser identifier、schedule policy の一対一対応を検証し、未登録 definition を実行しない。

### 12. テストの置換方針

旧 store/job-type の振る舞いを固定するテストは、新しい invariant を検証するテストへ書き換える。Parser/workflow/domain write のテストは維持する。API、Collector、SQS、Lambda相当、restart、migration、failure injection、fairness、revision、location fallback の統合テストを cutover gate とする。

### 13. Cutover と rollback

本番 cutover は `prepare new DB/queue -> stop old planning -> drain running leases -> build initial state from Domain Data -> switch Api/Worker/Lambda -> smoke test -> delete old job data and old queues -> enable new planning` の順とする。rollback 可能なのは旧データ・旧queue削除前までである。削除後は旧基盤へ戻さず、新基盤を修正して Resource state から再開する。Domain Data は巻き戻さない。

### 14. 削除完了条件

旧 job classes/tables/endpoints/UI/configuration/tests が production dependency graph と repository からなくなり、`AgentJobType`, `ProcessingStateStore`, `IProcessingStateStore`, `ProcessingJobEntity`, `CollectionExecutionService` の CodeGraph caller がゼロになることを確認する。AWS 上に旧 main queue/DLQ が存在せず、旧 job DB/table と旧 job ID/deduplication key が残っていないことを確認する。

## Rejected alternative

旧 job を compatibility adapter で包み definition ごとに長期間 dual-run する案は採用しない。短い検証環境での shadow comparison は許容するが、本番で新旧双方が work を生成・実行する期間は作らない。

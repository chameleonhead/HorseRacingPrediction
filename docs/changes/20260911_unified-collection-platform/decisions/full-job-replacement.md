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

### 3. 旧データの意味的移行

旧 job row を新 task row へ機械的に複製しない。domain data、source citation、race/subject/day status、成功 attempt を基に Resource、State、Location、Batch progress を再構築する。旧 Pending/Ready/Retryable/Running job は cutover 時に停止・lease expiry 確認後、理由付き CollectionRequest へ変換する。変換不能は件数と理由を migration report に残し、黙って破棄しない。

### 4. 履歴・監査の保全

旧 DB は cutover 前に整合性確認とバックアップを行い、read-only archive として retention 期間を定める。新 UI/API から旧 job ID を実行対象として扱わず、必要な監査参照だけ archive report で可能にする。個人情報・認証情報・payload secret が archive にないことも確認する。

### 5. Active task と冪等性

新 task table は terminal 履歴の重複を許し、別 guard により Resource + Definition の active execution を一件にする。SQLite の partial unique index または guard row の競合試験を行い、Api 多重要求、SQS 重複、lease expiry、再起動、manual/revision/schedule 同時要求を検証する。

### 6. 通知契約とデプロイ順

旧 `{jobType, deduplicationKey, dispatchGeneration}` 通知を廃止し、新 `{taskId, dispatchGeneration}` 契約へ置換する。Api、Collector/Lambda image、SQS event handler、DLQ reconciler、Terraform、local `--once` を同じ rollout version で合わせる。混在 version は acquire を拒否し、Poison/DLQ ループを起こさない。

### 7. Queue cutover

maintenance mode で新規計画を停止し、旧 Running lease を drain する。旧 outbox/SQS/DLQ の件数を照合し、未完了 work を request に変換してから旧通知を隔離する。キュー purge は不可逆操作なので、実件数・変換結果・バックアップ・対象 ARN/URL を確認し、実施時に改めて利用者承認を得る。可能なら新 queue へ切り替えて旧 queue を retention 後に削除する。

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

本番 cutover は `prepare -> stop old planning -> drain -> backup -> migrate -> verify -> switch Api/Worker/queue -> smoke test -> enable planning` の順とする。rollback は新規収集を停止して新 DB/queue を保存し、旧 image/DB/queue を再有効化できる時点までに限定する。新基盤で domain write が始まった後は、domain data を巻き戻さず、旧基盤へ再投入する request を生成する。

### 14. 削除完了条件

旧 job classes/tables/endpoints/UI/configuration/tests が production dependency graph と repository からなくなり、`AgentJobType`, `ProcessingStateStore`, `IProcessingStateStore`, `ProcessingJobEntity`, `CollectionExecutionService` の CodeGraph caller がゼロになることを確認する。旧 DB/queue の運用削除は retention と別途の破壊操作承認後に行う。

## Rejected alternative

旧 job を compatibility adapter で包み definition ごとに長期間 dual-run する案は採用しない。短い検証環境での shadow comparison は許容するが、本番で新旧双方が work を生成・実行する期間は作らない。

# 現行収集処理の調査結果

調査日: 2026-09-11

## 1. 現在の収集処理一覧

| 領域 | 主な入口/JobType | 実行 | 状態 |
|---|---|---|---|
| 出馬表 | `RaceCardCollection` | `CollectionExecutionService` → `JraRaceCardCollectionWorkflow` | 稼働中。開催日/場から一覧を開き各 RaceCard を保存 |
| レース結果 | `RaceResultCollection`, `ResultDayCollectionRequest` | `JraRaceResultCollectionWorkflow` | 稼働中。current/recent/historical navigation を使用 |
| レース/開催日再取得 | `RaceReacquisition`, `RaceDayReacquisition` | 専用 payload/execution | 稼働中。新 job を作る手動再取得 |
| 馬/調教師プロフィール | `SubjectProfileRefresh` | `JraNavigator.ToSubjectProfileAsync` | 稼働中。馬は検索、調教師は名鑑から同定 |
| 馬履歴 | `HorseHistoryDiscovery`, `HorseHistoryRace`, `HorseHistoryExcluded` | profile 履歴を探索し子 job 化 | 稼働中。成功/失敗/除外を保持 |
| 自律 Backfill | planning/month/day/result 系 job | scheduler/registration/execution | 既定3年 rolling backfill。production flag は実測待ち |
| 予想 | `PredictionExecution` | Predictor | 同じ job store に残るが収集 Resource の対象外 |
| Odds | なし | API は unavailable response | 未実装 |

`AgentJobType` は収集対象、workflow、集約、planning、予想を同じ文字列名前空間で表し、execution service は job type switch で分岐する。

## 2. Race / Horse / Trainer 等の識別方法

- JRA Race は scraping 層の `RaceId(Date, RaceCourse, Number)`、domain Race は `DeterministicIdGenerator.BuildRaceId` の別 ID を使う。
- Horse/Jockey/Trainer の domain ID は、名前の normalize 結果から deterministic に生成する経路が残る。
- 公式プロフィールは `JraSubjectIdentity(SubjectType, Name, BirthDate, SourceIdentity)` を使う。Horse の `SourceIdentity` は検索結果リンク、Trainer は名前/所属名簿で同定する。
- domain ID、JRA identity、source link の関係は共通 registry ではなく各経路に分散する。Jockey profile の direct collection は未統合である。

## 3. URL生成・Navigation方法

- `JraUrls` に既知 endpoint があり、`JraNavigator` がトップ、開催選択、一覧、レース番号リンクを遷移する。
- result は対象日の新旧に応じ current/recent/historical 導線を選び、RaceCard は表示期間内の開催選択を使う。
- 一覧リンクを current URL から解決して直接 navigate する箇所はあるが、ResourceLocation として永続・評価されない。
- Horse は名前検索を行い、同名時は birth date/source link で絞る。Horse history の race URL は profile 上の link と再探索を組み合わせる。
- workflow は要求 RaceId と parse 済み page RaceId を照合し、HTTP 200 のみで成功にしない。

## 4. Job / Queue / ProcessingState管理方法

- Api 所有 SQLite が `jobs`, `job_attempts`, dispatch/failure outbox, audit, markers と用途別 status table を保持する。
- `jobs` は `(job_type, deduplication_key)` unique。再取得は GUID 入り新 key と failed job の再投入が混在する。
- 状態は Pending/Ready/Running/WaitingDependency/Succeeded/Failed/Cancelled/DeadLetter。lease token、expiry、dispatch generation で重複 Worker を防ぐ。
- SQS は通知のみで、Api が正本、Collector は単一 task worker である。
- Race、ResultDay、Subject acquisition に別々の projection があり、共通 Resource + Definition state はない。

## 5. リトライ・エラー管理

- attempt number、job status、error、開始/終了を DB に保持する。
- watchdog が lease expiry/dispatch 漏れを回収し上限後 DeadLetter にする。DLQ reconciler は未報告終了を Failed に反映し、閾値で全体停止する。
- transient HTTP retry、race error classifier、navigation reason が存在する。
- attempt には URL、HTTP status、redirect、page identification、共通 error category が一貫して保存されず、用途別 error code も要求分類より粗い。

## 6. Domain Dataへの書き込み経路

- workflow は `IDataCollectionWriteService` 経由で Api へ HTTP 書き込みする。
- RaceCard は Race/Horse/Jockey/Trainer/RaceEntry を Upsert し source citation を記録する。
- RaceResult は bulk endpoint で Race/Entry/Result/Payout 等を宣言し、既存 Race 再取得時は refresh path を使う。
- subject profile は source identity/source URL/acquired time と fields を保存する。
- race mutation は running job ID + lease token + target scope を検証する。
- write は概ね冪等だが collection revision と outcome の atomic な関連付けはない。Odds snapshot は未実装である。

## 7. 再利用可能なもの

- Api job controller、SQLite transaction、outbox、SQS notification、lease/heartbeat/dispatch generation。
- watchdog、DLQ circuit breaker、hold/pause/resume、failure notification、audit。
- `JraSession`, `JraNavigator`, semantic snapshot、Page/Parser、Race identity validation。
- current/recent/historical result navigation、calendar/race list discovery。
- RaceCard/Result/Subject workflow と冪等 domain write。
- manual reacquisition、rolling backfill、ResultDay progress、subject child-task/checkpoint の知見とテスト。
- priority ordering、planning review、site interval、deadline、mutation lease guard。

## 8. 廃止または統合すべきもの

- Resource/Definition/Reason/aggregate planning が混在する `AgentJobType` 中心制御。
- `RaceDataCollectionStatus`, `AgentAcquisitionStatus`, `ResultDayCollectionStatus` という用途別状態正本。
- URL を status row/source identity の単一フィールドとして持つ方式。
- priority を作成時だけ固定する方式。
- job type ごとの active prefix query。
- job attempt の自由文 error のみへの依存。

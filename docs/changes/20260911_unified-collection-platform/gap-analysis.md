# Gap Analysis

| 要求能力 | 現状 | Gap | Phase |
|---|---|---|---|
| Resource identity | Race/domain/subject ID が個別 | 共通 registry と alias mapping がない | 1 |
| Definition | JobType/workflow が代用 | 観測、handler、schedule、revision の境界がない | 1-2 |
| Revision | なし | impact/RequiredRevision/部分再取得を表現不能 | 1, 8 |
| State | 用途別 status | Resource + Definition の鮮度/次回/revision がない | 1-2 |
| Request reason | payload/audit に分散 | 全起点を同じ request で監査できない | 1-2 |
| Repeatable task | 一部 GUID key | active guard と同 revision 再取得の共通契約がない | 1-2 |
| Attempt evidence | 基本履歴あり | URL/HTTP/redirect/page identity/error category 不足 | 1-3 |
| Discovery graph | 各 workflow 内 | ResourceReference/request expansion が未共通化 | 3-5 |
| Backfill holes | 日/子 job で把握 | Resource/Definition/batch 横断 projection がない | 5 |
| Refresh policy | planning と priority | state を評価する共通 policy がない | 6 |
| Lane/fairness | priority sort | lane quota/starvation bound がない | 6 |
| Direct URL | 一部 direct navigate | 永続候補、verification、fallback がない | 3 |
| Explicit URL | debug のみ | Resource 同定から通常 request への入口がない | 3, 9 |
| Error semantics | retry/DLQ/reason あり | 共通分類と NotYetAvailable が不足 | 2-3 |
| Odds | unavailable | parser/snapshot/repeat/停止条件がない | 7 |
| Manual/bulk | 個別 endpoint | 共通 preview/selector/expansion がない | 8-9 |
| Monitoring | job/day/subject | Resource/revision/lane/batch projection がない | 1, 8-9 |

## Replacement risks and controls

- 既存 job ID/API/UI の呼び出し元を inventory 化し、新 API/UI へ切替後に旧実装を削除する。旧 identity は新 task model に持ち込まず archive report に限定する。
- `jobs` unique を外す前に Resource + Definition active guard を transactionally 保証する。
- Race scraping/domain ID と name-based subject ID は一括統合せず provider identity alias を追加し、曖昧なものは unresolved にする。
- domain write 成功前に state を Current にせず、write receipt/outbox と projection 更新順を規定する。
- dynamic priority は Ready/RetryWaiting のみ更新し running lease を奪わない。
- Odds は site-wide rate limit、jitter、Retry-After、kill switch、最大観測回数を release gate に含める。

## Full replacement strategy

1. Predictor work を旧 collection store から分離する。
2. 旧 store に依存しない新 schema/controller/worker/scheduler/API/UI を完成させる。
3. 隔離環境で旧 DB の dry-run migration と shadow parity を行うが、新旧 worker は同じ Resource を実行しない。
4. maintenance window で旧 planning を止め、lease drain、backup、未完了 work conversion、新 notification contract への切替を行う。
5. smoke test 後に新 planning を有効化し、rollback window を経て旧 production code を同じ変更セット内で削除する。
6. 旧 DB/queue は read-only retention 後、正確な対象と別途承認を得て削除する。

# ローカル収集ソークテスト（2026-09-13）

## 実行条件

- 実行時間: 2026-09-13 13:33:47～15:33:58 JST（120.19分、120サンプル）
- 構成: API outbox → SQLite永続ローカルキュー → 常駐Collector
- 外部基盤: AWS/SQS/Lambdaは不使用。JRA公開サイトへの取得だけを実通信とした
- 対象: 2026-09-12、2026-09-13を優先し、空き時間用に2026-08のBackfillを追加

## 結果

- Worker failure: 0
- API応答時間: 平均56.31ms、p95 78.54ms
- APIメモリ: 約159～274MB
- 2026-09-12: RaceCard 24/24、RaceResult 24/24成功
- 2026-09-13: RaceCard 24/24成功。実行時点で当日結果は収集対象外
- 関連するHorse/Jockey/Trainerを含め、1,700件超のTaskへ段階展開した
- Collectorを意図的に停止・再起動し、永続キューから処理を再開できることを確認した
- 2026-09-05 中山11Rで本賞金と付加賞、同6Rで競走中の出来事が保存されることをAPIで確認した

Collector再起動後はPIDが変わるため、ランナーが保持した旧PIDに対するCollectorメモリ値は0となった。再起動後の手動観測値は約132～141MBであり、次回はプロセス名と起動時刻からPIDを追従する。

## 検出した問題と修正

### Ready Taskの誤った再配送

単一FIFOキューにBackfillが滞留すると、送信済みのReady Taskがgrace時間内にAcquireされない。watchdogがこれを配送消失と誤認し、再配送を繰り返した後に`DispatchAttemptsExceeded`として1,038件を失敗扱いにしていた。

送信成功後のReady Taskはキュー上で待機し得るため、watchdogの時間経過だけでは再配送・dead-letterしないよう修正した。watchdogは期限切れRunning leaseだけを回収し、実際の配送枯渇はtransport DLQ reconcilerへ委ねる。誤失敗1,038件は通常のRecovery requestへ展開し、534件を新規作成、504件を既存active taskへ合流させた。監視周期経過後も`DispatchAttemptsExceeded`は再発していない。

### グレード欠落

JRA本文のgrade badgeがsnapshot textに含まれない場合があり、レース名に`GⅡ`が保存されていても`gradeCode`がnullになった。Parserは抽出済みレース名も判定材料にし、API書き込み境界でも収集値、収集レース名、既存レース名の順に`G1/G2/G3/JPN1/JPN2/JPN3`を補完する。

### 優先順位の制約

標準FIFOキューへ既に送信済みのBackground通知を、後から作ったRealtime通知が追い越すことはできない。今回の正式計測では直近日を先にseedした。今後、実運用で同一キューを維持したまま厳密な追い越しを必要とする場合は、outboxからキューへ先送りする量を制限し、lane allocatorが選べる在庫をDB側に残す必要がある。

## 自動検証

- Solution build: 警告0、エラー0
- Solution tests: 成功884、skip 2、失敗0
- 追加対象テスト: Ready待機中の非再配送、レース名からのgrade補完、ページ本文にgradeがないParser経路

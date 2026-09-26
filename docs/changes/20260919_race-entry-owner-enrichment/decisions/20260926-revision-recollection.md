# 停止しない取得revision更新（最新方針）

> 2026-09-26 14:02 JST: 第一段階revision3配備後、中山5Rの既存馬番不一致で再停止。[補正前差分・限定補正の具体設計](20260926-number-repair-impact.md)を最新補足とする。補正可能性を公開GETだけで断定せず、authoritative previewと独立参照の拒否、単一補正event、部分失敗gate、本番applyの別途承認を具体化した。以下の「revision 2使用中」「revision 3候補」は当初の履歴であり、次版は本番使用状況を確認して選ぶ。

> 停止障害対応を[追加設計](20260926-track-condition-recovery.md)へ追加した。新たな全体メンテナンス停止を行わない方針は維持するが、既に発生した自動停止からの安全な再開は運用スコープに含める。第一段階の障害修正と第二段階の馬主補正には異なる次revisionを割り当てる。本文のrevision 3は当初候補であり固定値ではない。配備前に本番の使用済み版を確認する。

- 利用者指示: 「停止は不要ですが、取得バージョンを進めて再取得を促してください」。停止・offline tool・直接DB補正の旧案を撤回する。
- 状態: Proposed。revision更新だけでは既存Current Cardをスキップし、誤馬番の通常bulkも安全に補正できないため、下記の保存条件を満たす実装設計を先に確定する。停止不要という判断は尊重するが、誤帰属を許可したとは解釈しない。

## 確認済みの実経路

- `race-detail` はAPI起動・initializer・discovery・history・owner repairでrevision 2を使用している。
- 既存revision APIはpreview/apply/recollect/progressを持ち、全体pauseを必要としない。applyはRequiredRevisionを更新するが、それだけではtaskを作らず、recollectが必要。
- handlerはCardのCurrent状態だけを見ており、旧版の取得済みCardを新版でもスキップする。leaseにartifactのAppliedRevisionを渡し、要求revisionとの比較が必要。
- 現行のowner migration applyはpauseを必須とするため今回使わない。Card公開期間外の過去Raceを一括再取得しない。

## 修正と運用の順序

1. 馬番確定待ち、通常/refreshのowner保存、保存前のidentity検証、raw照合を修正する。旧Cardを新版で再取得できるartifact revision判定も修正する。
2. すべての登録入口のrace-detail取得revisionを次版へ揃える。新版の動作が検証できる前に、本番要求revisionだけを先に上げない。
3. 新版が稼働することを確認後、公式Card取得可能な対象に限定してrevision impactをpreview/apply/recollectする。最初は問題のRaceをSpecificResourcesで指定し、全期間の再投入・cancel・pause・failure消去は行わない。
4. 再取得後のraw owner、公式馬番、grade、関連projectionと再送時event数を確認する。revision適用やrequest作成は修復完了としない。

## 解消が必要な保存境界

馬番が既存と一致するentryは同じHorseIdにだけ後着値をマージする。馬番入替が必要な対象について、以下を満たさずに既存refreshを呼び出すことはしない。

- 公式Horse source identityによる全頭1対1対応、重複なし、期待する旧状態/versionとの一致。
- 関連主体作成より前の全件検証と、入替先の旧馬の属性を継承しないこと。
- 既存予想・結果・払戻・オッズ等を無断で別馬へ付け替えないこと。参照の有無を調べてから書くまで、対象Raceの新規書込みが割り込まない保証。
- 全体停止の代わりとなる対象Race単位の排他と、複数process・再送・lease期限切れ・projection遅延での安全性を検証する。実現未確認の排他を既存機構にあると仮定しない。

現時点では最後のオンライン補正境界が未検証であり、revision引上げだけの本番操作は行わない。主担当が保存・排他設計を所有し、revision/公開待ち等の独立した実装sliceはこの境界確定後に切り出す。

## オンライン保存境界の具体案（追加設計）

全体メンテナンスの代わりに、対象Raceだけの短時間の書込直列化をAPIで行う。Race更新・通常/refresh bulk・個別entry・オッズ・予想生成/更新の全入口が同じ鍵を使い、他RaceとGETは止めない。予想IDのみの入口は保存済みRaceIdを解決する。現行APIのcommand呼出しを棚卸し、filterのない別groupのodds入口も対象にする。

本番SQLiteの同じDBパスに対応する共有lock領域で、RaceIdから生成した鍵の排他的ファイルハンドルを保持する。process内Semaphoreだけで済ませず、同一volumeを使う複数processの排他・process終了時解放・待機timeout/取消を実試験する。排他対象ファイルは保持し、lock中の削除/再生成を行わない。未知のDB構成や共有lock領域を確立できない構成は、補正可能とみなさず拒否する。これは短時間のRace単位排他であり、pipeline pause・API停止ではない。

鍵取得後に最新状態を読み、全頭のidentity対応と既存参照を検証してから、通常のdomain更新として1レース分のeventをcommitする。関連主体の作成も検証後に行う。馬番の入替は参照がない場合に限定し、予想ticket・結果・払戻・オッズ・未知参照があれば構造化エラーで書込前拒否。最新Cardだけで全頭の対応を証明できなければ、再取得したことを理由に上書きしない。既存entryの旧占有馬の属性を引き継がず、旧新projectionを更新し、raw照合まで鍵を保持する。Read modelにまだ反映されていないeventがあれば補正せず、再試行可能な状態として扱う。

旧leaseや重複配信への対策は別に維持する。Race resourceの `date:course:number` とdomainRaceIdの対応を実装上正規化し、正しい有効leaseのみをworkerの書込権限として使う。不正leaseが「activeなし」を理由に無条件通過する経路を補正に使用しない。集約versionの競合検出も有効にする。

この新規のRace単位排他・参照なしの場合のオンライン入替は、利用者のrevision更新指示から不可逆な付替えまで自動的に承認されたとは扱わない。設計の追加境界として提示し、承認後にのみ実装する。承認対象は、馬番確定待ちと確定後取得、通常/refreshのowner保存、revision 3とartifact revision判定、上記の安全な限定入替、対象限定のrevision再取得、本番raw照合である。既存予想・結果の付替え、全体停止、直接DB修復は含まない。

## Evidence / task checkpoint

- Read-only explorer `revision_reacquisition`（requested `gpt-6-sol`、observed model/token unavailable）がrevision入口とartifact skipを調査。書込なし、テスト未実行。Mainがコードと整合を確認。旧版active taskから新版後続taskを作る既存経路は利用候補。
- IAC2/IT2を「停止中のoffline隔離」から「再取得中のRace単位の安全な書込み境界」へ再設計する。IAC4/IT4のoffline補正toolは採用しない。IAC1/IAC3/IAC5/IAC6の成果条件は維持する。
- 次の操作: 通常/refresh/個別entry/予想生成のwriterを対象にオンライン排他の設計を詰める。再取得時の参照移行が必要なら別途その範囲を明示する。全体停止の承認を再び求めない。
- コード、本番revision、task、DBは未変更。

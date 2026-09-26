# 読取証拠と再現

## Production signal

- Observed: 2026-09-27 00:55 JST。Mutation performed: none。
- resource: `Race/JRA/20260927:Nakayama:1`, definition `race-detail`, required revision4/applied0。
- group `43C1C29257924632`、target count1、attempt count1。
- task `8c7910f8-5385-4100-b082-1f4f2ea8497a`、notification `f1526af8-aa12-42f7-a0a1-cbca19cb3888`。
- attempt `e40823b1-8fed-47b5-834a-b2973acfb11c`: 00:53:29–00:53:37 JST、stage ResolveCard、persisted=false。
- error: `JraValueParseException`, `FieldName=HorseNumber`, `RawValue=取消`。
- final URL: `https://www.jra.go.jp/JRADB/accessD.html?CNAME=pw01dde0106202604090120260927/08`。
- page identity: Expected=RaceCard / Actual=RaceCard / Resource=20260927:Nakayama:1。HTTP statusはattemptに未保存。
- batch `83d9aa73-3f0e-47b3-a2e9-3f88166cc638`、Lambda request `ce4361cb-f67b-5a70-9deb-02fa1ad5ddff`、batchTaskCount24だがAPI保存済みattemptは当該ordinal1だけ。他23件の実行結果は推定しない。
- GET pipeline: isPaused=true、同notification/同取消エラーがreason、updatedAt00:53:37 JST。

## Official HTML observation

2026-09-27のread-only GETはHTTP200。対象行には `td.num` 内の `span.cap` が取消、現在Horse linkが `pw01dud002024105198/F2`、馬名ニシノドリーマー、枠画像altは枠3赤。
対象馬番欄には現在の数値馬番がない。隣接行には5と7があるが、これを根拠に6を採番してはならない。同じrowの過去レース欄には11番があるが、現在馬番ではない。

## Production-shaped reproduction

```powershell
dotnet run --project docs/changes/20260927_race-card-cancellation/probes/CancellationProbe.csproj --configuration Release
```

公式HTMLをbytesのまま取得してページの文字コード宣言を保持し、Chromium上で実snapshotter→現行parserを通した。副resourceアクセスは遮断し本番管理APIやwriterは一切使用しない。

```text
Observed number cells: 1,2,3,4,5,取消,7,8,9,10,11,12,13,14,15,16
REPRODUCED: JraValueParseException; Field=HorseNumber; Raw=取消
```

初回probeはHttpClientの文字列変換でShift-JISを誤解釈し別の構造例外になったため、bytes配信へ直し、本番と同じ例外が出ることを確認した。これはprobe側の局所修正であり、本番コード変更ではない。
live URLは時間経過で変わるので実装時に専用セル/馬リンク/過去馬番を保持した最小固定HTML fixtureを追加する。probeのPARSED出力だけでは修正の受入証拠にせず、実entryの状態・ID・番号・下流read-backを検証する。

## Source trace / independent inventory

- `RaceCardPageParser.ParseEntries`: nonempty horse numberに1–18の数値を要求し取消でthrow。
- `Scraping/Jra/Models/RaceEntry.cs`: 発走前取消状態がない。
- `JraRaceCardCollectionWorkflow` / `.Refresh`: 全Card行をupsert、行を省略しても旧entryを消す契約がない。
- `JraRaceCollectionHandlers`: null番号がCard保存後の未確定待機条件。
- `ApiOnlyPredictionWorkflow` / `RacePredictor`: nullを拒否するが取消対象を区別しない。
- `RaceAggregate.DeclareEntryResult`: ResultDeclared以降のみ、結果取消の流用不可。
- `RaceCardPublicationTests`: 木曜null/数字/未知値の既存基準。`RaceResultPageParserTests`: 結果取消の非回帰基準。

独立reader `entry_reference_inventory` がCard→domain→prediction/result経路をread-onlyで確認。Mainが実ページと再現を独立照合。model/token telemetryは未取得。作業worktreeにはCodeGraph indexがないため通常検索を使用し、新規indexは作成しない。

## Implementation verification checkpoint

- Live probe after patch: `Entries=16; Active=15; Cancelled=ニシノドリーマー; Number=null; Owner=西山 茂行`。公式HTMLを実snapshotter/parserへ通し、16行、取消1行、identityと馬主、番号非推測をassertした。
- Parser focused Release: 39 passed（16行/15Active、取消・除外、過去11番、未知値、identity欠落）。
- Domain focused 8 passed、Domain regression 126 passed（旧JSON、未指定保持、明示復活、stable ID、null番号保持、odds）。
- API初回統合3件は成功。16行read-back/15予想、全取消skip、状態変更後の古いfingerprint・取消馬へのmark/finalize・odds拒否を確認。refresh経路と不正状態の追加反例は後続全体gateで再実行する。
- Release solution build succeeded（0 warnings/errors）。formatと全体回帰・CI・配備は継続中。

## Verification failure ledger

| Task | Failure | Correction / closure evidence | State |
| --- | --- | --- | --- |
| T1 | 初回fixture期待値/16行・除外成功case不足 | fixtureを訂正し両状態16行へ拡充。focused39 passed | Verified |
| T4 | API fixtureで関連収集definition未登録、Owner名はowner-profileでなくowner-identity | 実定義名で初期化。API3 passed | Verified |
| T3 | context fingerprintがrepair barrierのあるRaceだけで返り、通常Raceでnull | 現在の割当から常時計算。API stale receipt反例成功 | Verified |
| T4 | future RaceのCard成功をTask全体Succeededと期待 | Result公開待ちは維持する契約に合わせ、PersistCard成功かつAwaitHorseNumbersなしを確認。Collector356成功 | Verified |
| T4 | 新規API testのwith initializer空白format | formatter後にexact format gate再成功 | Verified |
| T4 | 既存予想API fixtureが未登録entryを参照、補正testが現在割当ではなくmanifest fingerprintを期待（全体API3件失敗） | fixtureにActive出走を登録、現在割当の独立計算値を期待。最終API310成功 | Verified |

AC4 closure: 作成時のcontextだけでなく、下書き作成後の状態変更も旧情報の再利用となる。作成eventへoptional fingerprintを保存し、mark/finalize時に現在割当と照合する。旧eventはnull既定で互換を維持し、現役entry検証は行う。read table列追加・過去event書換えは不要。確定済み履歴は読取可能のままとする。

AC4 revision closure: isolated host smokeで `RepairHoldConflict`。新規DBはrevision5だけを登録する一方、repair holdの初回deferred requestとsmoke requestが4固定だったため未登録revisionとして拒否された。hold producerは登録済みcurrent revisionを参照し、smokeはhold.requiredRevisionを使う。Release側の `Math.Max(4, revision)` は旧補正契約の最低値で、current definitionとのmaxにより5を保持するため変更不要。JRA主体のHorse revision4は別definitionで対象外。該当API回帰fixtureもcurrent constantへ接続して同じ欠陥を検出できるようにした。

Integration base: reflogで本branch作成時のHEADは67da0af2と確認。332e38adは直前の配備baseであり、T1/T2D auditへ誤転記した開始revisionを67da0af2へ訂正した。実装中のHEAD移動はない。Web scaffolding/skill規則など既存commitはそのまま保持し、現在base上で全体検証する。本変更へ無関係なcommitを作り直したり混在させたりしない。DDD/orchestration skillと参照formatを再読了。

Pre-deployment read-only: pipelineは同じ取消notificationによるpauseのまま。対象には00:57:38の既存Recovery requestと未実行task `ed456814-30ca-4cf3-a695-8a9a43837c6b` が存在し、actionable failure groupsは空。配備後に再読取し、既存target taskのrevisionを確認して必要な対象限定更新だけ行う。重複retryや通知消去は不要。

Local checkpoint: 全体非External Release suiteは1360 passed / 1 skipped / 0 failed。formatのexact CI gate成功。EF pending-model差分なし、空SQLiteへの全migration適用成功。補正holdのrevision固定値修正後はAPI全体とisolated hostを再実行中であり、最終gateは未完了。文書checkpoint後にsource/testsを検証してcommitし、CIと配備へ進む。

CodeGraph: `.codegraph/` にはgitignore stubのみでindexは未初期化。`codegraph sync .` は `CodeGraph not initialized`。新規index作成は利用者判断のため行わず、source追跡と実経路testsを証拠とする。graph-basedな検証成功は主張しない。

Final local gates: 修正後API全体310 passed / 1 skipped、isolated host smoke成功（14entries/14owners、crossProcessLock、durableHold、backupVerified、delayedOddsRejected、currentWorkerWriteすべてTrue）。exact format gate再実行成功。上記のfuture Result待機、format、API fixtureの失敗は元gate再成功により閉鎖。revision固定値もcurrent-only DBの別プロセスsmokeで閉鎖。ローカル検証の未解決失敗なし。Linux CI/CDと条件付き対象復旧・観測は未完了。

AC-group review (Main): AC1 live HTML＋固定fixtureの独立証拠、AC2/3 API/event/projection/full-card16とML/API-only15・全取消skip、AC4旧event/未指定維持・旧receipt/下書き拒否・unknown拒否・既存回帰と別process境界を照合。T1のcoverage correction1回（加えてMainがdeprecated test属性を現行表記へ変更）、T2Dは追加修正なし。両workerのモデル実行・tokens/費用は独立telemetryがなく未確認。成功標本不足のため永続routing変更なし。単発のfixture/接続欠陥は既存gate内で修正し、新しいskill規則は追加しない。

## GitHub delivery / targeted recovery

- PR [#102](https://github.com/chameleonhead/HorseRacingPrediction/pull/102)、head b2e214d141ac94d34abd7bc8d1e8c5f2ea125ac1。Linux CI [36257452800](https://github.com/chameleonhead/HorseRacingPrediction/actions/runs/36257452800)成功。merge 8efc1d612a31e3d110c2c0b39f37ca94eb6ce4dc。
- Deployment [36257902413](https://github.com/chameleonhead/HorseRacingPrediction/actions/runs/36257902413)全job成功。API/collectorは同merge SHA、API health成功、既存pipeline pauseを維持。owner migrationはpreview-only。ローカルAWS/SSH配備なし。
- merged branchはancestor/tree一致・cleanを確認してdetach後にlocal/remote両方を削除。以後同branchへのcommit/pushなし。完了記録は最新mainから別branch。
- 02:20 JST事前GET: pipeline paused、runningなし、actionable failure group/Failed/DeadLetterとも0。対象にはrevision4のReady、attempt0の既存手動再取得taskが残る。
- 対象の旧Ready task `ed456814-30ca-4cf3-a695-8a9a43837c6b` のみcancelし履歴保持。旧active taskのrevisionはRequestAsyncで更新されないため、旧版実行を避ける対象限定置換とした。データ・通知・他jobの削除なし。
- revision5 Recovery(reason6) request `99bcdcd1-bdec-4377-ab3b-821882d152e3` → task `758b1f1a-9cc3-4c79-941b-a7dab2636222`。Ready/revision5と他失敗0を再確認し02:20:43 JSTにresume。
- 02:22:59 JST時点: pipeline再停止なし、worker実行とCurrent 27→28等の進捗あり。対象はまだReadyであり、この時点ではAC5未完了。対象保存後のCard/read-backと後続batch完了または15分観測を継続する。

## Production acceptance / final review

- 対象attempt `5e987653-5849-429f-aa05-f6f3587e7857` は02:23:22–02:23:25 JST。CardはCurrent/applied5/required5、lastPersistedAt02:23:25、errorなし。ResultはAwaitingPublication、RaceNotStarted、nextDueAt10:05。取消を未確定馬番待機として誤扱いせず、正常な結果公開待ちへ進んだ。
- 対象batch `2ed67a71-b129-4a19-a3a0-07ae063f42f7` は24番目の対象まで処理し02:23:25終了。将来のResultを保存済み/成功とは主張しない。
- 本番race `race-e6b2821d-268d-54e3-a4ee-1a0f7050d914` のGET race/contextを独立照合: 両方16entries、Active15、Cancelled1。取消馬ニシノドリーマーはHorseId `horse-45b9747c-425d-5641-ad23-4c5cc11a0813`、馬番null、枠3、馬主「西山 茂行」、騎手「野中 悠太郎」、調教師「竹内 正洋」。行や属性を削除せず、馬番6/過去11を補完していない。本番で予想生成を手動実行した証拠ではなく、候補状態read-backとローカル予想15件testsを組み合わせた検証。
- 対象後の独立batch `5d734b7b-8ca6-4fb9-a773-b7c6b3000d48` は02:24:09–02:24:43 JST、全4taskがSucceeded/result1、errorなし。02:25:12以降のGETでpipeline isPaused=false。観測条件は「15分または正常後続batch完了」の後者を達成（再開02:20:43→後続完了02:24:43）。
- AC1–AC5/T0–T5をVerifiedへ更新。コード/ローカル/Linux CI/同版配備/対象復旧・進捗は完了。データ削除、過去予想の書換え、他失敗の一括retryなし。

## Separate owner-identity follow-up (excluded from cancellation scope)

02:25:12 JSTにgroup `BA97EBE92E4D55E1`、SubjectNotIdentified/OwnerNotRegisteredを6件観測。pipelineは稼働継続。代表resource `owner-dbe401ae-0c14-5102-beb3-ac5cad1f7ffb`、task `427a2fb7-0357-4738-88ab-941eb2db50f0` は修正配備前の00:49:11作成。参照raceは2026-09-26中山1R `race-f3f8044a-e19e-5dfa-8c81-b22f9032aff2`、今回の2026-09-27対象とは異なる。

代表馬主「(株)ノルマンディーサラブレッドレーシング」: 要求owner IDのGETは404、同race出馬表には同名で `owner-74ff355a-d012-573c-8f22-f2bcfd342d09` が保存されている。handlerは要求IDの存在を確認しNotRegisteredを返す。該当JraSubjectCollectionHandlers.csは本PR未変更。これにより取消セル解析とは別の要求/保存ID不一致を確認したが、ID生成経路全体の原因特定や6件すべての同一原因は未確定。通知/データを消去せず、retryもしない。

Owner Main、別設計の要否を利用者へ提示する非阻害follow-up。今回のACは対象取消対応と限定復旧であり、全対象の識別データ補正ではない。対象取消馬の馬主保存、正常後続batch、pipeline稼働を実証したためAC5を満たすが、「本番の全エラー解消」とは報告しない。

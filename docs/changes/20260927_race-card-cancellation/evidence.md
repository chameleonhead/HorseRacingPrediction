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
| T4 | future RaceのCard成功をTask全体Succeededと期待 | Result公開待ちは維持する契約に合わせ、PersistCard成功かつAwaitHorseNumbersなしを確認 | In progress |
| T4 | 新規API testのwith initializer空白format | formatter後にexact format gateを再実行予定 | In progress |
| T4 | 既存予想API fixtureが未登録entryを参照、補正testが現在割当ではなくmanifest fingerprintを期待（全体API3件失敗） | fixtureにActive出走を登録、現在割当の独立計算値を期待。focusedと全体gateで再検証 | In progress |

AC4 closure: 作成時のcontextだけでなく、下書き作成後の状態変更も旧情報の再利用となる。作成eventへoptional fingerprintを保存し、mark/finalize時に現在割当と照合する。旧eventはnull既定で互換を維持し、現役entry検証は行う。read table列追加・過去event書換えは不要。確定済み履歴は読取可能のままとする。

AC4 revision closure: isolated host smokeで `RepairHoldConflict`。新規DBはrevision5だけを登録する一方、repair holdの初回deferred requestとsmoke requestが4固定だったため未登録revisionとして拒否された。hold producerは登録済みcurrent revisionを参照し、smokeはhold.requiredRevisionを使う。Release側の `Math.Max(4, revision)` は旧補正契約の最低値で、current definitionとのmaxにより5を保持するため変更不要。JRA主体のHorse revision4は別definitionで対象外。該当API回帰fixtureもcurrent constantへ接続して同じ欠陥を検出できるようにした。

Integration base: reflogで本branch作成時のHEADは67da0af2と確認。332e38adは直前の配備baseであり、T1/T2D auditへ誤転記した開始revisionを67da0af2へ訂正した。実装中のHEAD移動はない。Web scaffolding/skill規則など既存commitはそのまま保持し、現在base上で全体検証する。本変更へ無関係なcommitを作り直したり混在させたりしない。DDD/orchestration skillと参照formatを再読了。

Pre-deployment read-only: pipelineは同じ取消notificationによるpauseのまま。対象には00:57:38の既存Recovery requestと未実行task `ed456814-30ca-4cf3-a695-8a9a43837c6b` が存在し、actionable failure groupsは空。配備後に再読取し、既存target taskのrevisionを確認して必要な対象限定更新だけ行う。重複retryや通知消去は不要。

Local checkpoint: 全体非External Release suiteは1360 passed / 1 skipped / 0 failed。formatのexact CI gate成功。EF pending-model差分なし、空SQLiteへの全migration適用成功。補正holdのrevision固定値修正後はAPI全体とisolated hostを再実行中であり、最終gateは未完了。文書checkpoint後にsource/testsを検証してcommitし、CIと配備へ進む。

CodeGraph: `.codegraph/` にはgitignore stubのみでindexは未初期化。`codegraph sync .` は `CodeGraph not initialized`。新規index作成は利用者判断のため行わず、source追跡と実経路testsを証拠とする。graph-basedな検証成功は主張しない。

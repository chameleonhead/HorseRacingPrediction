# JRAサイト収集契約

- Document owner: Collection platform maintainer
- Last verified: 2026-09-19
- Applies to: JRA公式Webサイトを入力とする収集、parser、navigation、再収集、データ補正

## 1. 目的と扱い

本書は、JRA公式Webサイトのどの画面から何を取得できるか、画面間をどのように遷移するか、
取得できない情報を何で代用してはいけないかを定める保守用の正本である。

> 2026-09-20 現行契約: 通常の曜日・時刻はprobe hintに限定し、公式calendar、Card/Result link、page identity、
> 公式StartTimeを正本とする曜日非依存の取得契約を採用した。実装と検証の状態は
> [JRA公開状態駆動のRace取得](changes/20260920_event-driven-race-acquisition/README.md)を参照する。

> 2026-09-20 提案: CardからResultへ進む場合も、具体Result URLとRace identityを汎用開催一覧探索より
> 優先し、Card要否で保存済みResult locationを無視しない。既存の正常fallbackを維持する設計は
> [堅牢なRace Result遷移](changes/20260920_robust-race-result-navigation/README.md)を参照する。

JRAサイトの実装詳細を推測して依存する文書ではない。利用者がブラウザーで確認できる画面と
公式案内を契約境界とし、非公開API、内部JSON、偶然残っているURL、ページ間で意味が異なる
同名項目には依存しない。

情報源の優先順位は次のとおりとする。

1. 取得日時とURLを記録した実画面の確認結果
2. JRA公式のFAQ・利用案内
3. 実画面から作成したsemantic snapshotと回帰fixture
4. 現行コードの挙動

コードが本書と異なる場合、コードを根拠に本書を合わせてはならない。実画面で仕様を再確認し、
本書、change record、fixture、実装を同じ変更として整合させる。

## 2. 最重要の取得元制約

### 2.1 馬主

- レース時点の馬主名は **RaceCard（出馬表）を取得できた場合だけ** 取得する。
- RaceResult（レース結果・過去レース結果）から馬主名は取得できない。
- 現在の競走馬プロフィールに表示される馬主を、過去レース時点の馬主として代用しない。
- RaceResult、Horse profile、別レース、名前の一致から馬主を推測・補完しない。
- 過去資料やPDF等に馬主が掲載される場合があっても、それらは現行収集経路の正式な取得元ではない。
  新しい取得元に採用する場合は、別の変更記録で正確性、公開期間、利用条件、レース時点性を確認する。
- 出馬表が取得可能な間に馬主の保存に失敗し、その後出馬表が取得不能になった場合、現行の公式取得元からは
  復元不能である。無期限再試行ではなく「出馬表取得期間外・補正不能」として明示する。

### 2.2 画面種別を越えた代用の禁止

RaceCardとRaceResultは同じRaceを表しても別artifactである。URL候補、snapshot、成功記録、
取得フィールドを相互に代用しない。期待した画面種別と実画面が異なる場合は成功扱いにせず、
page identityを検証してから保存する。

## 3. 画面と取得情報

以下は当アプリが正式に扱う画面の契約である。「取得可」は現行parserが正式に扱う項目、
「取得不可・注意」は欠損時に別画面から推測してはならない項目を示す。

| 画面 | 主な入口・遷移 | 取得できる主な情報 | 取得不可・注意 |
| --- | --- | --- | --- |
| 開催カレンダー | JRAトップから月を選択 | 開催日、開催場への導線 | レースの公開済み判定には使わない |
| レース一覧 | 開催日・開催場を選択 | Race identity、レース番号、名称、発走時刻、Card/Resultへの導線 | 一覧にあることだけでCard本文の取得成功を保証しない |
| RaceCard（出馬表） | 直近のレース一覧から対象Raceを選択 | レース概要、出走馬、枠・馬番、馬名、性齢、斤量、騎手、調教師、馬体重、人気・オッズ、**馬主**、生産者、血統など画面に存在する項目 | 公開前、公開終了後、取消等で欠損し得る。古いRaceで存在すると仮定しない |
| RaceOdds（オッズ） | 出馬表周辺のオッズ導線 | Race identity、オッズとその観測時刻 | 出馬表を名乗る実ページはCard parserへ委ねる。最終オッズとは限らない |
| RaceResult（結果） | 直近結果一覧または過去レース検索 | 着順・異常区分、枠・馬番、馬名、性齢、斤量、騎手、タイム、着差、通過順位、上がり、馬体重、調教師、人気、天候・馬場・コース、賞金、払戻など画面に存在する項目。馬名cellに存在する検証済みHorse profile linkは主体identityとして取得できる | **馬主は取得しない**。Card固有情報を復元できる画面として扱わない。年代・レイアウトによりHorse linkがない場合はURLを推測しない |
| 過去レース検索 | 年月・開催・日を選びRaceResultへ遷移 | 古いRaceResultへの導線 | 「過去結果がある」ことは「過去の出馬表がある」ことを意味しない |
| Horse profile/history | 出馬表・結果等の馬リンク、競走馬検索 | 現在のプロフィールと掲載されている履歴Raceへの導線 | 現在の馬主を過去Race entryへコピーしない。履歴Raceは共有Race resourceへ正規化する |
| Jockey profile | 出馬表・結果等の騎手リンク | 騎手プロフィールと画面に存在する識別情報 | Race entryの事実とプロフィールの現在値を混同しない |
| Trainer profile | 出馬表・結果等の調教師リンク | 調教師プロフィールと画面に存在する識別情報 | Race entryの事実とプロフィールの現在値を混同しない |

項目はページに常に存在するとは限らない。欠損が正常なケースと、DOM変更・未知表記・誤画面を
区別し、値があるのに解析不能な場合は静かに欠落させず構造化エラーにする。

## 4. レース画面の遷移契約

```text
JRAトップ
  └─ 開催カレンダー
       └─ 開催日・開催場のレース一覧
            ├─ RaceCard（出馬表が公開されている期間だけ）
            │    └─ RaceOdds / Horse / Jockey / Trainer
            └─ RaceResult（結果公開後）
                 └─ Horse / Jockey / Trainer

JRAトップ
  └─ 過去レース検索
       └─ 年月 → 開催 → 日 → RaceResult
```

Navigationは次を守る。

- RaceCardとRaceResultの探索経路・期間判定を分離する。
- RaceCard探索は現在、対象日が「今日 - 5日」以降かを事前フィルタにする。この5日はアプリの
  負荷回避方針であり、JRAによる公開保証期間ではない。期間内でも実在確認が必要である。
- 事前フィルタより古いRaceではRaceCard探索自体を行わない。RaceResult収集は独立した経路で続行できる。
- RaceResultだけを取得する過去レースでは、該当Race identity、馬番、正規化馬名を検証したうえで、馬名cellの
  Horse profile linkを主体identityとして保存できる。linkはRaceResult由来の主体参照であり、Card artifactや
  Card固有フィールドの代用ではない。linkがない年代・行では、名前やURL形式からprofile URLを組み立てない。
- 直近・過去の判定だけで目的ページに到達したとみなさず、Race identityとpage kindを検証する。
- セッション依存の画面ではJRAの画面導線を用いる。直接URLは、実画面で安定性を確認し、取得後の
  identity検証を行える場合だけ候補として利用する。
- ブラウザー待機は目的画面の見出し・表・Race identity等、利用者に見える準備完了条件を用いる。
  `NetworkIdle`や非公開通信の完了を仕様にしない。
- Card上のRace番号要素と`レース結果`要素が別でも、同じRace文脈とvalidated CNAMEから直接Result URLを選べるようにする。
  `#`などのJavaScript controlはURL候補ではなくclick targetとして扱い、どの経路も遷移後にpage kindとRace identityを検証する。
- discoveryが後から同一RaceのResult URLを発見した場合、既存request/taskの有無にかかわらず、Cardとは別artifactのlocationとして
  冪等に統合する。このcorrective contractの承認・実装状態は
  [堅牢なResult navigation変更記録](changes/20260920_robust-race-result-navigation/README.md)を正本とする。

JRA公式FAQでは1986年以降のレース結果を案内し、直近と古い結果で入口が分かれる旨が説明されている。
また2000年以前は一部情報や表示条件が異なり得る。したがって、年代差を一つのDOM形式へ推測で
押し込まず、実例fixtureで検証する。

## 5. 保存・補正契約

- Card由来値とResult由来値にはprovenance（artifact種別、URL、観測時刻）を保持する。
- 後からCardを取得できた場合、既存Race entryへ非破壊・冪等にCard固有値をマージできる。
- null/空値で既存の検証済み値を消さない。異なる非null値の優先規則は項目と取得元を明示する。
- Collection taskの成功とデータ完全性を分離する。Cardが存在しないRaceResult収集は成功し得るが、
  ownerが取得済みであるとは扱わない。
- owner補正の対象は「Cardを実際に再取得できるRace」に限る。日付窓内であることは候補条件であり、
  適用成功の保証ではない。
- Card取得期間外のowner欠損は、失敗ジョブを増殖させず、取得元制約による未補正として表示する。
- 個別のerror jobを自動復旧、優先度変更、通知解決する処理とは分離する。

## 6. 変更検知と文書更新ワークフロー

次のいずれかに該当した場合、本書の更新要否を必ず判定する。

1. 本番または外部E2Eで、見出し、表、項目、URL、画面種別、遷移が従来と異なった。
2. parser、navigator、page kind判定、RaceCard探索期間、取得元、fallbackを変更する。
3. JRA画面変更が原因または疑いとなるproduction incidentが発生した。
4. semantic snapshotの構造fingerprintが変わり、抽出JSONまたはvalidation結果にも意味差が出た。
5. PDF、別公式画面、新しい公式API等を取得元へ追加する。
6. migrationまたはrepairが、従来取得できない項目を再取得・推測できる前提を置く。

更新は次の順で行う。

1. 発見日時、対象Race/subject、正規URL、期待page kind、実際のpage kindを記録する。
2. 認証情報や個人情報を含めず、診断APIでsnapshot、validation issues、structure fingerprint、抽出JSONを保存する。
3. 本書の画面表、取得元制約、遷移図、公開期間のどこが変わるかを更新する。
4. `docs/changes/yyyyMMdd_<change-name>/README.md`に影響、判断、受け入れ基準、補正可否を記録し、
   外部仕様が変わる場合は承認前にproduction codeを変更しない。
5. 実画面由来fixture、parser/navigationテスト、誤画面・欠損・年代差の回帰テストを追加する。
6. 保存済みデータへの影響を評価し、「再取得可能」「既存evidenceから補正可能」「公式取得元から復元不能」を分ける。
7. 対象テスト、診断、change-record validatorを実行し、Last verifiedと根拠を更新する。

structure fingerprintだけの変化では仕様変更と断定しない。一方、抽出結果の意味差がある場合は、
fingerprintが同じでも更新対象である。外部E2Eは異常を検出して証拠を残すまでとし、文書やparserを
自動更新しない。

### 6.1 レビュー時の必須確認

JRA収集関連の変更レビューでは、change recordに次のどちらかを明記する。

- `JRA site contract impact: Updated` とし、本書の変更箇所と検証証拠を記載する。
- `JRA site contract impact: None` とし、取得元・画面・遷移・公開期間に影響しない理由を記載する。

将来の自動化では、JRA parser/navigation/workflowまたはJRA由来migrationの変更時にこの記載がない
change recordをCIで拒否する。CIは仕様を推測して本書を書き換えない。

## 7. 診断手順と参照先

- HTML変更の診断APIとsnapshot取得: [JRA HTML変更の診断](24-jra-html-change-diagnostics.md)
- Parser/Navigatorの責務: [JRAスクレイピング層 実装指示書](23-jra-scraping-redesign.md)
- Resource、location、request/task: [収集基盤設計](26-collection-platform-design.md)
- JRA公式FAQ（レース結果の掲載範囲・入口）: <https://www.jra.go.jp/faq/pop02/1_6.html>

## 8. 既知の未確定事項

- JRAはRaceCardの公開期間を当アプリの「5日」として保証していない。5日は探索コストを抑える現行方針である。
- 年代、開催、レース種別、取消・除外・中止等で表示列が変わるため、代表fixtureを継続的に増やす。
- JRA公式の別媒体に同じ名称の項目があっても、Race時点の意味と収集経路を検証するまで代替元にしない。
## 開催中止・代替開催の収集契約

- 曜日を開催可否の判定に使用しない。通常平日、祝日、週末を同じ公式ページidentityで扱う。
- 予定日のCardが取得不能でも、結果確認時刻後はResult段階へ進む。結果確認時刻前は従来どおりCard公開待ちを継続する。
- Card URLの`CNAME`から年・競馬場・開催回・開催日番号・R番号を取得し、最大7日先で同一identityを探索する。旧URLエラーだけでは代替と断定しない。
- 代替先発見時は旧taskを`MeetingRescheduled / NotApplicable`で終端し、実施日の`race-detail` Recovery requestを冪等作成する。新Card保存後に旧domain Raceへreplacement lineageを記録する。
- wake group内の`ActiveElsewhere`は当該taskだけを再配信対象とし、後続taskとbatch completionを継続する。

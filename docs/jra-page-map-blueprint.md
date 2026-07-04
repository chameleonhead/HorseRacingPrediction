# JRA Page Map Blueprint

> このコンポーネントは Collector の実装詳細である。全体構成・責務境界は [collector-design.md](collector-design.md) を参照。

## 目的

JRA サイトから構造化データを継続的に取得するために、ページ遷移とページ解析を分離する。

- 遷移は JraSiteDataCollector と Browser が担う
- 判定は JraPageKindDetector が担う
- 構造化抽出は structured page parser 群が担う
- 後続の業務処理は typed page model と extraction envelope を受け取る

この分離により、レイアウト変更が発生しても「どのページで崩れたか」と「どのフィールドが取れなくなったか」を warnings / errors として局所化できる。

## 最終形の責務分割

1. Navigation Layer

- クリック、戻る、URL 遷移、セッション維持を扱う
- ページ内容の意味解釈はしない

2. Detection Layer

- URL、title、headings、mainText から page kind を判定する
- 判定不能時は Unknown を返す

3. Parser Layer

- PageSnapshot から typed page model を復元する
- parser は pure function として扱い、副作用を持たない
- parser は issues、confidence、recommendedNextLinks を返す

4. Extraction Layer

- parser の結果を domain/application 向け DTO に橋渡しする
- 例: 開催日程ページ -> JraRaceScheduleCalendar

5. Workflow Layer

- 収集の開始点、対象期間、再試行方針、保存順序を制御する

## 論理ページマップ

### KeibaMenu

- URL の代表: /keiba/ , /keiba/index.html
- 役割: 競馬機能への入口
- 抽出対象:
  - 開催日程
  - 出馬表
  - オッズ
  - レース結果
  - 今週の注目レース
  - 馬場情報
- 診断:
  - 開催日程リンクが見つからない場合は Error

### ScheduleCalendar

- URL の代表: /keiba/calendar/ , /keiba/calendar/may.html
- 役割: 月別の開催日カレンダー
- 抽出対象:
  - 年
  - 月
  - 月メニュー
  - 日付ごとの開催場一覧
  - セル生テキスト
- 診断:
  - 年が取れない場合は Warning
  - 月が取れない場合は Error
  - 開催日セルが 0 件の場合は Warning

### HoldingList

- 到達経路: KeibaMenu -> 出馬表
- 役割: 1回東京1日 のような開催ラベル一覧

### RaceList

- 到達経路: HoldingList -> 個別開催
- 役割: 当日レース一覧

### ThisWeekFeature

- URL の代表: /keiba/thisweek/
- 役割: 今週末の注目レース一覧
- 抽出対象:
  - 掲載期間
  - 各重賞レースの名前、日付、競馬場、距離
  - G1 特設トップへの導線
  - 出馬表、出走馬情報、データ分析などの導線

### GradeOneSpecial

- URL の代表: /keiba/g1/{slug}.html
- 役割: G1 特設トップページ
- 抽出対象:
  - レース名
  - 開催日
  - 競馬場
  - 距離
  - タブ導線
  - 関連ニュース

### RaceCard / Odds / Result

- URL の代表: JRADB accessD / accessO / accessP
- 役割: レース本体データ
- 実装状況:
  - 既存 extractor / scraper 済み

## Parser 契約

すべての structured parser は次を返す。

- success
- typed data
- issues
- confidence
- recommendedNextLinks
- error

recommendedNextLinks には、LLM やワークフローが次に辿る価値の高い導線だけを入れる。

NavigationMode は次を区別する。

- DirectUrl: 単体で安全に開ける URL
- CurrentSessionAction: 現在セッション文脈でクリックした方が安定する導線

## レイアウト変更への耐性

- URL だけに依存せず、title / headings / mainText / tables で裏取りする
- テキスト比較は normalize を通す
- calendar cell は構造化値だけでなく RawText も保持する
- 診断コードを固定し、崩れ方を観測できるようにする

## 段階的な完成ロードマップ

### Phase 1

- KeibaMenu parser
- ScheduleCalendar parser
- HoldingList parser
- RaceList parser
- JraSiteDataCollector の開催日程収集を calendar 起点へ変更

### Phase 2

- ThisWeekFeature parser
- GradeOneSpecial parser
- structured page extraction API の公開
- verifier での可視化

### Phase 3

- confidence の洗練
- directNextLinks の公開
- snapshot golden tests の整備
- JraSiteDataCollector の shortcut navigation を structured next links ベースへ寄せる

## 現在の復旧対象

今回の復旧では、以下を repository に戻す。

- structured page model
- structured page parser
- link relation 定数
- page map blueprint
- thisweek / G1 special page の page kind

これを基点に、JraSiteDataCollector と plugin / verifier への再接続を進める。
# JRA Task Agent — 設計仕様書

## 概要

`JraTaskAgent` は JRA 公式サイトを「人間のように」操作する、状態保持型のタスクエージェントです。  
単なるスクレイパーの集合ではなく、**現在ページを起点にして最短経路で目的の情報へ到達し、必要があれば戻る**という、人間のブラウジング操作をそのまま抽象化します。

Playwright セッションをエージェント自身が保持し、`IAsyncDisposable` で安全に終了できます。

---

## 設計思想

### 問題意識

従来の設計:
- スクレイパーが URL を受け取って 1 ページを解析するのみ
- 遷移ロジックが呼び出し側（Verifier）に散在
- 「出馬表を取った後でオッズを取る」「騎手情報を見て戻る」といった連続操作が書けない

新しい設計:
- エージェントが **セッション＋コンテキスト** を持つ
- 呼び出し側は「何を取りたいか」だけを依頼する
- エージェントが「どこから辿るか」「何をクリックするか」「戻るか」を自律的に判断する

### 核心となる操作原則

1. **現在ページのリンク・ボタンを最優先**で使う
2. 同一レースのページ間（出馬表↔オッズ↔結果）は**タブ遷移**で移動
3. プロフィール（馬・騎手・調教師）は名前リンクをクリックし、取得後**GoBack で元のページに戻る**
4. 現在ページで目的地に到達できない場合のみ、エントリーポイントから再ナビゲーション
5. 失敗したクリックを記憶してリトライを防ぐ

---

## アーキテクチャ

```
┌─────────────────────────────────────────────┐
│                JraTaskAgent                 │
│  (IAsyncDisposable / Playwright セッション)  │
│                                             │
│  ┌──────────────┐  ┌──────────────────────┐ │
│  │ SessionMemory│  │  NavigationPlanner   │ │
│  │ - currentUrl │  │  - 遷移ヒント辞書    │ │
│  │ - pageKind   │  │  - リンク候補選択    │ │
│  │ - race ctx   │  └──────────────────────┘ │
│  │ - back stack │                           │
│  └──────────────┘  ┌──────────────────────┐ │
│                    │  ExtractorRegistry   │ │
│  ┌──────────────┐  │  - PageKind → 抽出器 │ │
│  │PageKindDetect│  └──────────────────────┘ │
│  │ URL + title  │                           │
│  └──────────────┘                           │
└─────────────────────────────────────────────┘
         ↓ IWebBrowser (PlaywrightWebBrowser)
```

---

## コンポーネント詳細

### JraTaskAgent（公開 API）

```csharp
await using var agent = await JraTaskAgent.CreateAsync();

// レース情報
await agent.RequestRaceCardAsync(date, racecourse, raceNumber);
await agent.RequestOddsAsync(date, racecourse, raceNumber);
await agent.RequestRaceResultAsync(date, racecourse, raceNumber);

// エンティティプロフィール（現在ページからリンクを辿る）
await agent.RequestHorseProfileAsync("デアトゥバトル");
await agent.RequestJockeyProfileAsync("松若 風馬");
await agent.RequestTrainerProfileAsync("水野 貴広");

// 低レベル操作
await agent.ExtractCurrentPageAsync();
await agent.FollowAsync("オッズ");
await agent.BackAsync();
```

### JraPageKind（ページ種別）

```
Unknown / RaceCard / Odds / Result /
HorseProfile / JockeyProfile / TrainerProfile /
RaceList / HoldingList
```

JRA URL パターンで判定（URLがない場合はページタイトル・本文で補完）:

| URL パターン | ページ種別 |
|---|---|
| `accessD.html` / `/syutsuba` | RaceCard |
| `accessO.html` | Odds |
| `accessP.html` | Result |
| `accessU.html` / 競走馬情報 | HorseProfile |
| `accessJ.html` / 騎手情報 | JockeyProfile |
| `accessT.html` / 調教師情報 | TrainerProfile |

### JraNavigationPlanner（遷移計画）

同一レース内の遷移ヒント辞書:

| 現在ページ | 遷移先 | クリック候補 |
|---|---|---|
| RaceCard | Odds | ["オッズ"] |
| RaceCard | Result | ["払戻金", "レース結果"] |
| Odds | RaceCard | ["出馬表"] |
| Odds | Result | ["払戻金", "レース結果"] |
| Result | RaceCard | ["出馬表"] |
| Result | Odds | ["オッズ"] |

クリック候補を Actions（ボタン）→ Links の順で検索する。

### JraSessionMemory（セッション記憶）

- 現在 URL / ページ種別
- 現在のレースコンテキスト（日付・競馬場・レース番号）
- URL の戻り先スタック
- 失敗したクリックの記録（重複クリック防止）

### Extractor Registry（抽出器レジストリ）

| ページ種別 | 抽出器 | 返却型 |
|---|---|---|
| RaceCard | JraRaceCardExtractor | JraRaceCardData |
| Odds | JraOddsExtractor | JraOddsResult |
| Result | JraRaceResultExtractor | JraRaceResultSummary |
| HorseProfile | JraProfileExtractor | JraEntityProfile |
| JockeyProfile | JraProfileExtractor | JraEntityProfile |
| TrainerProfile | JraProfileExtractor | JraEntityProfile |

---

## エントリーナビゲーション（特定レースへの到達）

1. `https://www.jra.go.jp/keiba/` に移動
2. 「出馬表」をクリック
3. 開催ラベル一覧を取得（例: "2回東京5日", "3回京都5日"）
4. 競馬場名でフィルタリング
5. 各候補開催をクリック → ページ内に対象日付が含まれるか確認
6. 対象日付の開催を発見したら、`{N}レース` ボタンをクリック

thisweek/ 経由の場合:
- 開催ボタンが見つからなければ「出馬表」ボタン直クリックを試みる（重賞レース直リンク）

---

## データモデル

### JraExtractionEnvelope（共通ラッパー）

```csharp
record JraExtractionEnvelope(
    bool Success,
    JraPageKind PageKind,
    string SourceUrl,
    JraNavigationTrace Trace,   // クリック経路・所要時間
    object? Data,
    string? Error);
```

`Data` の取り出し: `envelope.GetData<JraOddsResult>()`

### JraNavigationTrace

```csharp
record JraNavigationTrace(
    IReadOnlyList<string> Steps,   // ["navigate: keiba/", "click: 出馬表", ...]
    TimeSpan Elapsed);
```

---

## 実装ファイル構成

```
src/HorseRacingPrediction.Agents/JraAgent/
├── JraPageKind.cs               # ページ種別 enum
├── JraPageKindDetector.cs       # URL + スナップショットから種別判定
├── JraAgentModels.cs            # Envelope, Trace, OddsResult, ResultSummary, EntityProfile
├── JraSessionMemory.cs          # セッション内状態管理
├── JraNavigationPlanner.cs      # 遷移ヒント辞書・クリック候補選択
├── IPageExtractor.cs            # 抽出器インターフェース
├── JraExtractorRegistry.cs      # PageKind → IPageExtractor マッピング
├── JraRaceCardExtractor.cs      # 出馬表抽出
├── JraOddsExtractor.cs          # オッズ抽出
├── JraRaceResultExtractor.cs    # レース結果抽出
├── JraProfileExtractor.cs       # 馬・騎手・調教師プロフィール抽出
└── JraTaskAgent.cs              # 公開 API + セッション管理
```

---

## バージョン履歴

| バージョン | 日付 | 内容 |
|---|---|---|
| v1.0 | 2026-05-08 | 初版: 出馬表・オッズ・結果・プロフィール取得、エントリーナビゲーション |

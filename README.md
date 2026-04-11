# HorseRacingPrediction

CQRS+ES と ASP.NET Core Web API を前提にした競馬予想アプリケーションの設計・実装用リポジトリです。

## 設計ドキュメント

- [docs/domain-design.md](docs/domain-design.md): CQRS+ES 前提の競馬予想ドメイン設計
- [docs/automation-design.md](docs/automation-design.md): 自動処理の責務設計

## 現時点の方針

- 収集対象はレース、馬、騎手、調教師、およびそれらに紐づく一次テキスト情報
- 正規化済みの構造化データと、原文・元データを両方保持する
- レース前の予想とレース後の結果を同一ライフサイクル上で管理する
- 書き込みモデルは CQRS + Event Sourcing を前提に設計する
- ASP.NET Core Web API と API キー認証を前提にする
- 予想は同一レースに複数登録できるようにする

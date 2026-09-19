# レース収集artifactの表示

対象: desktop 1280px / narrow 360px。既存Detail Page patternを維持する。

```text
レース詳細の収集                          [一部待機]
レース・JRA・20260919:Hanshin:10          [更新]

収集処理の概要
  出馬表   取得済み        最終取得 09/18 18:34
  結果     発走待ち        公式発走 15:10 / 次回確認 15:15
  障害     未解決なし      復旧操作はこの画面から自動実行されません

  総実行 7回   公開確認 7回   技術的な失敗 0回

[概要] [依頼履歴] [タスク履歴] [試行履歴] [障害履歴] [取得先] [管理]
```

narrowでは各phaseを縦積みにし、「出馬表」「結果」「次回確認」とPrimary/Secondary actionを残す。
URL、batch ID、Lambda ID等は概要へ常設せず、既存の試行詳細で展開する。

状態例:

- Loading: phase行にskeletonを表示し、過去値を成功値として見せない。
- Empty/legacy: `状態を再判定します`と表示し、未取得と断定しない。
- API error: `状態を読み込めませんでした`と再読込操作を表示する。
- Card rejected: 同じRaceジョブ内で出馬表行を`保存エラー`とし、Result facetをそのまま表示する。
- Card persisted/result unavailable: 出馬表行を`取得済み`、結果行を`公開待ち`とする。

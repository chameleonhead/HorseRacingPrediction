# JRA HTML 変更の診断

画面ごとの取得可能項目、遷移、取得元の禁止条件、文書更新条件は
[JRAサイト収集契約](27-jra-site-collection-contract.md)を正本とする。本診断で意味のある差分を確認した場合は、
snapshotの追加だけで終えず、同文書の「変更検知と文書更新ワークフロー」に従う。

Collector をローカル起動した状態で、抽出対象 URL を診断 API に渡す。

```text
GET http://localhost:61692/api/tools/jra-json?url=<URL encoded JRA URL>&includeSnapshot=true&headless=true
```

レスポンスでは次を確認する。

- `validationIssues`: 必須項目が取得できなければ、項目名を含む診断コードを返す。
- `structureFingerprint`: ページ種別、テーブル数、見出し構成から生成した SHA-256。正常時の値と変わった場合、HTML 構造変更の可能性がある。
- `snapshot`: `includeSnapshot=true` の場合に取得時点の HTML を保存する。抽出失敗時の再現テスト作成に使用する。

`structureFingerprint` の変化だけでは障害とは判定しない。JRA の表示内容によってテーブル構成が変わる可能性があるため、`validationIssues` と抽出 JSON を併せて比較する。

確認手順:

1. 同じページの正常時 fingerprint を記録する。
2. 欠損が発生したら診断 API を再実行する。
3. `validationIssues`、fingerprint、snapshot を正常時と比較する。
4. snapshot をテスト fixture として追加し、抽出処理を修正する。

今回確認した結果ページでは、現行 HTML から Grade、馬場、距離、方向を抽出できた。このため、これらの欠損は HTML 変更ではなく、レース作成 API が収集済みメタデータを保存していなかったことが原因だった。

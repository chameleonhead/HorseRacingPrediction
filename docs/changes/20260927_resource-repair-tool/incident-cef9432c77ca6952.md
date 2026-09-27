# CEF9432C77CA6952 読み取り診断

- Observed: 2026-09-27 15:34 JST前後。production GETと公式ページのread-only probe。
- Group: horse-profile / HttpRequestException / HTTP409、3件。
- Mutation: なし。再要求/停止解除/補正なし。プログラム修正の承認とは扱わない。

| 馬 | Task | 失敗JST | 保存プロフィールAcquiredAt JST | 父 |
| --- | --- | --- | --- | --- |
| ナムラアラバイ | 2110c9d0-f5ab-440e-845a-1ed064e62c99 | 14:53:54.688 | 14:53:54.149 | マテラスカイ |
| メイプルカモーン | 76e54860-c827-4ab4-ac1d-a3b6965a8653 | 14:59:56.451 | 14:59:56.325 | ゴールドシップ |
| アッパレヤ | 5083a8ff-1da9-4830-9636-fc4ea55faf01 | 15:04:49.340 | 15:04:48.944 | マテラスカイ |

全3resourceのrequest/task/attempt総件数は各1。batchはcf4b719f-4d96-4791-a396-f546f9e8e24d、a91d4739-7587-4205-afba-dc5b387719fc、c7f29074-bddf-4a08-a45e-e561f4a19ea9をGET照合。2番目は先行3件成功後の失敗、他はbatch先頭。時刻だけで失敗した子処理を確定しない。

## 確認できた事実

各馬の `/api/admin/subjects/Horse/{id}/profile` は当該attempt内のAcquiredAtと正しい名前/生年月日/公式identityを返す。現在の公式ページを既存Probeで取得しても同じ名前/生年月日を解析できた。プロフィール保存そのものが全くできていない状態ではない。

父馬は本番でそれぞれ1件存在し、保存済み公式identityあり。

- マテラスカイ: horse-1e8c36ff-ad99-5d50-a8be-2926c1ab26a8、/JRADB/accessU.html?CNAME=pw01dud002014110060/B1、AcquiredAt 02:55:28。
- ゴールドシップ: horse-f8bf5dd5-6eae-5f4d-9286-a757381e07c4、/JRADB/accessU.html?CNAME=pw01dud002009102739/44、AcquiredAt 03:07:12。

`JraSubjectProfileCollectionHandler`はprofile保存後、調教師→父→母の順で関連主体をUpsertして収集要求する。父は名前のみで `UpsertHorseAsync(name, null, null, null)` を呼ぶ。`HttpDataCollectionWriteService` は `/api/identity/horse` にSourceIdentityなしで問い合わせる。

`CollectionIdentityResolver.ResolveHorse`は名前一致の既存馬に公式identityがある場合、SourceIdentityなしの解決を `HorseIdentityEvidenceRequired` で拒否。endpointは409を返す。これは同名別馬の誤結合防止であり、保護を外すべきではない。

## 推定原因と限界

本番データと現行コードから、父馬の名前のみUpsertがこの保護に衝突する経路が有力で、現在のデータ条件ではその呼出しは409になる。既存API統合テスト `HorseProfile_EquivalentOfficialUrlsResolveLegacyId_ButNameOnlyAndDifferentSourceAreRejected` をRelease/no-buildで実行し1件成功（公式identityあり既存馬への名前のみ要求が409、根拠付き要求は成功）。

ただし過去attemptは汎用HttpRequestExceptionだけで、HTTPの呼出先、409本文、子主体、失敗stageを保持していない。`EnsureSuccessStatusCode`が本文を業務エラーとして伝播しない。本番各件の正確な発生endpoint/codeは保存証拠から復元できず、父Upsert前の別409がなかったと断定できない。既存テストはguardの再現であり、3頭の本番worker全経路再現ではない。

## 対応案（未実装）

1. 血統参照の生産者が公式根拠を保持して同一性解決へ渡す。名前だけなら未同定参照として保留し、既存馬を推測で選ばない。
2. profile保存と関連主体発見を段階として記録。既知の子同定不足は理由付き局所保留にし、未知の409を一律無視/全成功化しない。
3. 内部APIの安全なerror code/呼出先path/子resource/stageを伝播・保存。秘密情報や生のrequest headerは保存しない。
4. 実profile保存→父の解決409→分類/保存/再開の統合反例を追加。既存関連主体・公式identityなし/あり・同名別馬を含める。

補正ツールだけではこの通常収集経路の不整合は解消しない。別の変更設計・承認が必要。収集状態GETはisPaused=false、updatedAt=15:32:09.969+09:00であり、こちらから再開していない。これを復旧成功とは扱わない。

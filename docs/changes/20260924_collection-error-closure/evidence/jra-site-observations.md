# JRA公式サイト確認記録

2026-09-24 JST、公開HTMLとPlaywrightで確認。本番DBへの書込なし。現在契約の正本は [JRA site collection contract](../../../27-jra-site-collection-contract.md)。

| 公式情報源 | 観察・利用範囲 |
| --- | --- |
| https://jra.jp/keiba/calendar2026/2026/9/0921.html | 日付付き競馬番組。中山表captionは4回中山7日、単一通知行に全開催中止と9/22代替を明示。阪神表は4回阪神7日、headerと通常12レース。 |
| https://jra.jp/news/202609/092105.html | 9/21中山の9/22代替開催を公式確認。出馬表内容・番号不変の案内は、保存先Race IDを旧日にする根拠にはしない。 |
| https://www.jra.go.jp/keiba/calendar2026/2026/9/0922.html | 代替日の独立した番組。旧日と混同しない。 |
| https://www.jra.go.jp/keiba/calendar/ | 日セルは開催だけでなくニュースも含む。現parserは中止ニュース内の中山も収集候補に含める。 |

## 構造・原因・安全境界

- 日付付きh1のほかにロゴ用h1もある。「h1は1個」と仮定しない。日付付き競馬番組見出しの一意性を確認する。
- 開催の境界はtable/caption。snapshotはcaptionと同tableのrows/cellsを保持する。一般注意書きにも中止・延期の語があるため全ページkeyword判定は禁止。
- 月間カレンダーは開催領域とニュース領域を分けて表示するが、現CalendarPageParserはcell全文からcourseを抽出する。今回、候補の不在を成功にせず、公式の日別中止証拠で対象だけ除外する。非公開JSONは本番取得元として採用しない。
- 公開日別番組リンクの形 `/keiba/calendar{year}/{year}/{month}/{MMdd}.html` は読取候補URLの作成だけに使用。最終公式host/path、表示日、開催caption、同table内の通知を再検証する。JRADBセッションURLの推測とは異なる。
- 対象のCard/Result探索が表示範囲外になった時に限定して確認する。通常開催、他場の中止、単一レース中止、可能性表現、重複table、日付不一致、誤hostは取消証拠にしない。未知状態・通信障害は失敗を維持する。
- 全候補中止はNotApplicable。他場成功との混在は通常取得と除外を区別し、attempt PageIdentificationへ日付/course/開催番号/日番号/公式URL/件数を保存する。後続対象の通常例外でも既確認の証拠と元のfailure分類を残す。実行キャンセルは既存executorのキャンセル処理を優先し、途中証拠は保証しない。
- 代替日は公式日程にある独立日として収集。日付+1推測・既存ID/alias書換えはしない。将来の未対応レイアウトや表現は失敗を維持し、原証拠を確認して見直す。

## 検証

`JraMeetingCancellationParserTests` は日付・host・曖昧文言・他場・重複・通常race rowsを反証する。
`OfficialMeetingCancellation_SeparatesCoursesAndReplacementDay` は実サイトの中山中止/阪神通常/翌日中山を確認。
`OfficialCancelledDay_RealScheduleNavigatorParserAndHandler_ContinueHanshin` は実際のカレンダー→navigator→parser→discoveryを通し、中山旧日requestゼロ、阪神12件を確認した。
初回実サイト試験はロゴh1の存在で失敗し、日付付き番組h1選択へ修正後に再合格。公開状態依存の試験はExternalで通常CIから分離する。本番復旧証拠の代替ではない。
実行コマンドと全体結果は [verification](verification.md) に集約する。

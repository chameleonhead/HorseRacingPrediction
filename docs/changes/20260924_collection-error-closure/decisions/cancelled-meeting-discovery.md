# 開催中止を予定カレンダーから通常開催と誤認する問題

- Status: Approved
- Owner: Main
- Parent: T2f / AC1, AC4, AC5, AC7。2026-09-24「お願いします。JRAのサイトに関する情報も記録をお願いします」により追加契約を承認。

## 根拠

2026-09-24 19:08 JST production GETでpipeline paused、最後の停止は9/23 23:51 JST、race-discovery task `8886fd12-8ace-4786-a4ab-1ac6405d96e4`。9/21 Nakayamaの開催選択不在。履歴URLは未保存。
最新mainの実サイト試験でもHistorical検索まで進んで同じ対象が不在。同日Hanshin、通常の直近Result、2020年Resultは成功。

- https://jra.jp/keiba/calendar2026/2026/9/0921.html : 4回中山7日の節に中止・9/22代替を記載。同じページの4回阪神7日は通常開催。
- https://jra.jp/news/202609/092105.html : 同一開催の9/22代替、出馬表内容不変を公式確認。
- CalendarPageParserは日番号と競馬場文字列だけを抽出。JraRaceDateはDate/Coursesのみ。日別番組の中止状態を読まず、discoveryが中止日のCard→Result→Historical探索を行い、例外が全体停止に至る。

## 追加する契約案

1. 公式の日付付き番組ページから、日付・競馬場・開催番号/開催日番号で区切った開催状態を読む。月間cellだけ、ページ全体の「中止」だけでは判定しない。
2. 明示中止の対象だけ旧日の探索対象外とし、対象と公式根拠を診断へ残す。正常取得成功件数には足さない。同日他場の処理は継続。
3. 代替日は公式日程に掲載された独立した日として通常収集する。日付+1推測、旧Race IDへの新日結果保存、既存ID/aliasの書換えをしない。既存Raceのreschedule lineage補正は既存の別gateを守る。
4. 明示状態不明、開催identity不一致、誤ページ、広域アクセス障害は対象外判定にせず従来の安全なエラーへ戻す。
5. 過去の失敗履歴を削除しない。本番配備/対象限定Recovery/resumeは別途許可後。

## 実装責任と検証

Mainが日別状態read model・Navigator/Parser・discovery契約を所有（public contractと同定判断のため）。凍結後に独立fixtureのみworker候補。独立reviewはread-only。
実ファイル境界は日別parser設計時に確定し、その前にcoding委譲しない。

受入反例: 中山中止+阪神通常の同日、翌日代替、通常開催、中止語が他場だけ、曖昧本文、異なる日付/開催番号、対象不明、通信失敗。旧日の中山Race requestを生成しないこと、阪神/新日の中山requestを正しい日付で生成することを実経路で検証。
本番では選択したdiscoveryだけRecoveryし、その終端・独立後続task成功・再停止なしを確認する。

## 懸念・選択

予定カレンダー単独で中止を推測する案、一律JraNavigationException隔離、全日中止、特定日のhard-codeは却下。公式日別ページへの限定した追加読取が必要になる。曖昧な公式表示は止めるため、将来全ての中止表記を無条件で救済する保証はしない。
User disposition: 上記追加契約を承認。本番配備・復旧操作の別gateは維持。

## Pre-implementation review

Main、2026-09-24。T2fの中止開催sliceをIn progressにする。実サイトHTMLで開催の区切りはheadingではなくtable captionであることを確認。日付heading、公式URL、対象courseを含む一意のcaption、そのtable内の明示中止文だけを証拠にする。一般注意書きの「中止・延期」は使わない。
MainがModelsの取消証拠、専用Parser、IJraNavigator/JraNavigator、discovery handlerと関連testを排他所有。公開契約・identity・失敗境界の統合が不可分のためcodingはLead保持。explorerは公式HTML/導線のread-only確認のみ。純粋parser反例、navigator失敗、handlerのCard→Result fallback失敗と混在開催、実サイト読取を検証後、非External全体testを実行する。未知状態は元の失敗を維持し、本番writeを行わない。

# Admin migration request mock

Proposed route: `/settings/horse-id-migration`. This is an operator console, not an automatic repair page.

```text
馬ID移行 — 仮説の確認

これは名前一致だけでは本人確認できない、明示承認型の物理移行です。
プレビューと実行依頼の保存だけではデータは変更されません。
保存後、別途 GitHub Actions の手動実行が必要です。

候補（初期状態ではすべて未選択）
□ horse-d198b3e2-771f-5dcb-94dc-a739e98b6119
  ナムラアラバイ → jra-horse:2024100828
  source: 仮説。表示名一致だけでは確定できません。DOB / profile source 不足。

□ horse-ba9e991c-56e3-5ecd-a392-69d125afed42
  ノヴァアクレイム → jra-horse:2024108210
  source: 仮説。表示名一致だけでは確定できません。DOB / profile source 不足。

□ horse-923a0fdb-5c66-57ea-93a4-94dee4998b67
  ボクマダネムイヨ → jra-horse:2023102553
  source: 仮説。legacy normalized-name candidate も異なります。DOB / profile source 不足。

[選択内容をプレビュー]

プレビュー結果: 実行不可 — 競合 / 未対応データ / 不完全な証拠があれば保存できません。
対象ストリーム、型付き参照件数、変換対象、未確認範囲、バックアップ対象を表示。

[この固定内容を実行依頼として保存]  (全ブロッカー解消後のみ有効)

保存結果: AwaitingActions
依頼 GUID: <画面内に表示する不透明なID>
source data は未変更です。
次の操作: 内容を確認して、既存の GitHub Actions workflow をこの GUID で
手動 dispatch してください。dispatch 前に Running と表示しません。

状態: AwaitingActions / Running / Succeeded / Failed / Partial / Interrupted
進捗: <bounded counts and safe reason codes>
[状態を更新]  (手動。自動pollなし)
```

Safety states: preview never writes source stores; stage saves only the immutable manifest and request; workflow claim is the only transition to `Running`. Duplicate claims are rejected. A partial/interrupted operation remains paused and cannot be resumed by this screen. After restart, the page reads journal state by GUID; it never repeats the migration automatically. A separate operator decision is required before using the existing pipeline-resume action after all postchecks pass.

The page never displays raw event payloads, metadata, URLs, database paths, SQL, shell arguments, secrets, or free-form exceptions. Candidates are not prechecked, and accepting one is an explicit operator decision recorded in the protected journal. A destination collision, unknown affected schema, unsupported event, ambiguous source/root, or incomplete backup set disables both staging and workflow execution. The same-name/different-source case remains a distinct horse, not an automatic merge.

Responsive behavior: desktop uses a compact candidate/evidence table; at 390px each candidate becomes a labeled card. Keyboard users can inspect evidence and explicitly select rows, preview, stage, then manually refresh status. No background polling or hidden auto-selection is used.

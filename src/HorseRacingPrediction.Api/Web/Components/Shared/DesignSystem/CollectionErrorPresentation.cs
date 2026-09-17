namespace HorseRacingPrediction.Api.Web.Components.Shared.DesignSystem;

public sealed record CollectionErrorAdvice(string Title, string Explanation, string SuggestedAction);

public static class CollectionErrorPresentation
{
    public static CollectionErrorAdvice Describe(string? code, string? message)
    {
        var value = $"{code} {message}";
        if (value.Contains("SubjectNotInProviderDirectory", StringComparison.OrdinalIgnoreCase))
            return new("JRA公開名簿の対象外です", "業務データ上の主体は存在しますが、JRAプロフィールの収集対象ではありません。", "再取得は不要です。プロフィール非該当として処理を終了します。");
        if (value.Contains("SubjectNotIdentified", StringComparison.OrdinalIgnoreCase)
            && value.Contains("主体名がない", StringComparison.Ordinal))
            return new("識別に必要な名前がありません", "発見元から主体名を引き継げなかったため、安全に検索できません。", "再取得を繰り返さず、発見元データまたは主体情報を補正してください。");
        if (value.Contains("SubjectNotIdentified", StringComparison.OrdinalIgnoreCase)
            && value.Contains("複数", StringComparison.Ordinal))
            return new("同名候補を一意に特定できません", "公開検索に同名候補が複数あり、現在の根拠だけでは本人を選べません。", "自動再取得は行いません。生年月日または公式識別子を確認して補正してください。");
        if (value.Contains("SubjectNotIdentified", StringComparison.OrdinalIgnoreCase)
            && value.Contains("産駒", StringComparison.Ordinal))
            return new("血統説明から誤って作られた対象です", "馬名ではなく血統欄の説明文字列が収集対象として登録されました。", "再取得せず、自動補正でこの対象を停止します。");
        if (value.Contains("SubjectNotIdentified", StringComparison.OrdinalIgnoreCase))
            return new("公開プロフィールを一意に特定できません", "名前、登録区分、所属、公式識別子のいずれかが公開情報と一致しませんでした。", "同じ抽出仕様での再取得は行わず、修正revisionによる自動復旧または識別情報の確認を待ってください。");
        if (value.Contains("見出しを確認できません", StringComparison.Ordinal))
            return new("公開ページの構造を確認できません", "期待するプロフィール見出しがなく、別ページまたは画面変更の可能性があります。", "通常の再取得を繰り返さず、抽出処理の修正revisionを配備してから復旧してください。");
        if (value.Contains("ERR_INSUFFICIENT_RESOURCES", StringComparison.OrdinalIgnoreCase))
            return new("ブラウザーのリソースが不足しました", "ページ取得用ブラウザーが利用可能なメモリなどを確保できず、読み込みを完了できませんでした。", "同じ原因の対象をまとめて再取得してください。再発が続く場合は実行時のメモリ使用量とバッチ件数を確認します。");
        if (value.Contains("Timeout", StringComparison.OrdinalIgnoreCase))
            return new("ページ取得が時間内に完了しませんでした", "通信またはページ表示に時間がかかり、制限時間を超えました。", "一時的な混雑の可能性があるため再取得してください。繰り返す場合は対象URLと処理時間を確認します。");
        if (value.Contains("UnexpectedPage", StringComparison.OrdinalIgnoreCase) || value.Contains("Resource ID", StringComparison.OrdinalIgnoreCase))
            return new("想定と異なるページを検出しました", "HTTP応答は得られましたが、対象の種類またはIDが依頼内容と一致しませんでした。", "取得先URLを確認し、保存済みURLが古い場合は探索を経由して再取得してください。");
        if (value.Contains("NotYetAvailable", StringComparison.OrdinalIgnoreCase))
            return new("情報がまだ公開されていません", "対象ページは開催前などの理由で、現時点では公開されていません。", "公開予定時刻以降の自動再試行を待つか、必要に応じて後から再取得してください。");
        if (value.Contains("429", StringComparison.OrdinalIgnoreCase) || value.Contains("AccessLimited", StringComparison.OrdinalIgnoreCase))
            return new("アクセスが制限されました", "取得先サイトから一時的なアクセス制限を受けました。", "間隔を空けた自動再試行を待ってください。短時間に繰り返し実行しないでください。");
        if (value.Contains("Parse", StringComparison.OrdinalIgnoreCase))
            return new("ページ内容を読み取れませんでした", "ページは取得できましたが、必要な項目を抽出できませんでした。", "対象URLとページ判定結果を確認し、画面変更がある場合は抽出処理を修正してから再取得してください。");
        if (value.Contains("503", StringComparison.OrdinalIgnoreCase) || value.Contains("500", StringComparison.OrdinalIgnoreCase))
            return new("一時的なサーバーエラーが発生しました", "取得先または管理APIが一時的に処理できない状態でした。", "少し時間を空けて再取得してください。同じ時間帯に集中している場合はサービス状態を確認します。");
        return new("収集処理を完了できませんでした", "記録された技術情報だけでは原因を自動分類できませんでした。", "対象URL、発生時刻、エラーコード、実行バッチを確認してから再取得してください。");
    }
}

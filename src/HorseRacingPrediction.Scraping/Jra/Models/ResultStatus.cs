namespace HorseRacingPrediction.Scraping.Jra.Models;

/// <summary>
/// レース結果における1頭の確定状態。JRAの着順欄表記に対応する。
/// 「Unknown」は意図的に設けない。未知の表記はパーサーの例外として扱う
/// （依頼書16節）。
/// </summary>
public enum ResultStatus
{
    /// <summary>数字着順（同着を含む）。</summary>
    Finished,

    /// <summary>取消。</summary>
    Cancelled,

    /// <summary>除外。</summary>
    Excluded,

    /// <summary>中止（競走中止）。</summary>
    DidNotFinish,

    /// <summary>失格。</summary>
    Disqualified,
}

/// <summary>
/// JRA着順欄の表記からResultStatusへの対応。
/// </summary>
public static class ResultStatusText
{
    public static bool TryParse(
        string text,
        out ResultStatus status)
    {
        switch (text.Trim())
        {
            case "取消":
                status = ResultStatus.Cancelled;
                return true;
            case "除外":
                status = ResultStatus.Excluded;
                return true;
            case "中止":
                status = ResultStatus.DidNotFinish;
                return true;
            case "失格":
                status = ResultStatus.Disqualified;
                return true;
            default:
                status = default;
                return false;
        }
    }
}

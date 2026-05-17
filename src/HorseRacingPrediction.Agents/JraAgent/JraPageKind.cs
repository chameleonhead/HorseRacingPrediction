namespace HorseRacingPrediction.Agents.JraAgent;

/// <summary>
/// JRA サイト上のページ種別。
/// URL パターンまたはページタイトル・本文から判定する。
/// </summary>
public enum JraPageKind
{
    /// <summary>判定不能 / 未分類</summary>
    Unknown,

    /// <summary>競馬メニュートップ（/keiba/）</summary>
    KeibaMenu,

    /// <summary>開催日程カレンダーページ（/keiba/calendar/）</summary>
    ScheduleCalendar,

    /// <summary>今週の注目レース一覧（/keiba/thisweek/）</summary>
    ThisWeekFeature,

    /// <summary>G1 特設トップ（/keiba/g1/{slug}.html）</summary>
    GradeOneSpecial,

    /// <summary>出馬表ページ（accessD.html / /syutsuba など）</summary>
    RaceCard,

    /// <summary>オッズページ（accessO.html）</summary>
    Odds,

    /// <summary>払戻金・レース結果ページ（accessP.html / accessS.html）</summary>
    Result,

    /// <summary>競走馬情報ページ（accessU.html）</summary>
    HorseProfile,

    /// <summary>騎手情報ページ（accessJ.html）</summary>
    JockeyProfile,

    /// <summary>調教師情報ページ（accessT.html）</summary>
    TrainerProfile,

    /// <summary>開催内レース一覧（出馬表選択直後のページ）</summary>
    RaceList,

    /// <summary>開催一覧（出馬表ボタン押下後のページ）</summary>
    HoldingList,
}

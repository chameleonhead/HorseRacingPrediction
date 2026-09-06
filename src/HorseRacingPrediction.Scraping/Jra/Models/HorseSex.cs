namespace HorseRacingPrediction.Scraping.Jra.Models;

/// <summary>
/// 出走馬の性別（依頼書15節）。「Unknown」は設けない。
/// </summary>
public enum HorseSex
{
    Colt,   // 牡
    Filly,  // 牝
    Gelding // せん
}

public static class HorseSexText
{
    public static bool TryParse(
        string text,
        out HorseSex sex)
    {
        switch (text)
        {
            case "牡":
                sex = HorseSex.Colt;
                return true;
            case "牝":
                sex = HorseSex.Filly;
                return true;
            case "せん":
                sex = HorseSex.Gelding;
                return true;
            default:
                sex = default;
                return false;
        }
    }

    /// <summary>
    /// API連携用のsexCode（JRA表示テキストそのまま）へ変換する。
    /// <see cref="HorseRacingPrediction.ApiClient.IDataCollectionWriteService.UpsertRaceEntryAsync"/>
    /// の <c>sexCode</c> と同じ表現（"牡"/"牝"/"せん"）を用いる。
    /// </summary>
    public static string ToSexCode(HorseSex sex) =>
        sex switch
        {
            HorseSex.Colt => "牡",
            HorseSex.Filly => "牝",
            HorseSex.Gelding => "せん",
            _ => throw new ArgumentOutOfRangeException(nameof(sex), sex, null),
        };
}

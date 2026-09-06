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
}

namespace HorseRacingPrediction.Api.CollectionController;

// Kept temporarily as the shared JRA course normalizer used by domain-write validation and
// the existing UI. The legacy reacquisition endpoints themselves have been removed.
public static class RaceReacquisitionEndpointExtensions
{
    internal static string? ResolveCourse(string? value) => value?.ToUpperInvariant() switch
    {
        "SAPPORO" or "札幌" => "札幌",
        "HAKODATE" or "函館" => "函館",
        "FUKUSHIMA" or "福島" => "福島",
        "NIIGATA" or "新潟" => "新潟",
        "TOKYO" or "東京" => "東京",
        "NAKAYAMA" or "中山" => "中山",
        "CHUKYO" or "中京" => "中京",
        "KYOTO" or "京都" => "京都",
        "HANSHIN" or "阪神" => "阪神",
        "KOKURA" or "小倉" => "小倉",
        _ => null
    };
}

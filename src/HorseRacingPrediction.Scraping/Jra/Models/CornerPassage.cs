namespace HorseRacingPrediction.Scraping.Jra.Models;

/// <summary>
/// コーナー通過順位の1コーナー分（依頼書23節）。コーナー数を固定せず、
/// 可変長のリストとして扱う。「必ず1～4コーナーが存在する」といった
/// Validationは行わない。<see cref="OrderRaw"/>は数値正規化せず生文字列
/// （例："3-5-1-2"、同着を示す"(3,4)"のような表記を含み得る）のまま保持する。
/// </summary>
public sealed record CornerPassage(
    int CornerNumber,
    string OrderRaw);

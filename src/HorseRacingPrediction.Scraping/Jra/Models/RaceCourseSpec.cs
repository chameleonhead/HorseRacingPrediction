namespace HorseRacingPrediction.Scraping.Jra.Models;

/// <summary>
/// レース種別（依頼書12節）。単純な芝/ダートのみのenumに押し込まず、
/// 「平地」「障害」を別次元として扱う。
/// </summary>
public enum RaceType
{
    Flat, // 平地
    Jump  // 障害
}

/// <summary>
/// コースの馬場種別。障害では複数種別を順に通過することを許容する（依頼書12節）。
/// </summary>
public enum CourseSurface
{
    Turf, // 芝
    Dirt  // ダート
}

/// <summary>
/// コースの回り方向。
/// </summary>
public enum CourseDirection
{
    Left, // 左
    Right // 右
}

/// <summary>
/// JRAのコース表記（例：「1,600メートル（芝・左）」「3,000メートル（芝→ダート）」）を
/// 分解した構造（依頼書12節）。単純なTurf/Dirt/Jumpのenumへ押し込まない。
/// 障害では<see cref="Surfaces"/>が複数要素（例：芝→ダート）になり得る。
/// <see cref="Direction"/>・<see cref="Layout"/>は表記に含まれない場合nullとなる。
/// <see cref="RawLayout"/>は括弧内の生文字列をデバッグ・将来対応用に保持する。
/// </summary>
public sealed record RaceCourseSpec(
    int DistanceMeters,
    RaceType RaceType,
    IReadOnlyList<CourseSurface> Surfaces,
    CourseDirection? Direction,
    string? Layout,
    string RawLayout);

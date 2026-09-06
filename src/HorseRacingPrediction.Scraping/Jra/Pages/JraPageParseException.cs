namespace HorseRacingPrediction.Scraping.Jra.Pages;

/// <summary>
/// JRAページの解析に失敗したことを表す例外の基底クラス。DOMやページ内容全文は含めない。
/// 依頼書31節が求める原因分類のため、より具体的な状況では派生型
/// （<see cref="JraPageStructureException"/> 等）を送出することが望ましいが、
/// 単純な構造欠落についてはこの基底クラス自体を直接使用してよい。
/// </summary>
public class JraPageParseException
    : Exception
{
    public JraPageParseException(
        JraPageKind pageKind,
        string url,
        string message,
        string? fieldName = null,
        string? rawValue = null)
        : base(
            $"JRAページ解析に失敗しました。 " +
            $"Kind={pageKind}, Url={url}, Reason={message}" +
            (fieldName is null ? string.Empty : $", FieldName={fieldName}") +
            (rawValue is null ? string.Empty : $", RawValue={rawValue}"))
    {
        PageKind = pageKind;
        Url = url;
        FieldName = fieldName;
        RawValue = rawValue;
    }

    public JraPageKind PageKind { get; }

    public string Url { get; }

    /// <summary>問題となったフィールド名（分かる場合）。</summary>
    public string? FieldName { get; }

    /// <summary>問題となった生の値（分かる場合）。HTML全文は含めない。</summary>
    public string? RawValue { get; }
}

/// <summary>
/// 必須のsection/table/columnがページ上に存在しない（依頼書31節）。
/// </summary>
public sealed class JraPageStructureException
    : JraPageParseException
{
    public JraPageStructureException(
        JraPageKind pageKind,
        string url,
        string message,
        string? fieldName = null)
        : base(pageKind, url, message, fieldName)
    {
    }
}

/// <summary>
/// 天候・馬場状態・ResultStatus等、既知値の集合を持つ項目にJRAとして未知の値が
/// 出現した（依頼書31節）。「不明」「東京」等の値をUnknownへ丸めず、この例外にする。
/// </summary>
public sealed class JraUnexpectedValueException
    : JraPageParseException
{
    public JraUnexpectedValueException(
        JraPageKind pageKind,
        string url,
        string fieldName,
        string rawValue,
        string? message = null)
        : base(
            pageKind,
            url,
            message ?? $"{fieldName} に未知の値が出現しました。",
            fieldName,
            rawValue)
    {
    }
}

/// <summary>
/// 既知項目ではあるが、値を期待する型へ変換できない（依頼書31節）。
/// </summary>
public sealed class JraValueParseException
    : JraPageParseException
{
    public JraValueParseException(
        JraPageKind pageKind,
        string url,
        string fieldName,
        string rawValue,
        string? message = null)
        : base(
            pageKind,
            url,
            message ?? $"{fieldName} の値を解析できませんでした。",
            fieldName,
            rawValue)
    {
    }
}

/// <summary>
/// Navigationが要求したRaceIdと、ページ自身から解析したRaceIdが一致しない
/// （依頼書8節・31節）。
/// </summary>
public sealed class JraRaceIdentityMismatchException
    : JraPageParseException
{
    public JraRaceIdentityMismatchException(
        JraPageKind pageKind,
        string url,
        string expectedRaceId,
        string parsedRaceId)
        : base(
            pageKind,
            url,
            $"要求したRaceIdとページから解析したRaceIdが一致しません。Expected={expectedRaceId}, Parsed={parsedRaceId}",
            "RaceId",
            parsedRaceId)
    {
    }
}

/// <summary>
/// 個々の値はParseできたが、結果データ内の他項目と矛盾する（依頼書31節）。
/// 例：ResultStatus=FinishedなのにFinishPositionが存在しない、降着表現を検出した
/// のに元の入線順位を解析できない等。
/// </summary>
public sealed class JraResultConsistencyException
    : JraPageParseException
{
    public JraResultConsistencyException(
        JraPageKind pageKind,
        string url,
        string message,
        string? fieldName = null,
        string? rawValue = null)
        : base(pageKind, url, message, fieldName, rawValue)
    {
    }
}

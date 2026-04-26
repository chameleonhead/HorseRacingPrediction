using System.Text;
using HorseRacingPrediction.Domain.Races;

namespace HorseRacingPrediction.Agents.Plugins;

/// <summary>
/// レース・馬・騎手・調教師の決定論的 ID 生成ロジックを提供するユーティリティクラス。
/// <para>
/// UUID v5 相当のアルゴリズム（SHA-1 ハッシュ + 名前空間）により、
/// 同じ入力から常に同じ ID を生成する。
/// <see cref="EventFlowDataCollectionWriteService"/> と
/// クラウド接続時の HTTP 実装の両方がこのクラスを使用する。
/// </para>
/// </summary>
public static class DeterministicIdGenerator
{
    private static readonly Guid HorseNamespaceId = new("1c86504c-11bb-4e95-b997-94d64f0569f3");
    private static readonly Guid JockeyNamespaceId = new("ec7d5b11-f383-4860-88b7-37ef25e4cc81");
    private static readonly Guid TrainerNamespaceId = new("36d7318f-bf48-488f-b0d8-0e2c942b36d2");
    private static readonly Guid RaceNamespaceId = new("d54c5101-305d-42aa-a8df-3c52ca96a6ef");

    /// <summary>レース日・競馬場コード・レース番号から決定論的な <see cref="RaceId"/> を生成する。</summary>
    public static RaceId BuildRaceId(DateOnly raceDate, string racecourseCode, int raceNumber)
    {
        var normalizedRacecourse = NormalizeKey(racecourseCode);
        var guid = CreateDeterministicGuid(RaceNamespaceId, $"{raceDate:yyyy-MM-dd}|{normalizedRacecourse}|{raceNumber:D2}");
        return new RaceId($"race-{guid:D}");
    }

    /// <summary>レース ID と馬番から出走エントリー ID を生成する。</summary>
    public static string BuildRaceEntryId(string raceId, int horseNumber) =>
        $"{raceId}-entry-{horseNumber:D2}";

    /// <summary>エンティティ種別と正規化名から決定論的なエンティティ ID を生成する。</summary>
    public static string BuildEntityId(string prefix, string normalizedName)
    {
        var namespaceId = prefix switch
        {
            "horse" => HorseNamespaceId,
            "jockey" => JockeyNamespaceId,
            "trainer" => TrainerNamespaceId,
            _ => throw new InvalidOperationException($"未知の ID prefix です: {prefix}")
        };

        var guid = CreateDeterministicGuid(namespaceId, normalizedName);
        return $"{prefix}-{guid:D}";
    }

    /// <summary>表示名を正規化する（前後の空白を除去）。</summary>
    public static string NormalizeDisplayName(string value) => value.Trim();

    /// <summary>競馬場コードなどのキー文字列を正規化する（小文字 + 英数字のみ）。</summary>
    public static string NormalizeKey(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
                builder.Append(character);
        }

        return builder.Length == 0 ? "unknown" : builder.ToString();
    }

    // ------------------------------------------------------------------ //
    // private helpers
    // ------------------------------------------------------------------ //

    private static Guid CreateDeterministicGuid(Guid namespaceId, string name)
    {
        var namespaceBytes = namespaceId.ToByteArray();
        SwapByteOrder(namespaceBytes);

        var nameBytes = Encoding.UTF8.GetBytes(name);
        var data = new byte[namespaceBytes.Length + nameBytes.Length];
        Buffer.BlockCopy(namespaceBytes, 0, data, 0, namespaceBytes.Length);
        Buffer.BlockCopy(nameBytes, 0, data, namespaceBytes.Length, nameBytes.Length);

        var hash = System.Security.Cryptography.SHA1.HashData(data);
        hash[6] = (byte)((hash[6] & 0x0F) | 0x50);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);

        var guidBytes = hash[..16].ToArray();
        SwapByteOrder(guidBytes);
        return new Guid(guidBytes);
    }

    private static void SwapByteOrder(byte[] guid)
    {
        (guid[0], guid[3]) = (guid[3], guid[0]);
        (guid[1], guid[2]) = (guid[2], guid[1]);
        (guid[4], guid[5]) = (guid[5], guid[4]);
        (guid[6], guid[7]) = (guid[7], guid[6]);
    }
}

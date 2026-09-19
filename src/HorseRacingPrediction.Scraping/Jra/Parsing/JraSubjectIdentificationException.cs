namespace HorseRacingPrediction.Scraping.Jra.Parsing;

public enum JraSubjectIdentificationFailureKind
{
    NoCandidate,
    MultipleCandidates,
    ProfileNameMismatch,
    BirthDateMismatch,
    HistoricalRaceEvidenceBudgetExceeded,
    HistoricalRaceEvidenceUnavailable,
    SourceIdentityMismatch,
}

public sealed record JraSubjectIdentificationCandidate(string Name, string Url, string? Evidence = null);

public sealed class JraSubjectIdentificationException : Exception
{
    private const int MaximumRecordedCandidates = 5;

    public JraSubjectIdentificationException(
        JraSubjectIdentificationFailureKind kind,
        string expectedSubjectType,
        string expectedName,
        string? actualName = null,
        IEnumerable<JraSubjectIdentificationCandidate>? candidates = null,
        string? requestedUrl = null,
        string? finalUrl = null)
        : base(BuildMessage(kind, expectedSubjectType, expectedName, actualName, candidates))
    {
        Kind = kind;
        ExpectedSubjectType = expectedSubjectType;
        ExpectedName = expectedName;
        ActualName = actualName;
        Candidates = (candidates ?? []).Take(MaximumRecordedCandidates).ToArray();
        RequestedUrl = requestedUrl;
        FinalUrl = finalUrl;
    }

    public JraSubjectIdentificationFailureKind Kind { get; }
    public string ExpectedSubjectType { get; }
    public string ExpectedName { get; }
    public string? ActualName { get; }
    public IReadOnlyList<JraSubjectIdentificationCandidate> Candidates { get; }
    public string? RequestedUrl { get; }
    public string? FinalUrl { get; }

    public JraSubjectIdentificationException WithNavigation(
        IEnumerable<JraSubjectIdentificationCandidate>? candidates = null,
        string? requestedUrl = null,
        string? finalUrl = null) => new(
            Kind,
            ExpectedSubjectType,
            ExpectedName,
            ActualName,
            candidates ?? Candidates,
            requestedUrl ?? RequestedUrl,
            finalUrl ?? FinalUrl);

    private static string BuildMessage(
        JraSubjectIdentificationFailureKind kind,
        string expectedSubjectType,
        string expectedName,
        string? actualName,
        IEnumerable<JraSubjectIdentificationCandidate>? candidates)
    {
        var reason = kind switch
        {
            JraSubjectIdentificationFailureKind.NoCandidate => "公開検索に一致候補がありません",
            JraSubjectIdentificationFailureKind.MultipleCandidates => "公開検索に一致候補が複数あります",
            JraSubjectIdentificationFailureKind.ProfileNameMismatch => "取得プロフィールの名前が一致しません",
            JraSubjectIdentificationFailureKind.BirthDateMismatch => "取得プロフィールの生年月日が一致しません",
            JraSubjectIdentificationFailureKind.HistoricalRaceEvidenceBudgetExceeded => "過去レース根拠の探索上限を超えました",
            JraSubjectIdentificationFailureKind.HistoricalRaceEvidenceUnavailable => "過去レースとの一致を公開履歴から確認できません",
            JraSubjectIdentificationFailureKind.SourceIdentityMismatch => "取得プロフィールの公開識別子が一致しません",
            _ => "公開プロフィールを同定できません",
        };
        var parts = new List<string>
        {
            $"同定不能: {reason}。期待={expectedSubjectType}:{expectedName}",
        };
        if (!string.IsNullOrWhiteSpace(actualName)) parts.Add($"取得名={actualName}");
        var summaries = (candidates ?? []).Take(MaximumRecordedCandidates)
            .Select(candidate => $"{candidate.Name} [{candidate.Url}]"
                + (string.IsNullOrWhiteSpace(candidate.Evidence) ? string.Empty : $" ({candidate.Evidence})"))
            .ToArray();
        if (summaries.Length > 0) parts.Add($"候補={string.Join(", ", summaries)}");
        return string.Join("; ", parts);
    }
}

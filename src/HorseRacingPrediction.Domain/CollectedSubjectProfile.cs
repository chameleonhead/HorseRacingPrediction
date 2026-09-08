namespace HorseRacingPrediction.Domain;

/// <summary>公開プロフィール。項目名と値は取得元の表記を保持する。</summary>
public sealed record CollectedSubjectProfile(string Name, string SourceIdentity, string SourceUrl,
    Dictionary<string, string> Fields, DateTimeOffset AcquiredAt);

using HorseRacingPrediction.Contracts.Collection;

namespace HorseRacingPrediction.Contracts.Repairs;

public sealed record GetSubjectNameNormalizationRequest(
    CollectionResourceType SubjectType, string? Query, int? Page = null, int? PageSize = null);


using HorseRacingPrediction.Contracts.Collection;

namespace HorseRacingPrediction.Contracts.Repairs;

public sealed record SubjectNameNormalizationItemResultDto(
    CollectionResourceType SubjectType,
    string SubjectId,
    string Status,
    string Message);

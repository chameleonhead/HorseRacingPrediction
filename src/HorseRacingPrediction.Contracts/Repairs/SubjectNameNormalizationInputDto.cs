
using HorseRacingPrediction.Contracts.Collection;

namespace HorseRacingPrediction.Contracts.Repairs;

public sealed record SubjectNameNormalizationInputDto(
    CollectionResourceType SubjectType,
    string SubjectId,
    string ManifestToken);

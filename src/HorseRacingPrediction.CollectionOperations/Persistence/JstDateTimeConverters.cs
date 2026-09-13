using HorseRacingPrediction.Contracts.Time;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HorseRacingPrediction.CollectionOperations.Persistence;

internal sealed class JstDateTimeOffsetConverter()
    : ValueConverter<DateTimeOffset, DateTime>(
        value => JstTime.ToDatabase(value),
        value => JstTime.FromDatabase(value));

internal sealed class NullableJstDateTimeOffsetConverter()
    : ValueConverter<DateTimeOffset?, DateTime?>(
        value => value.HasValue ? JstTime.ToDatabase(value.Value) : null,
        value => value.HasValue ? JstTime.FromDatabase(value.Value) : null);

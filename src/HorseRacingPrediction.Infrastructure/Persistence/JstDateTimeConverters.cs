using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using HorseRacingPrediction.Contracts.Time;

namespace HorseRacingPrediction.Infrastructure.Persistence;

internal sealed class JstDateTimeOffsetConverter()
    : ValueConverter<DateTimeOffset, DateTime>(
        value => JstTime.ToDatabase(value),
        value => JstTime.FromDatabase(value));

internal sealed class NullableJstDateTimeOffsetConverter()
    : ValueConverter<DateTimeOffset?, DateTime?>(
        value => value.HasValue ? JstTime.ToDatabase(value.Value) : null,
        value => value.HasValue ? JstTime.FromDatabase(value.Value) : null);

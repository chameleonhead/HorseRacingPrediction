using HorseRacingPrediction.Contracts;

namespace HorseRacingPrediction.Api.Endpoints.Shared;

internal static class EndpointQueryUtilities
{
    internal static bool ContainsIgnoreCase(string? value, string searchTerm)
        => !string.IsNullOrWhiteSpace(value)
            && value.Contains(searchTerm, StringComparison.OrdinalIgnoreCase);

    internal static string? ValidatePaging(int page, int pageSize)
    {
        if (page < 1)
            return "Page must be greater than or equal to 1.";

        if (pageSize is < 1 or > 100)
            return "PageSize must be between 1 and 100.";

        return null;
    }

    internal static PagedResponse<TResponse> ToPagedResponse<TReadModel, TResponse>(
        IEnumerable<TReadModel> source,
        int page,
        int pageSize,
        Func<TReadModel, TResponse> selector)
    {
        var totalCount = source.Count();
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);
        var items = source
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(selector)
            .ToList();

        return new PagedResponse<TResponse>(items, page, pageSize, totalCount, totalPages);
    }

    internal static IReadOnlyList<ParticipationHistoryEntryDto> EntriesInLastThreeYears(
        IReadOnlyList<ParticipationHistoryEntryDto> entries)
    {
        var latestDate = entries.Where(x => x.RaceDate.HasValue).Select(x => x.RaceDate!.Value).DefaultIfEmpty().Max();
        if (latestDate == default) return entries;
        var from = latestDate.AddYears(-3);
        return entries.Where(x => !x.RaceDate.HasValue || x.RaceDate.Value >= from).ToList();
    }
}

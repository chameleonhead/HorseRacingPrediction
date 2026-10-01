namespace HorseRacingPrediction.Contracts.Collection;

public sealed record ListFailureNotificationsRequest(string? View = null, bool? Published = null,
    string? DeliveryState = null, int? Limit = null);

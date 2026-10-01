
using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Predictions;

public sealed record WithdrawPredictionTicketRequest(
    [property: JsonIgnore] string PredictionTicketId,
    WithdrawPredictionTicketInputDto? Withdrawal);

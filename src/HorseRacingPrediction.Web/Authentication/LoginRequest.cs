namespace HorseRacingPrediction.Web.Authentication;

public sealed record LoginRequest(
    string Username,
    string Password,
    bool RememberMe = false);
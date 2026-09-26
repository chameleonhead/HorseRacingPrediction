namespace HorseRacingPrediction.Contracts;

/// <summary>A structured, isolated operational fence, not a parse or domain-validation failure.</summary>
public sealed class CollectionRepairHeldException() : Exception("Collection is fenced by race assignment repair.");

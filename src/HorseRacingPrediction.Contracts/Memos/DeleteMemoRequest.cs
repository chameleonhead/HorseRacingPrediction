using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Memos;

public sealed record DeleteMemoRequest([property: JsonIgnore] string MemoId);

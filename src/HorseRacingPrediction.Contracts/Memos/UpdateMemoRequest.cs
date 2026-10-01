
using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Memos;

public sealed record UpdateMemoRequest(
    [property: JsonIgnore] string MemoId,
    UpdateMemoInputDto? Memo);

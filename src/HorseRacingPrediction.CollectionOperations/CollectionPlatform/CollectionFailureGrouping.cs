using System.Security.Cryptography;
using System.Text;

namespace HorseRacingPrediction.CollectionOperations.CollectionPlatform;

public static class CollectionFailureGrouping
{
    public static string CreateKey(string definition, CollectionTaskStatus status, string? errorCode)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{definition}\n{status}\n{errorCode ?? string.Empty}"));
        return Convert.ToHexString(bytes, 0, 8);
    }

    public static IReadOnlyList<CollectionFailureGroup> Build(
        IReadOnlyList<PendingCollectionFailureNotification> notifications) => notifications.GroupBy(x => new
        {
            Definition = x.Definition.Value,
            x.Status,
            ErrorCode = x.ErrorCode ?? string.Empty,
        }).Select(x => new CollectionFailureGroup(
            CreateKey(x.Key.Definition, x.Key.Status, x.Key.ErrorCode), new(x.Key.Definition), x.Key.Status,
            string.IsNullOrEmpty(x.Key.ErrorCode) ? null : x.Key.ErrorCode,
            x.OrderByDescending(y => y.FailedAt).Select(y => y.ErrorMessage).FirstOrDefault(), x.Count(),
            x.Min(y => y.FailedAt), x.Max(y => y.FailedAt), x.Select(y => y.NotificationId).ToList(),
            x.Select(y => y.Resource).Distinct().Take(20).ToList())).OrderByDescending(x => x.LastFailedAt).ToList();
}

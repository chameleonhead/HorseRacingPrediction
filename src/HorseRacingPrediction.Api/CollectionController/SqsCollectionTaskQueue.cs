using Amazon.SQS;
using Amazon.SQS.Model;
using HorseRacingPrediction.Collector.Scheduling;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace HorseRacingPrediction.Api.CollectionController;

public sealed class SqsCollectionTaskQueue : ICollectionTaskQueue
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IAmazonSQS _sqs;
    private readonly CollectionQueueOptions _options;

    public SqsCollectionTaskQueue(IAmazonSQS sqs, IOptions<CollectionQueueOptions> options)
    {
        _sqs = sqs;
        _options = options.Value;
    }

    public async Task SendAsync(CollectionTaskNotification notification, CancellationToken cancellationToken)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.QueueUrl))
            throw new InvalidOperationException("CollectionQueue is not configured.");

        await _sqs.SendMessageAsync(new SendMessageRequest
        {
            QueueUrl = _options.QueueUrl,
            MessageBody = JsonSerializer.Serialize(notification, JsonOptions)
        }, cancellationToken).ConfigureAwait(false);
    }
}

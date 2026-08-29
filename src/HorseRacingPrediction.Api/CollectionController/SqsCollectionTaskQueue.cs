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
        if (!_options.Enabled || (string.IsNullOrWhiteSpace(_options.QueueUrl) && string.IsNullOrWhiteSpace(_options.QueueName)))
            throw new InvalidOperationException("CollectionQueue is not configured.");

        var queueUrl = _options.QueueUrl;
        if (string.IsNullOrWhiteSpace(queueUrl))
            queueUrl = (await _sqs.GetQueueUrlAsync(_options.QueueName, cancellationToken).ConfigureAwait(false)).QueueUrl;

        await _sqs.SendMessageAsync(new SendMessageRequest
        {
            QueueUrl = queueUrl,
            MessageBody = JsonSerializer.Serialize(notification, JsonOptions)
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task PurgeAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled) return;
        var queueUrl = await ResolveQueueUrlAsync(_options.QueueUrl, _options.QueueName, cancellationToken).ConfigureAwait(false);
        var deadLetterQueueUrl = await ResolveQueueUrlAsync(
            _options.DeadLetterQueueUrl, _options.DeadLetterQueueName, cancellationToken).ConfigureAwait(false);
        await _sqs.PurgeQueueAsync(queueUrl, cancellationToken).ConfigureAwait(false);
        await _sqs.PurgeQueueAsync(deadLetterQueueUrl, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> ResolveQueueUrlAsync(string configuredUrl, string queueName, CancellationToken cancellationToken)
        => string.IsNullOrWhiteSpace(configuredUrl)
            ? (await _sqs.GetQueueUrlAsync(queueName, cancellationToken).ConfigureAwait(false)).QueueUrl
            : configuredUrl;
}

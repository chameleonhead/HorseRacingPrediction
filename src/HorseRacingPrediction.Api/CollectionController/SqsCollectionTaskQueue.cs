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

    public async Task<long> GetDeadLetterQueueDepthAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled) return 0;

        var deadLetterQueueUrl = await ResolveQueueUrlAsync(
            _options.DeadLetterQueueUrl, _options.DeadLetterQueueName, cancellationToken).ConfigureAwait(false);
        var attributes = await _sqs.GetQueueAttributesAsync(new GetQueueAttributesRequest
        {
            QueueUrl = deadLetterQueueUrl,
            AttributeNames = [QueueAttributeName.ApproximateNumberOfMessages]
        }, cancellationToken).ConfigureAwait(false);

        return attributes.ApproximateNumberOfMessages;
    }

    public async Task<CollectionQueueDepth> GetQueueDepthAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled) return new CollectionQueueDepth(0, 0);

        var queueUrl = await ResolveQueueUrlAsync(_options.QueueUrl, _options.QueueName, cancellationToken).ConfigureAwait(false);
        var attributes = await _sqs.GetQueueAttributesAsync(new GetQueueAttributesRequest
        {
            QueueUrl = queueUrl,
            AttributeNames =
            [
                QueueAttributeName.ApproximateNumberOfMessages,
                QueueAttributeName.ApproximateNumberOfMessagesNotVisible
            ]
        }, cancellationToken).ConfigureAwait(false);

        return new CollectionQueueDepth(attributes.ApproximateNumberOfMessages, attributes.ApproximateNumberOfMessagesNotVisible);
    }

    public async Task<IReadOnlyList<DeadLetterQueueMessage>> ReceiveDeadLetterMessagesAsync(int maxMessages, CancellationToken cancellationToken)
    {
        if (!_options.Enabled) return [];

        var deadLetterQueueUrl = await ResolveQueueUrlAsync(
            _options.DeadLetterQueueUrl, _options.DeadLetterQueueName, cancellationToken).ConfigureAwait(false);
        var response = await _sqs.ReceiveMessageAsync(new ReceiveMessageRequest
        {
            QueueUrl = deadLetterQueueUrl,
            MaxNumberOfMessages = Math.Clamp(maxMessages, 1, 10),
            WaitTimeSeconds = 0
        }, cancellationToken).ConfigureAwait(false);

        return (response.Messages ?? [])
            .Select(x => new DeadLetterQueueMessage(x.ReceiptHandle, x.Body))
            .ToList();
    }

    public async Task DeleteDeadLetterMessageAsync(string receiptHandle, CancellationToken cancellationToken)
    {
        if (!_options.Enabled) return;

        var deadLetterQueueUrl = await ResolveQueueUrlAsync(
            _options.DeadLetterQueueUrl, _options.DeadLetterQueueName, cancellationToken).ConfigureAwait(false);
        await _sqs.DeleteMessageAsync(deadLetterQueueUrl, receiptHandle, cancellationToken).ConfigureAwait(false);
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

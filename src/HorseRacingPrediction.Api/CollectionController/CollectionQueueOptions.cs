namespace HorseRacingPrediction.Api.CollectionController;

public sealed class CollectionQueueOptions
{
    public const string SectionName = "CollectionQueue";
    public bool Enabled { get; set; }
    public string ServiceUrl { get; set; } = string.Empty;
    public string QueueUrl { get; set; } = string.Empty;
    public string QueueName { get; set; } = "horse-racing-prediction-collector";
    public string DeadLetterQueueUrl { get; set; } = string.Empty;
    public string DeadLetterQueueName { get; set; } = "horse-racing-prediction-collector-dlq";
    public int DispatchIntervalSeconds { get; set; } = 5;
    public int DispatchBatchSize { get; set; } = 10;
}

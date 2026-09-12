using System.Text.RegularExpressions;

namespace HorseRacingPrediction.Collector.Tests.CollectionPlatform;

[TestClass]
public sealed class CollectionQueueCutoverContractTests
{
    private static readonly string Root = FindRepositoryRoot();
    private static string Main => File.ReadAllText(Path.Combine(Root, "infra", "collector-lambda", "main.tf"));
    private static string Outputs => File.ReadAllText(Path.Combine(Root, "infra", "collector-lambda", "outputs.tf"));
    private static string DeployWorkflow => File.ReadAllText(Path.Combine(Root, ".github", "workflows", "app-deploy.yml"));

    [TestMethod]
    public void Terraform_DefinesOnlyResourceCollectionQueuePair()
    {
        AssertQueueResource("resource_collection", "horse-racing-prediction-resource-collection");
        AssertQueueResource("resource_collection_dlq", "horse-racing-prediction-resource-collection-dlq");
        StringAssert.Contains(ResourceBlock(Main, "resource_collection"),
            "deadLetterTargetArn = aws_sqs_queue.resource_collection_dlq.arn");
        var queueResources = Regex.Matches(Main, "resource \\\"aws_sqs_queue\\\" \\\"(?<name>[^\\\"]+)\\\"")
            .Select(x => x.Groups["name"].Value).Order().ToArray();
        CollectionAssert.AreEqual(new[] { "resource_collection", "resource_collection_dlq" }, queueResources);
        Assert.IsFalse(Main.Contains("horse-racing-prediction-collector-dlq", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Terraform_UsesResourceCollectionQueueWithoutCutoverFlags()
    {
        StringAssert.Contains(Outputs, "value = aws_sqs_queue.resource_collection.url");
        StringAssert.Contains(Outputs, "value = aws_sqs_queue.resource_collection.arn");
        Assert.IsFalse(Main.Contains("active_queue_", StringComparison.Ordinal));
        Assert.IsFalse(Main.Contains("retain_legacy_collection_queues", StringComparison.Ordinal));
    }

    [TestMethod]
    public void EcrBootstrap_IncludesResourcesWithPendingStateAddressMoves()
    {
        StringAssert.Contains(DeployWorkflow, "-target=aws_ecr_repository.collector");
        StringAssert.Contains(DeployWorkflow, "-target=aws_ecr_lifecycle_policy.collector");
        StringAssert.Contains(DeployWorkflow, "-target=aws_sqs_queue.resource_collection");
        StringAssert.Contains(DeployWorkflow, "-target=aws_sqs_queue.resource_collection_dlq");
    }

    [TestMethod]
    public void Terraform_ConnectsLambdaDirectlyToResourceCollectionQueue()
    {
        var mapping = ResourceBlock(Main, "collector_queue", "aws_lambda_event_source_mapping");
        StringAssert.Contains(mapping, "event_source_arn                   = aws_sqs_queue.resource_collection.arn");
        StringAssert.Contains(mapping, "function_name                      = aws_lambda_function.collector[0].arn");
    }

    [TestMethod]
    public void Terraform_ProtectsLambdaTransportFailureBoundaries()
    {
        var queue = ResourceBlock(Main, "resource_collection");
        var dlq = ResourceBlock(Main, "resource_collection_dlq");
        var lambda = ResourceBlock(Main, "collector", "aws_lambda_function");
        var mapping = ResourceBlock(Main, "collector_queue", "aws_lambda_event_source_mapping");

        StringAssert.Contains(queue, "visibility_timeout_seconds = 5400");
        StringAssert.Contains(queue, "message_retention_seconds  = 345600");
        StringAssert.Contains(queue, "maxReceiveCount = 3");
        StringAssert.Contains(dlq, "message_retention_seconds = 1209600");
        StringAssert.Contains(lambda, "timeout                        = 900");
        StringAssert.Contains(lambda, "reserved_concurrent_executions = 1");
        StringAssert.Contains(mapping, "batch_size                         = 1");
        StringAssert.Contains(mapping, "function_response_types            = [\"ReportBatchItemFailures\"]");
        StringAssert.Contains(Main, "resource \"aws_cloudwatch_metric_alarm\" \"collector_lambda_throttles\"");
        Assert.IsFalse(Main.Contains("aws_lambda_function_event_invoke_config", StringComparison.Ordinal),
            "SQS event source mappings must use the queue redrive policy, not Lambda async invoke settings.");
    }

    [TestMethod]
    public void Terraform_IamPoliciesDoNotReferenceLegacyQueues()
    {
        StringAssert.Contains(Main, "Resource = [aws_sqs_queue.resource_collection.arn]");
        StringAssert.Contains(Main, "Resource = [aws_sqs_queue.resource_collection_dlq.arn]");
        Assert.IsFalse(Main.Contains("aws_sqs_queue.collector", StringComparison.Ordinal));
        Assert.IsFalse(Outputs.Contains("legacy_collector_queue_url", StringComparison.Ordinal));
    }

    private static void AssertQueueResource(string resourceName, string physicalName)
        => StringAssert.Matches(ResourceBlock(Main, resourceName),
            new Regex($"name\\s*=\\s*\"{Regex.Escape(physicalName)}\""),
            $"Queue resource {resourceName} must retain its exact physical name.");

    private static string ResourceBlock(string text, string name, string type = "aws_sqs_queue")
        => NamedBlock(text, "resource", type, name);

    private static string NamedBlock(string text, string keyword, params string[] names)
    {
        var header = keyword + " " + string.Join(" ", names.Select(x => $"\"{x}\""));
        var start = text.IndexOf(header, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, start, $"Missing Terraform block: {header}");
        var brace = text.IndexOf('{', start);
        var depth = 0;
        for (var index = brace; index < text.Length; index++)
        {
            if (text[index] == '{') depth++;
            if (text[index] == '}' && --depth == 0) return text[start..(index + 1)];
        }
        Assert.Fail($"Unterminated Terraform block: {header}");
        return string.Empty;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "HorseRacingPrediction.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}

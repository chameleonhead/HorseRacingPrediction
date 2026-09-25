using System.Text.RegularExpressions;

namespace HorseRacingPrediction.Collector.Tests.CollectionPlatform;

[TestClass]
public sealed class CollectionQueueCutoverContractTests
{
    private static readonly string Root = FindRepositoryRoot();
    private static string Main => File.ReadAllText(Path.Combine(Root, "infra", "collector-lambda", "main.tf"));
    private static string Outputs => File.ReadAllText(Path.Combine(Root, "infra", "collector-lambda", "outputs.tf"));
    private static string DeployWorkflow => File.ReadAllText(Path.Combine(Root, ".github", "workflows", "app-deploy.yml"));
    private static string LocalMonitoringRunner => File.ReadAllText(Path.Combine(Root, "tools", "collection_monitoring", "invoke_local_monitor.ps1"));
    private static string Compose => File.ReadAllText(Path.Combine(Root, "deploy", "docker-compose.yml"));
    private static string ApiSettings => File.ReadAllText(Path.Combine(Root, "src", "HorseRacingPrediction.Api", "appsettings.json"));

    [TestMethod]
    public void MonitoringWorkflow_IsRemovedAfterLocalCodexTaskCutover()
    {
        Assert.IsFalse(File.Exists(Path.Combine(Root, ".github", "workflows", "collection-monitoring.yml")));
        Assert.IsTrue(File.Exists(Path.Combine(Root, "tools", "collection_monitoring", "invoke_local_monitor.ps1")));
    }

    [TestMethod]
    public void LocalMonitoringRunner_UsesDpapiFilesAndRequiresCauseRouting()
    {
        StringAssert.Contains(LocalMonitoringRunner, "Export-Clixml");
        StringAssert.Contains(LocalMonitoringRunner, "Import-Clixml");
        StringAssert.Contains(LocalMonitoringRunner, "rootCauseHypothesis");
        StringAssert.Contains(LocalMonitoringRunner, "ownerTask");
        StringAssert.Contains(LocalMonitoringRunner, "nextSafeOperation");
        Assert.IsFalse(LocalMonitoringRunner.Contains("gh ", StringComparison.Ordinal));
    }

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
        StringAssert.Contains(ApiSettings,
            "\"DeadLetterQueueName\": \"horse-racing-prediction-resource-collection-dlq\"");
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
    public void DeployWorkflow_MigratesLegacyRaceJobsAfterHealthCheck()
    {
        var migration = DeployWorkflow.IndexOf("- name: Migrate legacy race collection jobs", StringComparison.Ordinal);
        var healthCheck = DeployWorkflow.LastIndexOf("- name: Verify deployment health", migration,
            StringComparison.Ordinal);

        Assert.IsGreaterThanOrEqualTo(0, healthCheck);
        Assert.IsGreaterThan(healthCheck, migration);
        StringAssert.Contains(DeployWorkflow, "$base/pipeline/pause");
        StringAssert.Contains(DeployWorkflow, "$base/migrations/race-detail/apply");
        StringAssert.Contains(DeployWorkflow, "$base/migrations/race-detail/preview");
        StringAssert.Contains(DeployWorkflow, "$base/pipeline/resume");
        StringAssert.Contains(DeployWorkflow, "jq '.sourceResources'");
        StringAssert.Contains(DeployWorkflow, "jq '.errors | length'");
    }

    [TestMethod]
    public void DeployWorkflow_PreviewsOwnerMigrationWithoutPausingOrApplying()
    {
        var migration = DeployWorkflow.IndexOf("migrate-race-entry-owners:", StringComparison.Ordinal);
        var preview = DeployWorkflow.IndexOf("race-entry-owners/preview", migration, StringComparison.Ordinal);
        var section = DeployWorkflow[migration..];

        Assert.IsGreaterThanOrEqualTo(0, migration);
        Assert.IsGreaterThan(migration, preview);
        StringAssert.Contains(section, "preview-only");
        Assert.IsFalse(section.Contains("$base/pipeline/pause", StringComparison.Ordinal));
        Assert.IsFalse(section.Contains("race-entry-owners/apply", StringComparison.Ordinal));
    }

    [TestMethod]
    public void CollectionOperationsWorkflows_AreRemovedAfterCodexTaskCutover()
    {
        Assert.IsFalse(File.Exists(Path.Combine(Root, ".github", "workflows", "collection-maintenance.yml")));
        Assert.IsFalse(File.Exists(Path.Combine(Root, ".github", "workflows", "collection-dlq-diagnostics.yml")));
        Assert.IsFalse(Compose.Contains("CollectionQueue__MaxInFlightEnvelopes", StringComparison.Ordinal));
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
        StringAssert.Contains(queue, "maxReceiveCount = 1");
        StringAssert.Contains(dlq, "message_retention_seconds = 1209600");
        StringAssert.Contains(lambda, "timeout                        = 900");
        StringAssert.Contains(lambda, "ephemeral_storage { size = 10240 }");
        Assert.IsFalse(lambda.Contains("ephemeral_storage { size = 4096 }", StringComparison.Ordinal));
        StringAssert.Contains(lambda, "reserved_concurrent_executions = 1");
        StringAssert.Contains(mapping, "batch_size                         = 1");
        StringAssert.Contains(mapping, "function_response_types            = [\"ReportBatchItemFailures\"]");
        StringAssert.Contains(Main, "resource \"aws_cloudwatch_metric_alarm\" \"collector_lambda_throttles\"");
        Assert.IsFalse(Main.Contains("aws_lambda_function_event_invoke_config", StringComparison.Ordinal),
            "SQS event source mappings must use the queue redrive policy, not Lambda async invoke settings.");
        StringAssert.Contains(ApiSettings, "\"MaxInFlightEnvelopes\": 1");
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

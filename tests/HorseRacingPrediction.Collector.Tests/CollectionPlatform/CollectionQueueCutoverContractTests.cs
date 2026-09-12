using System.Text.RegularExpressions;

namespace HorseRacingPrediction.Collector.Tests.CollectionPlatform;

[TestClass]
public sealed class CollectionQueueCutoverContractTests
{
    private static readonly string Root = FindRepositoryRoot();
    private static string Main => File.ReadAllText(Path.Combine(Root, "infra", "collector-lambda", "main.tf"));
    private static string Variables => File.ReadAllText(Path.Combine(Root, "infra", "collector-lambda", "variables.tf"));
    private static string Outputs => File.ReadAllText(Path.Combine(Root, "infra", "collector-lambda", "outputs.tf"));

    [TestMethod]
    public void Terraform_DefinesDistinctLegacyAndReplacementQueuePairs()
    {
        AssertQueueResource("collector", "horse-racing-prediction-collector");
        AssertQueueResource("collector_dlq", "horse-racing-prediction-collector-dlq");
        AssertQueueResource("resource_collection", "horse-racing-prediction-resource-collection");
        AssertQueueResource("resource_collection_dlq", "horse-racing-prediction-resource-collection-dlq");
        StringAssert.Contains(ResourceBlock(Main, "resource_collection"),
            "deadLetterTargetArn = aws_sqs_queue.resource_collection_dlq.arn");
        StringAssert.Contains(ResourceBlock(Main, "collector"),
            "deadLetterTargetArn = aws_sqs_queue.collector_dlq[0].arn");
        var queueResources = Regex.Matches(Main, "resource \\\"aws_sqs_queue\\\" \\\"(?<name>[^\\\"]+)\\\"")
            .Select(x => x.Groups["name"].Value).Order().ToArray();
        CollectionAssert.AreEqual(new[]
        {
            "collector", "collector_dlq", "resource_collection", "resource_collection_dlq"
        }, queueResources);
    }

    [TestMethod]
    public void Terraform_DefaultsToLegacyActiveAndRetained()
    {
        StringAssert.Matches(Variables, new Regex(
            "variable \\\"activate_resource_collection_queue\\\"[\\s\\S]*?default\\s*=\\s*false"));
        StringAssert.Matches(Variables, new Regex(
            "variable \\\"retain_legacy_collection_queues\\\"[\\s\\S]*?default\\s*=\\s*true"));
        StringAssert.Matches(Main, new Regex(
            "active_queue_arn\\s*=\\s*var\\.activate_resource_collection_queue \\? aws_sqs_queue\\.resource_collection\\.arn : aws_sqs_queue\\.collector\\[0\\]\\.arn"));
        StringAssert.Matches(Main, new Regex(
            "active_queue_url\\s*=\\s*var\\.activate_resource_collection_queue \\? aws_sqs_queue\\.resource_collection\\.url : aws_sqs_queue\\.collector\\[0\\]\\.url"));
        StringAssert.Contains(Outputs, "value = local.active_queue_url");
        StringAssert.Contains(Outputs, "value = local.active_queue_arn");
    }

    [TestMethod]
    public void Terraform_GuardsLegacyDeletionAndSwitchesLambdaThroughActiveQueue()
    {
        var check = NamedBlock(Main, "check", "legacy_queues_removed_only_after_activation");
        StringAssert.Contains(check,
            "condition     = var.retain_legacy_collection_queues || var.activate_resource_collection_queue");
        var mapping = ResourceBlock(Main, "collector_queue", "aws_lambda_event_source_mapping");
        StringAssert.Contains(mapping, "event_source_arn                   = local.active_queue_arn");
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
    public void Terraform_LegacyRetentionCanDeleteOnlyTheOldQueuePair()
    {
        var guardedResources = new[] { "collector", "collector_dlq", "resource_collection", "resource_collection_dlq" }
            .Where(name => ResourceBlock(Main, name).Contains(
                "count                     = var.retain_legacy_collection_queues ? 1 : 0", StringComparison.Ordinal)
                || ResourceBlock(Main, name).Contains(
                    "count                      = var.retain_legacy_collection_queues ? 1 : 0", StringComparison.Ordinal))
            .Order().ToArray();
        CollectionAssert.AreEqual(new[] { "collector", "collector_dlq" }, guardedResources);
        Assert.IsFalse(ResourceBlock(Main, "resource_collection").Contains("retain_legacy_collection_queues",
            StringComparison.Ordinal));
        Assert.IsFalse(ResourceBlock(Main, "resource_collection_dlq").Contains("retain_legacy_collection_queues",
            StringComparison.Ordinal));
    }

    [TestMethod]
    public void HelperAndRunbook_RequireSuccessfulSmokeTaskIdBeforeLegacyDeletion()
    {
        var helper = File.ReadAllText(Path.Combine(Root, "infra", "collector-lambda",
            "Invoke-CollectionQueueCutover.ps1"));
        StringAssert.Contains(helper, "if ([string]::IsNullOrWhiteSpace($SmokeTaskId))");
        StringAssert.Contains(helper, "DeleteLegacy requires -SmokeTaskId from a successful smoke test.");
        StringAssert.Contains(helper, "'DeleteLegacy' {");
        StringAssert.Contains(helper, "@('true', 'false')");

        var runbook = File.ReadAllText(Path.Combine(Root, "docs", "changes",
            "20260911_unified-collection-platform", "cutover-runbook.md"));
        StringAssert.Contains(runbook, "-Stage DeleteLegacy -VarFile <tfvars-path> -SmokeTaskId <task-id>");
        StringAssert.Contains(runbook, "Only after Gate 3 is recorded as successful");
        StringAssert.Contains(runbook, "horse-racing-prediction-collector-dlq");
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

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
    public void DeployWorkflow_RestoresPipelineAfterHealthCheckWithoutLegacyMigration()
    {
        var lambdaJobStart = DeployWorkflow.IndexOf("  deploy-collector-lambda:", StringComparison.Ordinal);
        var migrationJobStart = DeployWorkflow.IndexOf("  migrate-race-entry-owners:", lambdaJobStart,
            StringComparison.Ordinal);
        var lambdaJob = Slice(DeployWorkflow, lambdaJobStart, migrationJobStart);
        var guardStart = lambdaJob.IndexOf("      - id: collection-guard", StringComparison.Ordinal);
        var infrastructureStart = lambdaJob.IndexOf("      - name: Initialize collector infrastructure",
            guardStart, StringComparison.Ordinal);
        var guard = Slice(lambdaJob, guardStart, infrastructureStart);
        var preDeployPipelineRead = guard.IndexOf("\"$base/pipeline\"", StringComparison.Ordinal);
        var preDeployPause = guard.IndexOf("\"$base/pipeline/pause\"", StringComparison.Ordinal);
        var drainLoop = guard.IndexOf("for attempt in $(seq 1 60)", StringComparison.Ordinal);

        Assert.IsGreaterThanOrEqualTo(0, lambdaJobStart);
        Assert.IsGreaterThan(lambdaJobStart, migrationJobStart);
        StringAssert.Contains(lambdaJob, "name: Pause and drain collection before changing deployed versions");
        StringAssert.Contains(guard, "base=\"$API_BASE_URL/api/admin/collection\"");
        StringAssert.Contains(guard, "\"$base/pipeline\"");
        StringAssert.Contains(guard, "\"$base/pipeline/pause\"");
        StringAssert.Contains(guard, "\"$base/tasks?status=Running&limit=1\"");
        StringAssert.Contains(guard, "for attempt in $(seq 1 60)");
        StringAssert.Contains(guard, "Collection drain timed out; pipeline remains paused");
        Assert.IsGreaterThan(preDeployPipelineRead, preDeployPause);
        Assert.IsGreaterThan(preDeployPause, drainLoop);
        Assert.IsGreaterThan(guardStart,
            lambdaJob.IndexOf("      - name: Apply collector Lambda infrastructure", StringComparison.Ordinal),
            "The deployed-v1 pipeline pause and drain must precede the Lambda version change.");

        var apiDeployStart = DeployWorkflow.IndexOf("  deploy:", StringComparison.Ordinal);
        var apiDeployEnd = DeployWorkflow.IndexOf("  deploy-collector-lambda:", apiDeployStart,
            StringComparison.Ordinal);
        var apiDeploy = Slice(DeployWorkflow, apiDeployStart, apiDeployEnd);
        var restart = apiDeploy.IndexOf("      - name: Restart remote stack", StringComparison.Ordinal);
        var healthCheck = apiDeploy.IndexOf("      - name: Verify deployment health", restart,
            StringComparison.Ordinal);
        var restore = apiDeploy.IndexOf("      - name: Restore collection pipeline state after deployment",
            healthCheck, StringComparison.Ordinal);
        var restoreStep = apiDeploy[restore..];
        var pipelineStateRead = restoreStep.IndexOf("\"$base/pipeline-state\"", StringComparison.Ordinal);
        var postDeployDrainCheck = restoreStep.IndexOf("\"$base/tasks?status=Running&limit=1\"",
            StringComparison.Ordinal);
        var actionableFailureCheck = restoreStep.IndexOf("\"$base/failure-notifications?view=Actionable&limit=1\"",
            StringComparison.Ordinal);
        var pipelineResume = restoreStep.IndexOf("--data '{\"paused\":false}' \"$base/pipeline\"",
            StringComparison.Ordinal);

        Assert.IsGreaterThanOrEqualTo(0, apiDeployStart);
        StringAssert.Contains(apiDeploy, "needs: [build-and-push, deploy-collector-lambda]");
        Assert.IsGreaterThanOrEqualTo(0, restart);
        Assert.IsGreaterThan(restart, healthCheck);
        Assert.IsGreaterThan(healthCheck, restore);
        StringAssert.Contains(restoreStep,
            "base=\"https://${DOMAIN_NAME:-$(echo \"$LIGHTSAIL_HOST\" | tr '.' '-').sslip.io}/api/v2/admin/collection\"");
        StringAssert.Contains(restoreStep, "\"$base/pipeline-state\"");
        StringAssert.Contains(restoreStep, "\"$base/tasks?status=Running&limit=1\"");
        StringAssert.Contains(restoreStep, "\"$base/failure-notifications?view=Actionable&limit=1\"");
        StringAssert.Contains(restoreStep, "-X PUT");
        StringAssert.Contains(restoreStep, "--data '{\"paused\":false}' \"$base/pipeline\"");
        StringAssert.Contains(restoreStep, "Collection remains paused; recovery requires an explicit operator decision.");
        StringAssert.Contains(restoreStep, "Actionable failures remain; pipeline stays paused for explicit recovery");
        Assert.IsGreaterThan(pipelineStateRead, postDeployDrainCheck);
        Assert.IsGreaterThan(postDeployDrainCheck, actionableFailureCheck);
        Assert.IsGreaterThan(actionableFailureCheck, pipelineResume);
        Assert.IsFalse(DeployWorkflow.Contains("Migrate legacy race collection jobs", StringComparison.Ordinal));
        Assert.IsFalse(DeployWorkflow.Contains("/migrations/race-detail/", StringComparison.Ordinal));
    }

    [TestMethod]
    public void DeployWorkflow_PreviewsOwnerMigrationWithoutPausingOrApplying()
    {
        var migration = DeployWorkflow.IndexOf("  migrate-race-entry-owners:", StringComparison.Ordinal);
        var section = DeployWorkflow[migration..];
        var previewStepStart = section.IndexOf("      - name: Queue race entry owner data migration",
            StringComparison.Ordinal);
        var previewStep = section[previewStepStart..];
        var previewPath = previewStep.IndexOf("$base/migration-previews/race-entry-owner-repair",
            StringComparison.Ordinal);

        Assert.IsGreaterThanOrEqualTo(0, migration);
        StringAssert.Contains(section, "needs: [deploy, deploy-collector-lambda]");
        Assert.IsGreaterThanOrEqualTo(0, previewStepStart);
        Assert.IsGreaterThan(previewStepStart, previewPath);
        StringAssert.Contains(previewStep,
            "base=\"https://${DOMAIN_NAME:-$(echo \"$LIGHTSAIL_HOST\" | tr '.' '-').sslip.io}/api/v2/admin/collection\"");
        StringAssert.Contains(previewStep,
            "curl --fail --silent --show-error -X POST -H \"X-Api-Key: $API_KEY\"");
        StringAssert.Contains(previewStep, "preview-only until its target-selection change record is approved");
        Assert.IsFalse(previewStep.Contains("$base/pipeline/pause", StringComparison.Ordinal));
        Assert.IsFalse(previewStep.Contains("$base/migrations/race-entry-owner-repair", StringComparison.Ordinal));
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

    private static string Slice(string text, int start, int end)
    {
        Assert.IsGreaterThanOrEqualTo(0, start, "Section start marker was not found.");
        Assert.IsGreaterThan(start, end, "Section end marker must follow its start marker.");
        return text[start..end];
    }

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

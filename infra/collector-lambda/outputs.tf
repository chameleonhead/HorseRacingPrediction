output "repository_url" {
  value = aws_ecr_repository.collector.repository_url
}

output "function_name" {
  value = try(aws_lambda_function.collector[0].function_name, null)
}

output "queue_url" {
  value = local.active_queue_url
}

output "queue_arn" {
  value = local.active_queue_arn
}

output "resource_collection_queue_url" {
  value = aws_sqs_queue.resource_collection.url
}

output "resource_collection_queue_arn" {
  value = aws_sqs_queue.resource_collection.arn
}

output "legacy_collector_queue_url" {
  value = try(aws_sqs_queue.collector[0].url, null)
}

output "collector_alert_topic_arn" {
  value = aws_sns_topic.collector_alerts.arn
}

output "api_queue_sender_policy_arn" {
  value = aws_iam_policy.api_queue_sender.arn
}

output "lightsail_api_access_key_id" {
  value     = aws_iam_access_key.lightsail_api.id
  sensitive = true
}

output "lightsail_api_secret_access_key" {
  value     = aws_iam_access_key.lightsail_api.secret
  sensitive = true
}

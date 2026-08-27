output "repository_url" {
  value = aws_ecr_repository.collector.repository_url
}

output "function_name" {
  value = try(aws_lambda_function.collector[0].function_name, null)
}

output "queue_url" {
  value = aws_sqs_queue.collector.url
}

output "queue_arn" {
  value = aws_sqs_queue.collector.arn
}

output "api_queue_sender_policy_arn" {
  value = aws_iam_policy.api_queue_sender.arn
}

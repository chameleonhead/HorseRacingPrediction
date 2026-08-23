output "repository_url" {
  value = aws_ecr_repository.collector.repository_url
}

output "function_name" {
  value = try(aws_lambda_function.collector[0].function_name, null)
}

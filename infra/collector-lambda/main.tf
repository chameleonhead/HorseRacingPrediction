locals {
  function_enabled = var.image_uri != ""
}

resource "aws_ecr_repository" "collector" {
  name                 = "horse-racing-prediction-collector"
  image_tag_mutability = "IMMUTABLE"

  image_scanning_configuration {
    scan_on_push = true
  }
}

resource "aws_ecr_lifecycle_policy" "collector" {
  repository = aws_ecr_repository.collector.name
  policy = jsonencode({
    rules = [{
      rulePriority = 1
      description  = "Keep the latest 20 collector images"
      selection = {
        tagStatus   = "any"
        countType   = "imageCountMoreThan"
        countNumber = 20
      }
      action = { type = "expire" }
    }]
  })
}

resource "aws_iam_role" "collector" {
  name = "horse-racing-prediction-collector-lambda"
  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect    = "Allow"
      Principal = { Service = "lambda.amazonaws.com" }
      Action    = "sts:AssumeRole"
    }]
  })
}

resource "aws_iam_role_policy_attachment" "logs" {
  role       = aws_iam_role.collector.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AWSLambdaBasicExecutionRole"
}

resource "aws_sqs_queue" "collector_dlq" {
  name                      = "horse-racing-prediction-collector-dlq"
  message_retention_seconds = 1209600
}

resource "aws_sqs_queue" "collector" {
  name                       = "horse-racing-prediction-collector"
  visibility_timeout_seconds = 5400
  message_retention_seconds  = 345600
  receive_wait_time_seconds  = 20
  redrive_policy = jsonencode({
    deadLetterTargetArn = aws_sqs_queue.collector_dlq.arn
    maxReceiveCount     = 3
  })
}

resource "aws_iam_role_policy" "collector_queue_consumer" {
  role = aws_iam_role.collector.id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect = "Allow"
      Action = [
        "sqs:ReceiveMessage",
        "sqs:DeleteMessage",
        "sqs:ChangeMessageVisibility",
        "sqs:GetQueueAttributes"
      ]
      Resource = aws_sqs_queue.collector.arn
    }]
  })
}

resource "aws_iam_policy" "api_queue_sender" {
  name = "horse-racing-prediction-api-collection-queue-sender"
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect   = "Allow"
      Action   = ["sqs:SendMessage", "sqs:GetQueueAttributes"]
      Resource = aws_sqs_queue.collector.arn
    }]
  })
}

resource "aws_lambda_function" "collector" {
  count                          = local.function_enabled ? 1 : 0
  function_name                  = "horse-racing-prediction-collector"
  role                           = aws_iam_role.collector.arn
  package_type                   = "Image"
  image_uri                      = var.image_uri
  timeout                        = 900
  memory_size                    = 2048
  reserved_concurrent_executions = 1

  ephemeral_storage { size = 2048 }

  environment {
    variables = {
      ApiClient__BaseUrl                   = var.api_base_url
      ApiClient__ApiKey                    = var.api_key
      AgentProcessing__UseApiStateStore    = "true"
      AgentProcessing__CollectionBatchSize = "1"
      AgentProcessing__MaxConcurrentJobs   = "1"
      ASPNETCORE_ENVIRONMENT               = "Production"
    }
  }
}

resource "aws_lambda_event_source_mapping" "collector_queue" {
  count                              = local.function_enabled ? 1 : 0
  event_source_arn                   = aws_sqs_queue.collector.arn
  function_name                      = aws_lambda_function.collector[0].arn
  batch_size                         = 1
  maximum_batching_window_in_seconds = 0
  function_response_types            = ["ReportBatchItemFailures"]
}

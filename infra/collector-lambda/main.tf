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

resource "aws_lambda_function_event_invoke_config" "collector" {
  count                  = local.function_enabled ? 1 : 0
  function_name          = aws_lambda_function.collector[0].function_name
  maximum_retry_attempts = 0
}

resource "aws_cloudwatch_event_rule" "collector" {
  count               = local.function_enabled ? 1 : 0
  name                = "horse-racing-prediction-collector-schedule"
  schedule_expression = "rate(15 minutes)"
}

resource "aws_cloudwatch_event_target" "collector" {
  count = local.function_enabled ? 1 : 0
  rule  = aws_cloudwatch_event_rule.collector[0].name
  arn   = aws_lambda_function.collector[0].arn
}

resource "aws_lambda_permission" "scheduler" {
  count         = local.function_enabled ? 1 : 0
  statement_id  = "AllowEventBridgeInvocation"
  action        = "lambda:InvokeFunction"
  function_name = aws_lambda_function.collector[0].function_name
  principal     = "events.amazonaws.com"
  source_arn    = aws_cloudwatch_event_rule.collector[0].arn
}

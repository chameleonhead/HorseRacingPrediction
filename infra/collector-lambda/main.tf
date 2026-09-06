locals {
  function_enabled = var.image_uri != ""
}

data "aws_caller_identity" "current" {}

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

resource "aws_ecr_repository_policy" "collector_lambda" {
  repository = aws_ecr_repository.collector.name
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Sid    = "LambdaECRImageRetrievalPolicy"
      Effect = "Allow"
      Principal = {
        Service = "lambda.amazonaws.com"
      }
      Action = [
        "ecr:BatchGetImage",
        "ecr:GetDownloadUrlForLayer"
      ]
      Condition = {
        StringLike = {
          "aws:sourceArn" = "arn:aws:lambda:${var.aws_region}:${data.aws_caller_identity.current.account_id}:function:horse-racing-prediction-collector"
        }
      }
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
    # Lambda側のSQS再試行(可視性タイムアウトによる再配信)は行わない。1回の実行失敗で
    # 即座にDLQへ送る。リトライ判断はAPI(ジョブコントローラー)側の責務とし、
    # CollectionDeadLetterQueueReconcilerがDLQを回収してジョブをFailedにマークする。
    maxReceiveCount = 1
  })
}

resource "aws_sns_topic" "collector_alerts" {
  name = "horse-racing-prediction-collector-alerts"
}

resource "aws_sns_topic_subscription" "collector_alert_sms" {
  count     = trimspace(var.alert_phone_number) != "" ? 1 : 0
  topic_arn = aws_sns_topic.collector_alerts.arn
  protocol  = "sms"
  endpoint  = trimspace(var.alert_phone_number)
}

resource "aws_cloudwatch_metric_alarm" "collector_lambda_errors" {
  count               = local.function_enabled ? 1 : 0
  alarm_name          = "horse-racing-prediction-collector-lambda-errors"
  alarm_description   = "Collector Lambda returned an error."
  namespace           = "AWS/Lambda"
  metric_name         = "Errors"
  dimensions          = { FunctionName = aws_lambda_function.collector[0].function_name }
  statistic           = "Sum"
  period              = 60
  evaluation_periods  = 1
  threshold           = 1
  comparison_operator = "GreaterThanOrEqualToThreshold"
  treat_missing_data  = "notBreaching"
  alarm_actions       = [aws_sns_topic.collector_alerts.arn]
}

resource "aws_cloudwatch_metric_alarm" "collector_dlq_messages" {
  alarm_name          = "horse-racing-prediction-collector-dlq-messages"
  alarm_description   = "Collector messages reached the SQS dead-letter queue."
  namespace           = "AWS/SQS"
  metric_name         = "ApproximateNumberOfMessagesVisible"
  dimensions          = { QueueName = aws_sqs_queue.collector_dlq.name }
  statistic           = "Maximum"
  period              = 60
  evaluation_periods  = 1
  threshold           = 1
  comparison_operator = "GreaterThanOrEqualToThreshold"
  treat_missing_data  = "notBreaching"
  alarm_actions       = [aws_sns_topic.collector_alerts.arn]
}

resource "aws_cloudwatch_metric_alarm" "collector_oldest_message" {
  alarm_name          = "horse-racing-prediction-collector-oldest-message"
  alarm_description   = "Collector queue has a visible message older than 20 minutes."
  namespace           = "AWS/SQS"
  metric_name         = "ApproximateAgeOfOldestMessage"
  dimensions          = { QueueName = aws_sqs_queue.collector.name }
  statistic           = "Maximum"
  period              = 60
  evaluation_periods  = 5
  datapoints_to_alarm = 3
  threshold           = 1200
  comparison_operator = "GreaterThanOrEqualToThreshold"
  treat_missing_data  = "notBreaching"
  alarm_actions       = [aws_sns_topic.collector_alerts.arn]
}

resource "aws_iam_role_policy" "collector_queue_consumer" {
  role = aws_iam_role.collector.id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect = "Allow"
        Action = [
          "sqs:ReceiveMessage",
          "sqs:DeleteMessage",
          "sqs:ChangeMessageVisibility",
          "sqs:GetQueueAttributes"
        ]
        Resource = aws_sqs_queue.collector.arn
      },
      {
        # aws_lambda_function_event_invoke_config の on_failure 送信先（DLQ）へ
        # Lambdaランタイム自身がメッセージを送出するために必要。
        Effect   = "Allow"
        Action   = ["sqs:SendMessage"]
        Resource = aws_sqs_queue.collector_dlq.arn
      }
    ]
  })
}

resource "aws_iam_policy" "api_queue_sender" {
  name = "horse-racing-prediction-api-collection-queue-sender"
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect   = "Allow"
        Action   = ["sqs:SendMessage", "sqs:GetQueueAttributes", "sqs:GetQueueUrl", "sqs:PurgeQueue"]
        Resource = aws_sqs_queue.collector.arn
      },
      {
        Effect = "Allow"
        Action = [
          "sqs:GetQueueAttributes", "sqs:GetQueueUrl", "sqs:PurgeQueue",
          "sqs:ReceiveMessage", "sqs:DeleteMessage"
        ]
        Resource = aws_sqs_queue.collector_dlq.arn
      },
      {
        Effect   = "Allow"
        Action   = ["sns:Publish"]
        Resource = aws_sns_topic.collector_alerts.arn
      }
    ]
  })
}

resource "aws_iam_user" "lightsail_api" {
  name = "horse-racing-prediction-lightsail-api"
}

resource "aws_iam_user_policy_attachment" "lightsail_api_queue_sender" {
  user       = aws_iam_user.lightsail_api.name
  policy_arn = aws_iam_policy.api_queue_sender.arn
}

# Lightsail instances do not support EC2 instance profiles. The fixed key is
# stored as a sensitive value in the encrypted Terraform backend and copied to
# the instance during deployment; it is never stored as a GitHub secret.
resource "aws_iam_access_key" "lightsail_api" {
  user = aws_iam_user.lightsail_api.name
}

resource "aws_lambda_function" "collector" {
  count                          = local.function_enabled ? 1 : 0
  function_name                  = "horse-racing-prediction-collector"
  role                           = aws_iam_role.collector.arn
  package_type                   = "Image"
  image_uri                      = var.image_uri
  timeout                        = 900
  memory_size                    = 4096
  reserved_concurrent_executions = 1

  depends_on = [
    aws_ecr_repository_policy.collector_lambda,
    aws_iam_role_policy_attachment.logs,
    aws_iam_role_policy.collector_queue_consumer
  ]

  ephemeral_storage { size = 4096 }

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

# SQS event source mapping（同期呼び出し）では本来この非同期呼び出し設定は参照されないが、
# コンソール上の既定値（再試行2回・送信先未設定）のままだと運用者が混乱するため、実際の
# 挙動（SQS側のredrive_policyで1回失敗即DLQ、main.tf内 aws_sqs_queue.collector 参照）と
# 一致するよう明示的に再試行0回・失敗時の送信先をDLQへ設定しておく。
resource "aws_lambda_function_event_invoke_config" "collector" {
  count                        = local.function_enabled ? 1 : 0
  function_name                = aws_lambda_function.collector[0].function_name
  maximum_retry_attempts       = 0
  maximum_event_age_in_seconds = 21600

  destination_config {
    on_failure {
      destination = aws_sqs_queue.collector_dlq.arn
    }
  }
}

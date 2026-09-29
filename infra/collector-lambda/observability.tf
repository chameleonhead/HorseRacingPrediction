locals {
  collection_dispatch_namespace   = "HorseRacingPrediction/CollectionDispatch"
  collection_dispatch_lanes       = ["Realtime", "Normal", "Background"]
  collection_dispatch_definitions = concat(var.collection_dispatch_definition_labels, ["OTHER"])
  collection_dispatch_lane_definitions = {
    for pair in setproduct(local.collection_dispatch_lanes, local.collection_dispatch_definitions) :
    "${lower(pair[0])}-${pair[1]}" => { lane = pair[0], definition = pair[1] }
  }
  collection_dispatch_starvation_periods = ceil(max(300, 3 * var.collection_dispatch_interval_seconds) / 60)
}

variable "collection_dispatch_interval_seconds" {
  description = "Configured API collection dispatcher interval used by lane-starvation alarms."
  type        = number
  default     = 1

  validation {
    condition     = var.collection_dispatch_interval_seconds >= 1
    error_message = "The collection dispatcher interval must be at least one second."
  }
}

variable "collection_dispatch_definition_labels" {
  description = "Registered task definition labels emitted by the API telemetry allowlist (at most 16)."
  type        = list(string)
  default     = ["race-discovery", "race-detail", "race-odds"]

  validation {
    condition = (
      length(var.collection_dispatch_definition_labels) <= 16
      && length(distinct(var.collection_dispatch_definition_labels)) == length(var.collection_dispatch_definition_labels)
      && alltrue([for label in var.collection_dispatch_definition_labels : can(regex("^[A-Za-z0-9-]{1,64}$", label))])
    )
    error_message = "Definition telemetry labels must be unique, bounded safe labels; register no more than 16."
  }
}

resource "aws_iam_user_policy" "lightsail_api_collection_dispatch_metrics" {
  name = "horse-racing-prediction-collection-dispatch-metrics"
  user = aws_iam_user.lightsail_api.name
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect   = "Allow"
      Action   = ["cloudwatch:PutMetricData"]
      Resource = "*"
      Condition = {
        StringEquals = {
          "cloudwatch:namespace" = local.collection_dispatch_namespace
        }
      }
    }]
  })
}

resource "aws_cloudwatch_metric_alarm" "collection_ready_missing_outbox" {
  alarm_name          = "horse-racing-prediction-collection-ready-missing-outbox"
  alarm_description   = "Ready collection tasks lack a current-generation outbox row for the configured persistence window."
  namespace           = local.collection_dispatch_namespace
  metric_name         = "ready_missing_current_outbox"
  statistic           = "Maximum"
  period              = 60
  evaluation_periods  = local.collection_dispatch_starvation_periods
  datapoints_to_alarm = local.collection_dispatch_starvation_periods
  threshold           = 1
  comparison_operator = "GreaterThanOrEqualToThreshold"
  treat_missing_data  = "notBreaching"
  alarm_actions       = [aws_sns_topic.collector_alerts.arn]
}

resource "aws_cloudwatch_metric_alarm" "collection_outbox_cardinality_anomaly" {
  alarm_name          = "horse-racing-prediction-collection-outbox-cardinality-anomaly"
  alarm_description   = "Duplicate current-generation collection outboxes persist for five minutes."
  namespace           = local.collection_dispatch_namespace
  metric_name         = "outbox_cardinality_anomaly_tasks"
  statistic           = "Maximum"
  period              = 60
  evaluation_periods  = local.collection_dispatch_starvation_periods
  datapoints_to_alarm = local.collection_dispatch_starvation_periods
  threshold           = 1
  comparison_operator = "GreaterThanOrEqualToThreshold"
  treat_missing_data  = "notBreaching"
  alarm_actions       = [aws_sns_topic.collector_alerts.arn]
}

resource "aws_cloudwatch_metric_alarm" "collection_lane_starvation" {
  for_each = local.function_enabled ? local.collection_dispatch_lane_definitions : {}

  alarm_name          = "horse-racing-prediction-collection-starvation-${each.key}"
  alarm_description   = "Eligible collection work in this lane and definition has no acquire or completion progress."
  comparison_operator = "GreaterThanOrEqualToThreshold"
  evaluation_periods  = local.collection_dispatch_starvation_periods
  datapoints_to_alarm = local.collection_dispatch_starvation_periods
  threshold           = 1
  treat_missing_data  = "notBreaching"
  alarm_actions       = [aws_sns_topic.collector_alerts.arn]

  metric_query {
    id          = "eligible"
    return_data = false
    metric {
      namespace   = local.collection_dispatch_namespace
      metric_name = "eligible_ready_rows"
      period      = 60
      stat        = "Maximum"
      dimensions  = { Lane = each.value.lane, Definition = each.value.definition }
    }
  }

  metric_query {
    id          = "acquired"
    return_data = false
    metric {
      namespace   = local.collection_dispatch_namespace
      metric_name = "acquire_success_by_lane_definition_total"
      period      = 60
      stat        = "Sum"
      dimensions  = { Lane = each.value.lane, Definition = each.value.definition }
    }
  }

  metric_query {
    id          = "completed"
    return_data = false
    metric {
      namespace   = local.collection_dispatch_namespace
      metric_name = "terminal_task_completion_by_lane_definition_total"
      period      = 60
      stat        = "Sum"
      dimensions  = { Lane = each.value.lane, Definition = each.value.definition }
    }
  }

  metric_query {
    id          = "starved"
    expression  = "IF(eligible > 0, IF(FILL(acquired, 0) + FILL(completed, 0) == 0, 1, 0), 0)"
    label       = "Eligible work without acquire or completion"
    return_data = true
  }
}

resource "aws_cloudwatch_metric_alarm" "collection_nowork_at_capacity" {
  alarm_name          = "horse-racing-prediction-collection-nowork-at-capacity"
  alarm_description   = "Five-minute NoWork activity coincides with full eligible capacity and no acquire success."
  comparison_operator = "GreaterThanOrEqualToThreshold"
  evaluation_periods  = 1
  datapoints_to_alarm = 1
  threshold           = 1
  treat_missing_data  = "notBreaching"
  alarm_actions       = [aws_sns_topic.collector_alerts.arn]

  metric_query {
    id          = "nowork"
    return_data = false
    metric {
      namespace   = local.collection_dispatch_namespace
      metric_name = "acquire_nowork_total"
      period      = 300
      stat        = "Sum"
    }
  }

  metric_query {
    id          = "capacity"
    return_data = false
    metric {
      namespace   = local.collection_dispatch_namespace
      metric_name = "eligible_in_flight_count"
      period      = 300
      stat        = "Maximum"
    }
  }

  metric_query {
    id          = "maximum"
    return_data = false
    metric {
      namespace   = local.collection_dispatch_namespace
      metric_name = "max_in_flight_envelopes"
      period      = 300
      stat        = "Maximum"
    }
  }

  metric_query {
    id          = "success"
    return_data = false
    metric {
      namespace   = local.collection_dispatch_namespace
      metric_name = "acquire_success_total"
      period      = 300
      stat        = "Sum"
    }
  }

  metric_query {
    id          = "saturated"
    expression  = "IF(nowork >= 5, IF(capacity >= maximum, IF(FILL(success, 0) == 0, 1, 0), 0), 0)"
    label       = "NoWork at capacity without acquisition"
    return_data = true
  }
}

resource "aws_cloudwatch_metric_alarm" "collection_dispatch_failures" {
  alarm_name          = "horse-racing-prediction-collection-dispatch-failures"
  alarm_description   = "Reservation release or SQS wake send failures exceed five in five minutes."
  comparison_operator = "GreaterThanThreshold"
  evaluation_periods  = 1
  datapoints_to_alarm = 1
  threshold           = 5
  treat_missing_data  = "notBreaching"
  alarm_actions       = [aws_sns_topic.collector_alerts.arn]

  metric_query {
    id          = "release"
    return_data = false
    metric {
      namespace   = local.collection_dispatch_namespace
      metric_name = "reservation_release_failure_total"
      period      = 300
      stat        = "Sum"
    }
  }

  metric_query {
    id          = "send"
    return_data = false
    metric {
      namespace   = local.collection_dispatch_namespace
      metric_name = "queue_send_failure_total"
      period      = 300
      stat        = "Sum"
    }
  }

  metric_query {
    id          = "failures"
    expression  = "FILL(release, 0) + FILL(send, 0)"
    label       = "Reservation release and queue send failures"
    return_data = true
  }
}

resource "aws_cloudwatch_dashboard" "collection_dispatch" {
  dashboard_name = "horse-racing-prediction-collection-dispatch"
  dashboard_body = jsonencode({
    widgets = [
      {
        type   = "metric"
        x      = 0
        y      = 0
        width  = 12
        height = 6
        properties = {
          title  = "Collection dispatch cycle outcomes"
          region = var.aws_region
          view   = "timeSeries"
          period = 300
          metrics = [
            [local.collection_dispatch_namespace, "dispatch_cycle_total", "Outcome", "NoCandidates"],
            [local.collection_dispatch_namespace, "dispatch_cycle_total", "Outcome", "Reserved"],
            [local.collection_dispatch_namespace, "dispatch_cycle_total", "Outcome", "CapacityFull"],
            [local.collection_dispatch_namespace, "dispatch_cycle_total", "Outcome", "CandidateRejected"],
            [local.collection_dispatch_namespace, "dispatch_cycle_total", "Outcome", "ReserveConflict"],
            [local.collection_dispatch_namespace, "dispatch_cycle_total", "Outcome", "WakeSent"],
            [local.collection_dispatch_namespace, "dispatch_cycle_total", "Outcome", "WakeSendDefiniteFailure"],
            [local.collection_dispatch_namespace, "dispatch_cycle_total", "Outcome", "WakeSendAmbiguousFailure"],
            [local.collection_dispatch_namespace, "dispatch_cycle_total", "Outcome", "WakeReceiptPersistFailure"],
            [local.collection_dispatch_namespace, "dispatch_cycle_by_lane_definition_total", "Outcome", "NoCandidates", "Lane", "Realtime", "Definition", "race-detail"],
            [local.collection_dispatch_namespace, "dispatch_cycle_by_lane_definition_total", "Outcome", "Reserved", "Lane", "Normal", "Definition", "race-detail"],
            [local.collection_dispatch_namespace, "wake_sent_total"],
            [local.collection_dispatch_namespace, "wake_sent_by_lane_definition_total", "Lane", "Realtime", "Definition", "race-detail"],
          ]
        }
      },
      {
        type   = "metric"
        x      = 12
        y      = 0
        width  = 12
        height = 6
        properties = {
          title  = "Backlog anomalies and eligible capacity"
          region = var.aws_region
          view   = "timeSeries"
          period = 60
          metrics = [
            [local.collection_dispatch_namespace, "ready_missing_current_outbox"],
            [local.collection_dispatch_namespace, "outbox_cardinality_anomaly_tasks"],
            [local.collection_dispatch_namespace, "eligible_in_flight_count"],
            [local.collection_dispatch_namespace, "max_in_flight_envelopes"],
            [local.collection_dispatch_namespace, "active_eligible_reservations"],
            [local.collection_dispatch_namespace, "expired_eligible_reservations"],
            [local.collection_dispatch_namespace, "reservation_release_total"],
            [local.collection_dispatch_namespace, "reservation_release_by_outcome_total", "Outcome", "Released"],
            [local.collection_dispatch_namespace, "reservation_release_failure_total"],
            [local.collection_dispatch_namespace, "queue_send_failure_total"],
          ]
        }
      },
      {
        type   = "metric"
        x      = 0
        y      = 6
        width  = 24
        height = 6
        properties = {
          title  = "Acquire and completion throughput by lane and definition"
          region = var.aws_region
          view   = "timeSeries"
          period = 300
          metrics = flatten([
            for pair in setproduct(local.collection_dispatch_lanes, local.collection_dispatch_definitions) : [
              [local.collection_dispatch_namespace, "eligible_ready_rows", "Lane", pair[0], "Definition", pair[1]],
              [local.collection_dispatch_namespace, "oldest_eligible_age_seconds", "Lane", pair[0], "Definition", pair[1]],
              [local.collection_dispatch_namespace, "acquire_success_by_lane_definition_total", "Lane", pair[0], "Definition", pair[1]],
              [local.collection_dispatch_namespace, "terminal_task_completion_by_lane_definition_total", "Lane", pair[0], "Definition", pair[1]],
            ]
          ])
        }
      },
    ]
  })
}

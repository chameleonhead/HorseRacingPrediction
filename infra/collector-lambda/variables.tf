variable "aws_region" {
  type        = string
  description = "AWS region."
}

variable "image_uri" {
  type        = string
  description = "Immutable ECR image URI deployed to Lambda."
  default     = ""
}

variable "api_base_url" {
  type        = string
  description = "Public HTTPS base URL of the API."
}

variable "api_key" {
  type        = string
  description = "API key used by the collector worker."
  sensitive   = true
  default     = ""
}

variable "alert_email" {
  type        = string
  description = "Email address subscribed to collection failure alerts. Empty disables the email subscription."
  default     = ""
}

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

variable "alert_phone_number" {
  type        = string
  description = "E.164 phone number subscribed to collection failure SMS alerts. Empty disables the SMS subscription."
  default     = ""

  validation {
    condition     = trimspace(var.alert_phone_number) == "" || can(regex("^\\+[1-9][0-9]{7,14}$", trimspace(var.alert_phone_number)))
    error_message = "alert_phone_number must be empty or an E.164 phone number such as +819012345678."
  }
}

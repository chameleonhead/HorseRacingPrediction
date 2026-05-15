variable "aws_region" {
  description = "AWS region for the Lightsail resources."
  type        = string
  default     = "us-east-1"
}

variable "project_name" {
  description = "Prefix for Lightsail resource names."
  type        = string
  default     = "horse-racing-prediction"
}

variable "availability_zone" {
  description = "Availability zone for the instance."
  type        = string
  default     = "us-east-1a"
}

variable "blueprint_id" {
  description = "Lightsail blueprint id. Ubuntu 22.04 is a stable default for Docker hosts."
  type        = string
  default     = "ubuntu_22_04"
}

variable "bundle_id" {
  description = "Instance size. micro_3_0 is the practical low-cost default for Docker + .NET + Caddy."
  type        = string
  default     = "micro_3_0"
}

variable "deploy_user" {
  description = "Primary SSH user created by the image."
  type        = string
  default     = "ubuntu"
}

variable "app_directory" {
  description = "Directory on the instance that stores the compose deployment."
  type        = string
  default     = "/opt/horse-racing-prediction/app"
}

variable "ssh_public_key" {
  description = "Public key material that will be imported into Lightsail for SSH access."
  type        = string
  sensitive   = true
}

output "instance_name" {
  description = "Lightsail instance name."
  value       = aws_lightsail_instance.api.name
}

output "static_ip" {
  description = "Public static IP to point the domain at."
  value       = aws_lightsail_static_ip.api.ip_address
}

output "app_directory" {
  description = "Remote application directory used by docker compose."
  value       = var.app_directory
}

output "public_hostname" {
  description = "sslip.io hostname clients should use for HTTPS access. Caddy obtains a browser-trusted Let's Encrypt certificate for this name automatically."
  value       = local.public_hostname
}

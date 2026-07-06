locals {
  instance_name = "${var.project_name}-api"
  key_pair_name = "${var.project_name}-deploy-key"
  # sslip.io resolves "<ip-with-dashes>.sslip.io" to the embedded IP, so Caddy can obtain
  # a CA-trusted Let's Encrypt certificate without owning a domain.
  public_hostname = "${replace(aws_lightsail_static_ip.api.ip_address, ".", "-")}.sslip.io"
}

resource "aws_lightsail_key_pair" "deploy" {
  name       = local.key_pair_name
  public_key = var.ssh_public_key
}

resource "aws_lightsail_instance" "api" {
  name              = local.instance_name
  availability_zone = var.availability_zone
  blueprint_id      = var.blueprint_id
  bundle_id         = var.bundle_id
  key_pair_name     = aws_lightsail_key_pair.deploy.name
  user_data = templatefile("${path.module}/cloud-init.yaml.tftpl", {
    deploy_user   = var.deploy_user
    app_directory = var.app_directory
  })

  tags = {
    Project = var.project_name
    Role    = "api"
  }
}

resource "aws_lightsail_static_ip" "api" {
  name = "${local.instance_name}-ip"
}

resource "aws_lightsail_static_ip_attachment" "api" {
  static_ip_name = aws_lightsail_static_ip.api.name
  instance_name  = aws_lightsail_instance.api.name
}

resource "aws_lightsail_instance_public_ports" "api" {
  instance_name = aws_lightsail_instance.api.name

  port_info {
    protocol  = "tcp"
    from_port = 22
    to_port   = 22
  }

  port_info {
    protocol  = "tcp"
    from_port = 80
    to_port   = 80
  }

  port_info {
    protocol  = "tcp"
    from_port = 443
    to_port   = 443
  }
}

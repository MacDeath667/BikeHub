provider "truenas" {
  endpoint = var.truenas_endpoint
  ca_cert  = pathexpand(var.truenas_ca_cert_path)
}

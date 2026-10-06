output "vm_id" {
  description = "Numeric VM ID assigned by TrueNAS."
  value       = truenas_vm.bikehub.id
}

output "vm_name" {
  description = "VM name."
  value       = truenas_vm.bikehub.name
}

output "debian_cloud_image_version" {
  description = "Pinned Debian cloud image build imported into the VM zvol."
  value       = var.debian_cloud_image_version
}

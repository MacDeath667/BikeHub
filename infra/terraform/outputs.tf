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

output "vm_ip" {
  description = "Reserved DHCP address configured for the VM NIC. Confirm the reservation matches the TrueNAS NIC MAC after the first apply."
  value       = var.vm_ip
}

output "ssh_command" {
  description = "SSH command for the Ansible user."
  value       = "ssh ${var.ansible_user}@${var.vm_ip}"
}

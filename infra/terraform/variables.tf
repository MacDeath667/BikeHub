variable "truenas_endpoint" {
  description = "TrueNAS WebSocket API endpoint, for example wss://nas.example.lan/api/current."
  type        = string
}

variable "truenas_ca_cert_path" {
  description = "Path to the local CA certificate used to verify the TrueNAS HTTPS certificate."
  type        = string
  default     = "~/.config/truenas/pki/bikehub-ca.crt"
}

variable "truenas_ssh_target" {
  description = "SSH destination for image import, in user@host form. The account must be able to run sudo; terraform-apply.sh supplies the password through SSH stdin."
  type        = string
}

variable "truenas_ssh_port" {
  description = "SSH port on TrueNAS used to import the verified cloud image."
  type        = number
  default     = 22
}

variable "truenas_ssh_identity_file" {
  description = "Local SSH private-key path used to reach TrueNAS. Defaults to ~/.ssh/truenas_admin."
  type        = string
  default     = "~/.ssh/truenas_admin"
}

variable "vm_name" {
  description = "Alphanumeric TrueNAS VM name and guest hostname."
  type        = string
  default     = "bikehub01"

  validation {
    condition     = can(regex("^[A-Za-z0-9]+$", var.vm_name))
    error_message = "vm_name must contain only ASCII letters and digits because of the TrueNAS API restriction."
  }
}

variable "vm_vcpus" {
  description = "Number of virtual CPUs."
  type        = number
  default     = 2
}

variable "vm_memory_mb" {
  description = "VM memory in MiB, as expected by the TrueNAS API."
  type        = number
  default     = 4096
}

variable "vm_disk_size_gb" {
  description = "OS zvol size in GiB."
  type        = number
  default     = 30
}

variable "debian_cloud_image_version" {
  description = "Pinned official Debian cloud image build version from Debian's daily JSON metadata."
  type        = string
  default     = "20261005-2622"
}

variable "debian_cloud_image_sha512" {
  description = "SHA-512 digest from the matching official Debian daily JSON metadata."
  type        = string
  default     = "2uNf5RtB+l2Lb54C6anUOMVw1Kog/2fSNn5WFQN52qpu7s8JopktTMTv/HWNFSrMzANhjO8K/nE6IbWm8yOe3A"
}

variable "vm_pool" {
  description = "Existing ZFS pool on TrueNAS for the VM disk."
  type        = string
}

variable "vm_network_attach" {
  description = "Existing TrueNAS bridge interface to which the VirtIO NIC is attached."
  type        = string
}

variable "vm_nic_mac" {
  description = "Unique, stable MAC address for this VM; do not duplicate it on the same LAN."
  type        = string

  validation {
    condition     = can(regex("^([0-9A-Fa-f]{2}:){5}[0-9A-Fa-f]{2}$", var.vm_nic_mac))
    error_message = "vm_nic_mac must be a colon-separated MAC address."
  }
}

variable "vm_running" {
  description = "Whether the VM should be running. Keep false while devices are attached; set true in a later apply after preparing the image and seed ISO."
  type        = bool
  default     = false
}

variable "ansible_user" {
  description = "Linux account used by Ansible and SSH."
  type        = string
  default     = "ansible"
}

variable "ssh_public_key_path" {
  description = "Path to the SSH public key installed for ansible_user. The default is the generated key under ~/.ssh."
  type        = string
  default     = "~/.ssh/truenas_admin.pub"
}

variable "cloud_init_iso_path" {
  description = "TrueNAS path of a cloud-init NoCloud seed ISO containing the rendered user-data and meta-data."
  type        = string
}

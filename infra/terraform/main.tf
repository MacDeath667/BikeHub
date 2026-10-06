locals {
  vm_disk_zvol       = "${var.vm_pool}/${var.vm_name}-os"
  ssh_public_key     = trimspace(file(pathexpand(var.ssh_public_key_path)))
  cloud_init_user_data = templatefile("${path.module}/cloud-init/user-data.yaml", {
    vm_name     = var.vm_name
    ansible_user = var.ansible_user
    ssh_public_key = local.ssh_public_key
  })
}

resource "truenas_zvol" "os" {
  name        = local.vm_disk_zvol
  volsize     = var.vm_disk_size_gb * 1024 * 1024 * 1024
  compression = "lz4"
  comments    = "OS disk for ${var.vm_name}; populated from the pinned official Debian 13 cloud image."
}

resource "terraform_data" "debian_cloud_image" {
  triggers_replace = [
    truenas_zvol.os.id,
    var.debian_cloud_image_version,
    var.debian_cloud_image_sha512,
  ]

  provisioner "local-exec" {
    command = "bash \"${path.module}/scripts/import-debian-cloud-image.sh\""

    environment = {
      TRUENAS_SSH_TARGET        = var.truenas_ssh_target
      TRUENAS_SSH_PORT          = tostring(var.truenas_ssh_port)
      TRUENAS_SSH_IDENTITY_FILE = pathexpand(var.truenas_ssh_identity_file)
      TRUENAS_ZVOL_PATH         = "/dev/zvol/${local.vm_disk_zvol}"
      DEBIAN_IMAGE_VERSION      = var.debian_cloud_image_version
      DEBIAN_IMAGE_SHA512       = var.debian_cloud_image_sha512
    }
  }
}

resource "terraform_data" "cloud_init_seed" {
  triggers_replace = [
    sha256(local.cloud_init_user_data),
    var.cloud_init_iso_path,
  ]

  provisioner "local-exec" {
    command = "bash \"${path.module}/scripts/create-cloud-init-seed.sh\""

    environment = {
      TRUENAS_SSH_TARGET        = var.truenas_ssh_target
      TRUENAS_SSH_PORT          = tostring(var.truenas_ssh_port)
      TRUENAS_SSH_IDENTITY_FILE = pathexpand(var.truenas_ssh_identity_file)
      CLOUD_INIT_ISO_PATH       = var.cloud_init_iso_path
      CLOUD_INIT_INSTANCE_ID    = var.vm_name
      CLOUD_INIT_USER_DATA      = local.cloud_init_user_data
    }
  }
}

resource "truenas_vm" "bikehub" {
  name       = var.vm_name
  description = "BikeHub Debian 13 application host"
  vcpus      = var.vm_vcpus
  memory     = var.vm_memory_mb * 1024 * 1024
  cores      = var.vm_vcpus
  bootloader = "UEFI"
  autostart  = true
  running    = var.vm_running
}

resource "truenas_vm_device" "os_disk" {
  vm = truenas_vm.bikehub.id

  depends_on = [terraform_data.debian_cloud_image]

  attributes = jsonencode({
    dtype = "DISK"
    path  = "/dev/zvol/${local.vm_disk_zvol}"
    type  = "VIRTIO"
  })

  order = 1000
}

resource "truenas_vm_device" "cloud_init" {
  vm = truenas_vm.bikehub.id

  depends_on = [terraform_data.cloud_init_seed]

  attributes = jsonencode({
    dtype = "CDROM"
    path  = var.cloud_init_iso_path
  })

  order = 2000
}

resource "truenas_vm_device" "nic" {
  vm = truenas_vm.bikehub.id

  attributes = jsonencode({
    dtype      = "NIC"
    type       = "VIRTIO"
    nic_attach = var.vm_network_attach
  })

  order = 3000
}

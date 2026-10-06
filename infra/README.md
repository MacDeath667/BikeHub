# BikeHub infrastructure

Terraform provisions a TrueNAS VM from the official Debian 13 generic cloud
image. The Debian Network Install ISO at `/mnt/FileTrash/Downloads/Debian 13.7 Network Install.iso`
is not needed for this cloud-image flow.

## Debian cloud image

The build is pinned to Debian image version `20261005-2622`, discovered in
the supplied official `daily/latest` JSON. The QCOW2 artifact SHA-512 is
`2uNf5RtB+l2Lb54C6anUOMVw1Kog/2fSNn5WFQN52qpu7s8JopktTMTv/HWNFSrMzANhjO8K/nE6IbWm8yOe3A`.
Terraform runs `scripts/import-debian-cloud-image.sh` after creating the OS
zvol. The script downloads the pinned QCOW2 on TrueNAS, verifies the published
SHA-512, and converts it to raw directly into that zvol. It refuses to write
if the target zvol is not empty.

For this step, TrueNAS must be reachable over SSH. The configured SSH user
must be able to run `sudo`. Terraform's `local-exec` does not forward terminal
input to its child process, so use `terraform/terraform-apply.sh` to collect
the sudo password in the local terminal and pass it to the remote `sudo -S`
commands through SSH stdin. The password is not stored in Terraform variables
or state. No passwordless sudo rule is required. The VM uses VirtIO and the
cloud image boots with UEFI.

## Cloud-init seed

The Debian cloud image includes cloud-init. Terraform renders
`terraform/cloud-init/user-data.yaml`, builds a NoCloud seed ISO, and uploads it
to TrueNAS at `cloud_init_iso_path`. The cloud-init configuration creates the
Ansible user, installs/starts SSH, enables key-based access, disables
password/root SSH, and grants passwordless sudo. It does not install Docker,
nginx, PostgreSQL, BikeHub, or Ansible.

## Configure and apply

Copy `terraform/terraform.tfvars.example` to
`terraform/terraform.tfvars`. Set the API endpoint, SSH destination, ZFS pool,
network interface, and seed ISO destination. Terraform
reads the public key from `~/.ssh/truenas_admin.pub` and uses the matching
private key at `~/.ssh/truenas_admin`. Keep the TrueNAS API key in
`TRUENAS_API_KEY`; never commit private keys or state. Locally, macOS `hdiutil`
or `genisoimage`, `ssh`, and `scp` are required. Run applies through the wrapper
so it can securely prompt for the remote sudo password.

```sh
cd infra/terraform
export TRUENAS_API_KEY='...'
terraform init
terraform plan
./terraform-apply.sh -var='vm_running=false'
```

The first apply creates the zvol and VM devices while the VM is stopped, then
downloads, verifies, and imports the image. Once that apply succeeds, start the
VM with a second apply:

```sh
./terraform-apply.sh -var='vm_running=true'
```

The router assigns the VM its address over DHCP. Find the actual lease for
`bikehub-01` in the router's DHCP client/lease list. To run Ansible, pass that
address through `BIKEHUB_VM_IP`, for example:

```sh
export BIKEHUB_VM_IP=192.168.0.123
ansible-playbook -i ../ansible/inventory/hosts.yml ../ansible/playbooks/server.yml
```

The official provider documents TrueNAS 25.04 as API-compatible but not fully
tested, so validate the exact 25.04.x build against a disposable VM first.

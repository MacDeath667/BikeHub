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
to TrueNAS at `cloud_init_iso_path`. The seed also includes a DHCP
`network-config` matching the guest's VirtIO NIC driver (`virtio_net`), not a
host bridge/interface name or guest interface name. Set `vm_nic_mac` to a
unique, stable MAC for this VM; two VMs on the same LAN must not share one. The
cloud-init user-data creates the Ansible user, installs/starts SSH, enables
key-based access, disables password/root SSH, and grants passwordless sudo. It
does not install Docker, nginx, PostgreSQL, BikeHub, or Ansible.

## Configure and apply

Copy `terraform/terraform.tfvars.example` to
`terraform/terraform.tfvars`. Set the API endpoint, SSH destination, ZFS pool,
bridge interface, and seed ISO destination. Terraform
reads the public key from `~/.ssh/truenas_admin.pub` and uses the matching
private key at `~/.ssh/truenas_admin`. Keep the TrueNAS API key in
`TRUENAS_API_KEY`; never commit private keys or state. Locally, macOS `hdiutil`
or `genisoimage`, `ssh`, and `scp` are required. Run applies through the wrapper
so it can securely prompt for the remote sudo password.

The VM NIC must attach to a TrueNAS bridge (configured as `br1` in the tfvars),
not directly to the physical `enp4s0` interface. The bridge does not exist yet.
Before applying this network change, create it in TrueNAS **System > Network**:
stop apps and VMs using the interface, remove the address from `enp4s0`, add a
bridge with `enp4s0` as its member, and move the NAS management address to the
bridge. Preserve the current management IP, subnet, and gateway, then use
TrueNAS **Test Changes** and confirm access before saving. These steps can
interrupt NAS/network access; keep local console access available and follow
the [TrueNAS 25.10 bridge guide](https://www.truenas.com/docs/scale/25.10/scaletutorials/network/containernasbridge/).
Only proceed once the bridge is visible as `br1` and has the NAS management IP.
Power off `bikehub01`, then review the Terraform plan before applying so the NIC
reattachment is made while the VM is stopped.

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
`bikehub01` in the router's DHCP client/lease list. To run Ansible, pass that
address through `BIKEHUB_VM_IP`.

## Ansible application deployment

The Ansible playbook installs nginx, Docker (only to build the app image), and
single-node K3s with its bundled Traefik disabled. It imports the image into
K3s, runs database migrations as a one-off Job, and deploys three BikeHub API
pods behind a Kubernetes NodePort and the host nginx reverse proxy. The external
PostgreSQL server is not installed or managed by this playbook; the target
database must already exist and the supplied user must be able to apply schema
migrations to it. Migrations run once in a Kubernetes Job before the API pods
start. Kubernetes Secret encryption at rest is enabled in K3s.

Create the ignored, encrypted credentials file on the Ansible control
machine:

```sh
ansible-vault edit infra/ansible/inventory/group_vars/bikehub/vault.yml
```

Put both variables in the editor that opens. Replace the example connection
string and choose a unique SSH password of at least 12 characters:

```yaml
bikehub_database_connection_string: "Host=db.example.lan;Port=5432;Database=bikehub;Username=bikehub;Password=...;"
vault_ansible_password: "choose-a-long-unique-password"
```

Once the VM boots and its DHCP address is known, run the one-time key-based
bootstrap. It sets the `ansible` account password from Vault and enables SSH
password authentication; root SSH remains disabled:

```sh
BIKEHUB_VM_IP=192.168.0.123 ansible-playbook \
  -i infra/ansible/inventory/bootstrap.yml \
  infra/ansible/playbooks/bootstrap-password.yml \
  --ask-vault-pass
```

Then run the normal deployment playbook from the repository root. Its inventory
uses the password from Vault:

```sh
BIKEHUB_VM_IP=192.168.0.123 ansible-playbook \
  -i infra/ansible/inventory/hosts.yml \
  infra/ansible/playbooks/server.yml \
  --ask-vault-pass
```

After the play succeeds, nginx serves the API on `http://<VM-IP>/`. Check the
three replicas with:

```sh
ssh -i ~/.ssh/truenas_admin ansible@<VM-IP> 'sudo k3s kubectl get pods -n bikehub'
```

The official provider documents TrueNAS 25.04 as API-compatible but not fully
tested, so validate the exact 25.04.x build against a disposable VM first.

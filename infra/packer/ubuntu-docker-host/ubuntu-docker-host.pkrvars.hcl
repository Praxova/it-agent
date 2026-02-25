# ============================================================================
# ubuntu-docker-host.pkrvars.hcl — Variable Values
# ============================================================================
# DO NOT commit this file to Git — contains API tokens.
# Copy from ubuntu-docker-host.pkrvars.hcl.example and fill in your values.
# ============================================================================

proxmox_url           = "https://10.0.0.2:8006/api2/json"
proxmox_token_id      = "tofu@pve!automation"
proxmox_token_secret  = "adff0449-1cd1-480b-b99f-ec4320cf95fe"
proxmox_node          = "pia-dev"

# VM template settings
vm_id       = 9010
vm_name     = "ubuntu-docker-template"
iso_file    = "local:iso/ubuntu-24.04.4-live-server-amd64.iso"
vm_storage  = "local-lvm"
disk_size   = "60G"
cores       = 4
memory      = 16384



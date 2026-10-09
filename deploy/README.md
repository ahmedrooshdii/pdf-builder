# Deploying to Oracle Cloud Always Free

CI builds an arm64 image, pushes it to GHCR, then SSHes into a VM and restarts the container
there. The steps below are the one-time setup that has to exist before the first deploy.

Why a VM rather than a managed container platform: this service consumes a queue, so it has to
stay running, and Chromium needs real CPU and memory. The free tiers that scale to zero suspend
the consumer, and the ones that stay up cap CPU far below what rendering needs.

## 1. Create the VM

In the Oracle Cloud console: **Compute → Instances → Create**.

| Setting | Value |
| --- | --- |
| Shape | **VM.Standard.A1.Flex** (Ampere, Arm) |
| OCPUs / memory | 2 OCPU / 12 GB — the current Always Free allowance |
| Image | Canonical Ubuntu 24.04 |
| Boot volume | 50 GB (Always Free covers 200 GB total) |
| SSH key | Upload a public key you hold the private half of |

> Oracle reduced the Always Free Ampere allowance from 4 OCPU / 24 GB to **2 OCPU / 12 GB** in
> June 2026, without announcing it. Ask for more than that and the instance will not start.

Capacity in a given region is often exhausted — "Out of host capacity" is normal. Retry, or pick
a different availability domain.

## 2. Open the ports

Two layers, and forgetting the second is the classic Oracle mistake.

**Virtual Cloud Network → Security List → Ingress:** allow TCP 80 and 443 from `0.0.0.0/0`.

**On the VM** — Ubuntu images ship iptables rules that drop everything else:

```bash
sudo iptables -I INPUT 6 -m state --state NEW -p tcp --dport 80 -j ACCEPT
sudo iptables -I INPUT 6 -m state --state NEW -p tcp --dport 443 -j ACCEPT
sudo netfilter-persistent save
```

Port 8080 stays closed. The container binds it to localhost and the proxy reaches it from there.

## 3. Install Docker

```bash
curl -fsSL https://get.docker.com | sudo sh
sudo usermod -aG docker ubuntu     # log out and back in for this to apply
sudo apt install -y rsync          # the deploy syncs templates with it
```

## 4. Create the application directory

```bash
sudo mkdir -p /opt/pdf-builder/templates
sudo chown -R ubuntu:ubuntu /opt/pdf-builder
```

Write `/opt/pdf-builder/.env` — this file holds the only secrets on the box, and nothing in it
is ever committed:

```bash
cat > /opt/pdf-builder/.env <<'EOF'
RABBITMQ_CONNECTION_STRING=amqps://user:pass@host.rmq.cloudamqp.com/vhost
PUBLIC_BASE_URL=https://pdf.example.com
EOF
chmod 600 /opt/pdf-builder/.env
```

`PUBLIC_BASE_URL` must be the address **callers** reach, not the VM's. It is what every `fileUrl`
is built from, so getting it wrong hands out dead links and nothing fails until someone clicks one.

## 5. Make the image pullable

The repository is public, so make the GHCR package public too and the VM needs no credentials:

**GitHub → your profile → Packages → pdf-builder → Package settings → Change visibility → Public.**

The image holds nothing private; real configuration arrives from `.env` at run time.

If you would rather keep it private, log in on the VM once with a PAT that has `read:packages`:

```bash
echo "$GITHUB_PAT" | docker login ghcr.io -u ahmedrooshdii --password-stdin
```

## 6. Put a reverse proxy in front

The container speaks plain HTTP on localhost. Caddy is the least work, because it obtains and
renews the certificate by itself:

```bash
sudo apt install -y caddy
printf 'pdf.example.com {\n    reverse_proxy 127.0.0.1:8080\n}\n' | sudo tee /etc/caddy/Caddyfile
sudo systemctl restart caddy
```

Point the domain's A record at the VM's public IP first, or the certificate request fails.

> Without a proxy you would be serving the API over plain HTTP, and `/files/{fileId}` URLs are
> bearer tokens — anyone who can read the traffic can fetch the document.

## 7. Add the repository secrets

**Settings → Secrets and variables → Actions**, and because the workflow uses a `production`
environment, add them there (**Settings → Environments → production**) if you want a manual
approval gate before each deploy.

| Secret | Value |
| --- | --- |
| `DEPLOY_HOST` | The VM's public IP |
| `DEPLOY_USER` | `ubuntu` |
| `DEPLOY_SSH_KEY` | The **private** key, whole file including the BEGIN/END lines |
| `DEPLOY_KNOWN_HOSTS` | Output of `ssh-keyscan <vm-ip>` |

`DEPLOY_KNOWN_HOSTS` is what makes the deploy refuse to talk to a host that is not yours. The
usual shortcut, `StrictHostKeyChecking=no`, hands the session and the key to whoever answers.

Use a deploy-only key rather than your personal one:

```bash
ssh-keygen -t ed25519 -f ./pdf-builder-deploy -C "github-actions"
ssh-copy-id -i ./pdf-builder-deploy.pub ubuntu@<vm-ip>
ssh-keyscan <vm-ip>          # paste into DEPLOY_KNOWN_HOSTS
cat ./pdf-builder-deploy     # paste into DEPLOY_SSH_KEY, then delete the local copy
```

## 8. Deploy

Push to `master`. The pipeline runs tests, builds the arm64 image, pushes it to GHCR, copies the
compose file and templates to the VM, restarts the container on the exact digest it just built,
and then polls `/health/ready` until it answers — so a deploy that comes back broken fails the
run instead of going green.

Check it by hand:

```bash
curl https://pdf.example.com/health/ready
```

## Running it manually

```bash
cd /opt/pdf-builder
docker compose up -d --pull always
docker compose logs -f api
```

## What this setup does not give you

**One instance only.** PDFs live on a Docker volume on this VM and job state lives in the
process's memory, so a second replica would serve 404s for files and jobs it never saw. Both are
two-method interfaces — see [operations.md](../docs/operations.md#scaling-past-one-instance).

**No backups.** The `pdf-data` volume is on the boot disk. Oracle's Always Free tier includes
block volume backups; turn them on if the documents matter after the VM does not.

**Brief downtime on deploy.** `compose up -d` stops the old container before the new one is
ready. With one instance and in-memory job state there is nothing to fail over to, so a few
seconds of 502 is the honest cost. In-flight renders are not cut off — the container is given 60
seconds to drain, matching the host's shutdown timeout.

**Stored PDFs are never deleted.** Watch the volume, or add a cron job over `/var/lib/docker/
volumes/pdf-builder_pdf-data` by file age until retention exists.

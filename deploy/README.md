# Deploying to Oracle Cloud Always Free

CI builds an arm64 image, pushes it to GHCR, then SSHes into a VM and restarts the container
there. The steps below are the one-time setup that has to exist before the first deploy.

Why a VM rather than a managed container platform: this service consumes a queue, so it has to
stay running, and Chromium needs real CPU and memory. The free tiers that scale to zero suspend
the consumer, and the ones that stay up cap CPU far below what rendering needs.

## 1. Create the account

Sign up at [oracle.com/cloud/free](https://www.oracle.com/cloud/free/). A card is required for
identity verification; Always Free resources are not billed.

> **Your home region cannot be changed afterwards.** It is the single most consequential choice in
> signup, because Ampere capacity is what this whole setup depends on and it is unevenly
> distributed. Frankfurt (`eu-frankfurt-1`) and Singapore (`ap-singapore-1`) have historically had
> capacity when the US regions had none. Pick a region near your users that is not one of the
> busiest.

## 2. Create the VM

**Compute → Instances → Create instance.**

| Setting | Value |
| --- | --- |
| Image | Canonical Ubuntu 24.04 |
| Shape | **VM.Standard.A1.Flex** — under the *Ampere* tab, not *AMD* |
| OCPUs | 2 |
| Memory | 12 GB |
| Boot volume | 50 GB (Always Free allows 200 GB in total) |
| SSH key | Upload the public half of a key you hold |

Both the shape and the numbers matter. `VM.Standard.E2.1.Micro` is the AMD Always Free shape — it
is 1 GB of RAM, which Chromium will not survive. And the Ampere allowance was **halved from 4
OCPU / 24 GB to 2 OCPU / 12 GB in June 2026** with no announcement, so asking for the older figure
now fails.

### When it says "Out of host capacity"

This is the normal experience, not a mistake you made. Ampere capacity in the free tier is
heavily oversubscribed. Nothing is created on a failed attempt, so retrying costs nothing.

In rough order of effort:

1. **Press Create again**, and try each availability domain and fault domain in turn.
2. **Try at a quieter hour.** Capacity is released continuously as others tear instances down.
3. **Upgrade to Pay As You Go.** This is the reliable fix: free accounts are served last, and
   paid accounts usually get Ampere capacity immediately. Always Free resources stay free after
   the upgrade — Oracle bills only for what exceeds the limits — so provided you create nothing
   beyond 2 OCPU / 12 GB and 200 GB of storage, the bill stays at zero. Be aware of two things:
   the upgrade places a temporary authorisation hold of around $100 on the card, and after it
   nothing stops you from accidentally creating something billable.

Scripts that retry the API on a loop until capacity appears are widely used for this, and are the
pragmatic answer if you would rather not upgrade.

## 3. Open the ports

Two layers, and forgetting the second is the classic Oracle mistake.

**Virtual Cloud Network → Security List → Ingress:** allow TCP 80 and 443 from `0.0.0.0/0`.

**On the VM** — Ubuntu images ship iptables rules that drop everything else:

```bash
sudo iptables -I INPUT 6 -m state --state NEW -p tcp --dport 80 -j ACCEPT
sudo iptables -I INPUT 6 -m state --state NEW -p tcp --dport 443 -j ACCEPT
sudo netfilter-persistent save
```

Port 8080 stays closed. The container binds it to localhost and the proxy reaches it from there.

## 4. Install Docker

```bash
curl -fsSL https://get.docker.com | sudo sh
sudo usermod -aG docker ubuntu     # log out and back in for this to apply
sudo apt install -y rsync          # the deploy syncs templates with it
```

## 5. Create the application directory

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

## 6. Make the image pullable

The repository is public, so make the GHCR package public too and the VM needs no credentials:

**GitHub → your profile → Packages → pdf-builder → Package settings → Change visibility → Public.**

The image holds nothing private; real configuration arrives from `.env` at run time.

If you would rather keep it private, log in on the VM once with a PAT that has `read:packages`:

```bash
echo "$GITHUB_PAT" | docker login ghcr.io -u ahmedrooshdii --password-stdin
```

## 7. Put a reverse proxy in front

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

## 8. Add the repository secrets

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

## 9. Deploy

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

# Docker Host Setup

This page covers preparing your Linux server and deploying the Praxova
Admin Portal and IT Agent containers. By the end of this page, the portal
will be running and accessible in your browser.

**Time required:** 30–60 minutes (plus model download time if using a local LLM)

**Before you start:** Confirm all items in the
[Prerequisites checklist](../getting-started/prerequisites.md) are complete.

---

## Step 1 — Get the Praxova Files

Clone the Praxova repository onto your Docker host:

```bash
git clone https://github.com/praxova/praxova-it-agent.git
cd praxova-it-agent
```

---

## Step 2 — Create Your Environment File

Praxova uses an environment file to receive its startup configuration.
Create yours from the provided example:

```bash
cp .env.example .env
```

Open `.env` in a text editor and set the following values:

```bash
# The unseal passphrase protects all credentials stored in Praxova.
# Choose something strong — 32+ characters — and store it in your password manager.
# You will need this every time the portal restarts.
PRAXOVA_UNSEAL_PASSPHRASE=your-strong-passphrase-here

# Leave this blank for now — you will fill it in during portal configuration.
LUCID_API_KEY=
```

> **Keep your `.env` file private.** It is already excluded from version
> control by `.gitignore`. Never commit it, email it, or share it. If you
> are deploying to a production server, see
> [Securing the Unseal Passphrase](../security/secrets-management.md)
> for the recommended approach to storing it outside the project directory.

---

## Step 3 — Provision the Local LLM Model (Local LLM Only)

> **Skip this step if you are using a cloud LLM provider** (OpenAI, Anthropic,
> Azure OpenAI). You will configure your API key during portal setup instead.

Praxova's local LLM option uses **llama.cpp server** — a lean, high-performance
inference engine with native TLS support. Unlike some alternatives, it runs
entirely within your network and requires no external API calls.

> **Why llama.cpp and not Ollama?** Ollama does not support TLS on its API
> endpoint. Since Praxova requires all inter-service communication to be
> encrypted, llama.cpp server is used instead — it supports HTTPS natively
> and its TLS certificate is issued by the same internal CA as every other
> Praxova component.

The LLM server expects a model file in GGUF format. You need to place this
in the Docker volume before starting the stack.

```bash
# Create the volume if it does not exist yet
docker volume create praxova-llm-models

# Download the model directly into the volume (~4.7 GB — allow 5–15 minutes)
docker run --rm \
  -v praxova-llm-models:/models \
  alpine/curl -L \
  -o /models/model.gguf \
  "https://huggingface.co/bartowski/Meta-Llama-3.1-8B-Instruct-GGUF/resolve/main/Meta-Llama-3.1-8B-Instruct-Q4_K_M.gguf"
```

Verify the model file is in place before continuing:

```bash
docker run --rm -v praxova-llm-models:/models alpine ls -lh /models/
# Expected: model.gguf, approximately 4.7 GB
```

> **GPU requirement:** The llama.cpp server container uses your NVIDIA GPU
> for inference. A GPU with at least 8 GB VRAM is required. See
> [Prerequisites](../getting-started/prerequisites.md) for details.

---

## Step 4 — Start the Stack

```bash
docker compose up -d
```

This starts the Admin Portal, IT Agent, and (if using a local LLM) the LLM
server container. On first start, Docker builds the images before launching.
The llama.cpp server image includes a full CUDA build and takes 10–15 minutes
to compile. Subsequent starts use the Docker layer cache and are much faster.

Watch the portal initialize:

```bash
docker compose logs -f admin-portal
```

On a successful first start, you will see messages similar to these:

```
Secrets store initialized and unsealed via PRAXOVA_UNSEAL_PASSPHRASE
Generated new JWT signing key and stored encrypted in database
Internal PKI initialized — Praxova root CA generated
Admin portal TLS certificate issued from internal CA
Default admin user created — password change required on first login
Database migration complete
Application started. Listening on: http://+:5000, https://+:5001
```

Press `Ctrl+C` to stop following logs — the containers keep running.

If using a local LLM, also confirm the LLM server started successfully:

```bash
docker compose logs llm
# Look for: llama server listening at https://0.0.0.0:8443

# Health check
curl -sk https://localhost:8443/health
# Expected: { "status": "ok" }
```

---

## Step 5 — Verify the Portal Is Running

```bash
curl -s http://localhost:5000/api/health/ | jq
```

Expected response:

```json
{
  "status": "Healthy",
  "sealed": false
}
```

**If `sealed` is `true`:** The portal started but could not read the unseal
passphrase. Check that `PRAXOVA_UNSEAL_PASSPHRASE` is set correctly in `.env`,
then restart:

```bash
docker compose restart admin-portal
docker compose logs -f admin-portal
```

**If the command returns nothing or an error:** The portal may still be
starting. Wait 30 seconds and try again. If it continues to fail, check
the full logs:

```bash
docker compose logs admin-portal
```

---

## Step 6 — Open the Admin Portal

Open your browser and navigate to:

```
https://<your-docker-host-ip>:5001
```

You will see a TLS certificate warning. This is expected — the portal uses
its own internally-generated certificate authority, which your browser does
not recognize yet. You can safely proceed past the warning for now.

> **Optional — Remove the browser warning:** You can import the Praxova CA
> certificate into your browser or OS trust store. To download it:
>
> ```bash
> curl -s http://<your-docker-host-ip>:5000/api/pki/trust-bundle -o praxova-ca.pem
> ```
>
> Then import `praxova-ca.pem`:
> - **Windows:** Double-click → Install Certificate → Local Machine →
>   Trusted Root Certification Authorities
> - **macOS:** Double-click → Keychain Access → set to Always Trust
> - **Firefox:** Settings → Privacy & Security → Certificates → Import

---

## Step 7 — Initial Portal Configuration

### 7.1 Change the Default Admin Password

Log in with username `admin` and password `admin`. The portal will immediately
redirect you to a mandatory password change screen. You cannot proceed until
this is done.

> **This account is your break-glass recovery account.** If Active Directory
> authentication is unavailable, this local account is how you get back in.
> Store the password in your password manager alongside the unseal passphrase.

---

### 7.2 Connect Your LLM Provider

Navigate to **Service Accounts → New**.

**Local LLM (llama.cpp server):**

| Field | Value |
|-------|-------|
| Provider type | `llm-ollama` |
| Name | `Local LLM` |
| Endpoint | `https://llm:8443` |
| Model | `local` |

> The provider type shows `llm-ollama` in the UI — this is the internal
> identifier for the local LLM option and is correct. The underlying engine
> is llama.cpp server, which uses an OpenAI-compatible API.

**OpenAI:**

| Field | Value |
|-------|-------|
| Provider type | `llm-openai` |
| Name | `OpenAI` |
| Model | `gpt-4o` |
| API Key | your OpenAI API key |

**Anthropic:**

| Field | Value |
|-------|-------|
| Provider type | `llm-anthropic` |
| Name | `Anthropic` |
| Model | `claude-sonnet-4-20250514` |
| API Key | your Anthropic API key |

---

### 7.3 Connect ServiceNow

Navigate to **Service Accounts → New**.

| Field | Value |
|-------|-------|
| Provider type | `servicenow-basic` |
| Name | `ServiceNow` |
| Instance URL | `https://yourinstance.service-now.com` |
| Username | your ServiceNow API account username |
| Password | your ServiceNow API account password |

---

### 7.4 Connect Active Directory

Navigate to **Service Accounts → New**.

| Field | Value |
|-------|-------|
| Provider type | `windows-ad` |
| Name | `Active Directory` |
| Domain | `yourdomain.com` |
| Domain Controller | `dc01.yourdomain.com` |
| Port | `636` |
| Use SSL | `true` |
| Username | `svc-praxova` |
| Password | the password you set when creating the service account |

---

### 7.5 Create the Agent

Navigate to **Agents → New**.

| Field | Value |
|-------|-------|
| Name | `helpdesk-agent` |
| Display name | `Helpdesk Agent` |
| LLM provider | select the account from step 7.2 |
| ServiceNow connection | select the account from step 7.3 |
| Assignment group | exact name of your ServiceNow assignment group |

---

### 7.6 Create an API Key

Navigate to **API Keys → New**.

| Field | Value |
|-------|-------|
| Name | `helpdesk-agent` |
| Role | `Agent` |

The portal shows the key once. Copy it immediately and add it to your `.env` file:

```bash
LUCID_API_KEY=prx_xxxxxxxxxxxxxxxxxxxxxxxx
```

Then restart the agent to pick up the key:

```bash
docker compose restart agent-helpdesk-01
```

---

## Next Steps

The portal and agent are running. Now deploy the Tool Server on your
domain-joined Windows server so the agent can execute Active Directory operations.

→ [Tool Server Installation](tool-server.md)

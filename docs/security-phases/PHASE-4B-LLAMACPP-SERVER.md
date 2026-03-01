# Phase 4b — Replace Ollama with llama.cpp Server

## Context for the Intermediary Chat

This prompt was produced by a security architecture session. It describes WHAT needs
to exist and WHY. Your job is to compare against the current codebase and produce a
Claude Code implementation prompt.

**Background decision:** The original plan was to add an Nginx TLS-terminating
sidecar in front of Ollama (TD-008). After analysis, we decided to replace Ollama
entirely with llama.cpp's built-in HTTP server (`llama-server`), which supports
native TLS. This eliminates the sidecar, reduces moving parts, and gives us direct
GPU control.

Ollama is llama.cpp with a model management layer. Praxova doesn't need model
management — it deploys a known model and runs it. llama.cpp server is the engine
without the dashboard.

**This is a stack change, not a security feature.** But it enables end-to-end TLS
for all inter-service communication, which is a security requirement.

**Dependency:** None strictly, but this should be done in the same release as the
other Phase 4 items. The agent's `create_prompt_driver()` factory is the main
integration point.

---

## Specification

### 1. Dockerfile for llama.cpp Server

Create a Dockerfile that builds llama.cpp with CUDA support. Pin to a release tag,
not `master`.

```dockerfile
# Build stage
FROM nvidia/cuda:12.4.0-devel-ubuntu22.04 AS builder

ARG LLAMA_CPP_VERSION=b4677
# Pin to a specific release tag. Check https://github.com/ggml-org/llama.cpp/releases
# for the latest stable tag at implementation time.

RUN apt-get update && apt-get install -y \
    git cmake build-essential \
    && rm -rf /var/lib/apt/lists/*

RUN git clone --branch ${LLAMA_CPP_VERSION} --depth 1 \
    https://github.com/ggml-org/llama.cpp.git /build/llama.cpp

WORKDIR /build/llama.cpp
RUN cmake -B build \
    -DGGML_CUDA=ON \
    -DLLAMA_CURL=OFF \
    -DCMAKE_BUILD_TYPE=Release \
    && cmake --build build --target llama-server -j$(nproc)

# Runtime stage
FROM nvidia/cuda:12.4.0-runtime-ubuntu22.04

RUN apt-get update && apt-get install -y \
    libgomp1 ca-certificates \
    && rm -rf /var/lib/apt/lists/*

COPY --from=builder /build/llama.cpp/build/bin/llama-server /usr/local/bin/

# Model mount point
VOLUME /models

# TLS cert mount point
VOLUME /certs

EXPOSE 8443

ENTRYPOINT ["llama-server"]
CMD [ \
    "--host", "0.0.0.0", \
    "--port", "8443", \
    "--model", "/models/model.gguf", \
    "--ssl-cert-file", "/certs/llama-server.crt", \
    "--ssl-key-file", "/certs/llama-server.key", \
    "--n-gpu-layers", "-1", \
    "--ctx-size", "8192", \
    "--parallel", "2" \
]
```

**Notes for the intermediary chat:**
- Check the current CUDA version on the Proxmox host (the dual Titan XP setup).
  The CUDA base image version must match or be compatible with the host's driver.
  Titan XP supports up to CUDA 12.x with recent drivers.
- The `LLAMA_CPP_VERSION` should be the latest stable release tag at implementation
  time. Check https://github.com/ggml-org/llama.cpp/releases.
- `--n-gpu-layers -1` means "offload all layers to GPU." With dual Titan XPs
  (12GB each = 24GB total), most 7B-13B models fit entirely in VRAM.
- `--parallel 2` allows 2 concurrent inference requests. Praxova typically has one
  agent, so 2 gives headroom for overlapping requests without overcommitting VRAM.
- `--ctx-size 8192` is reasonable for ticket classification. Adjust based on the
  model's native context length and the prompt size.
- The CMD args are defaults. docker-compose.yml can override them.

### 2. docker-compose.yml Changes

Replace the `ollama` service:

```yaml
# REMOVE this:
#  ollama:
#    image: ollama/ollama:latest
#    container_name: praxova-ollama
#    ports:
#      - "11434:11434"
#    volumes:
#      - praxova-ollama-models:/root/.ollama
#    deploy:
#      resources:
#        reservations:
#          devices:
#            - driver: nvidia
#              count: all
#              capabilities: [gpu]

# ADD this:
  llm:
    build:
      context: ./docker/llama-server
      dockerfile: Dockerfile
    container_name: praxova-llm
    volumes:
      - praxova-llm-models:/models
      - praxova-llm-certs:/certs:ro
    deploy:
      resources:
        reservations:
          devices:
            - driver: nvidia
              count: all
              capabilities: [gpu]
    environment:
      - CUDA_VISIBLE_DEVICES=0,1  # Both Titan XPs
    # Override CMD for environment-specific tuning:
    # command: >
    #   --host 0.0.0.0
    #   --port 8443
    #   --model /models/model.gguf
    #   --ssl-cert-file /certs/llama-server.crt
    #   --ssl-key-file /certs/llama-server.key
    #   --n-gpu-layers -1
    #   --ctx-size 8192
    #   --parallel 2
    #   --tensor-split 0.5,0.5
    healthcheck:
      test: ["CMD", "curl", "-fsk", "https://localhost:8443/health"]
      interval: 30s
      timeout: 10s
      retries: 3
    networks:
      - praxova-internal
```

**Notes:**
- Container name changes from `praxova-ollama` to `praxova-llm`. This is
  intentionally generic — it's a local LLM server, not tied to any specific
  engine. If we ever swap engines again, the container name doesn't change.
- The volume for models changes from `praxova-ollama-models` to
  `praxova-llm-models`. The old Ollama volume stored models in Ollama's
  internal format; the new volume stores raw GGUF files.
- A separate certs volume for the TLS cert and key. These should be generated
  by the portal's PKI system (same as other component certs).
- `--tensor-split 0.5,0.5` distributes model layers evenly across both GPUs.
  Tune this based on actual VRAM usage.
- The healthcheck uses the llama.cpp server's `/health` endpoint over HTTPS.

### 3. TLS Certificate for LLM Server

The portal's internal PKI should issue a certificate for the LLM server, just
like it does for the admin portal and (eventually) the agent client cert.

**SAN requirements:**
- `DNS:llm` (container name, used for inter-container communication)
- `DNS:praxova-llm` (container name alias)
- `DNS:localhost` (for health checks from within the container)

The cert and key should be written to the `praxova-llm-certs` volume. The
intermediary chat should examine how the portal currently generates and deploys
component certificates — specifically how the admin portal's own cert is
generated at startup — and follow the same pattern for the LLM server cert.

If certs are generated by an init container or startup script, add the LLM
server cert to that process. If they're generated manually, document the
command. Either way, the cert needs to exist before the LLM container starts.

### 4. Model File Provisioning

With Ollama, you ran `ollama pull llama3.1` inside the container. With llama.cpp
server, you need the raw GGUF file in the models volume.

**One-time setup procedure:**

```bash
# Download the model (example: Llama 3.1 8B Q4_K_M quantization)
# This downloads directly into the Docker volume
docker run --rm -v praxova-llm-models:/models alpine/curl \
  -L -o /models/model.gguf \
  "https://huggingface.co/bartowski/Meta-Llama-3.1-8B-Instruct-GGUF/resolve/main/Meta-Llama-3.1-8B-Instruct-Q4_K_M.gguf"

# Or copy a local file into the volume
docker cp /path/to/model.gguf praxova-llm:/models/model.gguf
```

The model file path inside the container (`/models/model.gguf`) matches the
`--model` flag in the Dockerfile CMD.

**For automated deployments:** Add a model provisioning script that checks
whether the model exists in the volume and downloads it if not. This would
run as part of the first-time setup sequence.

### 5. Agent Driver Configuration Change

The agent currently uses `OllamaPromptDriver` from Griptape. It needs to switch
to `OpenAiChatPromptDriver` pointed at the llama.cpp server's OpenAI-compatible
endpoint.

The intermediary chat MUST examine:

1. **`create_prompt_driver()` factory** in `agent/src/agent/drivers/` — this is
   where the LLM provider is selected based on the ServiceAccount's provider type.
   The Ollama case currently returns an `OllamaPromptDriver`. It should be changed
   to return an `OpenAiChatPromptDriver` configured for the local llama.cpp server.

2. **Provider type handling** — There are two approaches:
   
   **Option A: Reuse the `llm-ollama` provider type.** Change the driver factory
   to return `OpenAiChatPromptDriver` when it sees `llm-ollama`. No database
   schema changes needed. The provider type name becomes slightly misleading
   (it says "ollama" but uses OpenAI-compatible API), but the ServiceAccount
   UI can display a friendlier label.
   
   **Option B: Add a new `llm-local` provider type.** More semantically correct
   but requires a database migration to add the enum value, updating the portal
   UI to show the new type, and handling existing `llm-ollama` records.
   
   I recommend **Option A** for v1.0 to minimize disruption. Rename in v1.1 if
   it bothers anyone.

3. **Connection details in ServiceAccount** — The `llm-ollama` ServiceAccount's
   `provider_config` JSON currently stores the Ollama server URL (e.g.,
   `http://ollama:11434`). This changes to `https://llm:8443`. The field names
   may need updating — check what keys the Ollama config uses and ensure the
   driver factory reads the correct ones.

4. **Model specification** — With Ollama, the model name was part of the API
   request (e.g., `model: "llama3.1"`). With llama.cpp server, the model is
   loaded at server startup and ALL requests use that model. The `model` field
   in the API request can be anything (llama.cpp server ignores it) or omitted.
   Check whether Griptape's `OpenAiChatPromptDriver` requires a model string —
   if so, set it to a descriptive value like `"local"` or the actual model name
   for logging purposes.

5. **TLS trust** — The agent needs to trust the LLM server's TLS certificate.
   Since the cert is issued by the Praxova internal CA, and the agent already
   trusts this CA (from the bootstrap fetch), the HTTPS connection should work
   without additional configuration. Verify that the agent's HTTP client (used
   by Griptape's driver) respects the `SSL_CERT_FILE` environment variable or
   whatever CA trust mechanism the agent uses.

### 6. Verify the API Compatibility

llama.cpp server's OpenAI-compatible endpoint:

```
POST https://llm:8443/v1/chat/completions
{
  "model": "local",
  "messages": [
    {"role": "system", "content": "You are a ticket classifier..."},
    {"role": "user", "content": "Ticket: I can't login, please reset my password"}
  ],
  "temperature": 0.1
}
```

Response format matches OpenAI's:

```json
{
  "id": "chatcmpl-...",
  "object": "chat.completion",
  "created": 1740000000,
  "model": "local",
  "choices": [
    {
      "index": 0,
      "message": {
        "role": "assistant",
        "content": "{\"ticket_type\": \"password_reset\", \"confidence\": 0.95, ...}"
      },
      "finish_reason": "stop"
    }
  ],
  "usage": {
    "prompt_tokens": 150,
    "completion_tokens": 50,
    "total_tokens": 200
  }
}
```

Griptape's `OpenAiChatPromptDriver` speaks this format natively. The key
configuration parameters:

```python
from griptape.drivers.prompt.openai import OpenAiChatPromptDriver

driver = OpenAiChatPromptDriver(
    model="local",
    api_key="not-needed",  # llama.cpp doesn't require an API key
    base_url="https://llm:8443/v1",
    temperature=0.1,
    max_tokens=1024,
)
```

The intermediary chat should check whether `OpenAiChatPromptDriver` allows
`api_key=""` or `api_key="not-needed"`. Some OpenAI client libraries require
a non-empty API key string even if the server doesn't validate it. If
Griptape requires it, use a dummy value like `"local-no-auth"`.

### 7. Build Script Updates

The `scripts/build-containers.sh` script currently builds the admin portal,
agent, and optionally the Ollama container. Update it:

- Remove the Ollama tarball build (it was just `docker save ollama/ollama:latest`)
- Add the llama.cpp server build: `docker build -t praxova-llm:latest docker/llama-server/`
- Update tarball export if applicable

### 8. Documentation Updates

**DEV-QUICKREF.md:**
- Update the container table: replace `praxova-ollama` with `praxova-llm`
- Update port: 11434 → 8443 (HTTPS)
- Update the "Ollama" section with new model provisioning commands
- Update health check commands: `curl -sk https://localhost:8443/health`
- Remove `ollama pull` / `ollama list` commands
- Add model download procedure

**ARCHITECTURE.md deployment topology:**
- Update the container diagram to show `llm` instead of `ollama`
- Note that the LLM server uses HTTPS with a Praxova CA-issued cert
- Update the "accessible only within Docker network" note — it's now
  "HTTPS within Docker network"

---

## File Structure

```
docker/
└── llama-server/
    └── Dockerfile
```

The Dockerfile is the only new file. Everything else is modifications to existing
files (docker-compose.yml, agent driver factory, build scripts, documentation).

---

## Rollback Plan

If llama.cpp server causes issues (build failures, GPU compatibility, inference
quality differences), the rollback is:

1. Revert docker-compose.yml to the Ollama service definition
2. Revert the agent driver factory to use `OllamaPromptDriver`
3. Run `ollama pull llama3.1` to restore the model

The Ollama volume (`praxova-ollama-models`) is not deleted during this migration,
so the models are still there. The rollback is clean.

However, rolling back means losing TLS on the LLM connection. If the rollback
is needed, re-plan the Nginx sidecar approach (original TD-008) as a fallback
for getting TLS in front of Ollama.

---

## Git Commit Guidance

```
feat(infra): add llama.cpp server Dockerfile with CUDA and native TLS
feat(infra): replace ollama with llama.cpp server in docker-compose
feat(agent): switch LLM driver from OllamaPromptDriver to OpenAiChatPromptDriver
feat(infra): add LLM server TLS cert generation to PKI bootstrap
docs: update DEV-QUICKREF for llama.cpp server
docs: update ARCHITECTURE for llama.cpp server
chore: update build scripts for llama.cpp container
```

### What NOT to Change

- Do not remove the `llm-ollama` provider type from the ServiceAccount enum —
  reuse it with a different driver behind the scenes
- Do not modify the classification prompts or few-shot examples
- Do not modify the LLM response parsing in the classifier
- Do not modify any other ServiceAccount provider types (OpenAI, Anthropic, etc.)
- Do not add authentication to the llama.cpp server (it's internal to the Docker
  network; TLS provides encryption, network isolation provides access control)

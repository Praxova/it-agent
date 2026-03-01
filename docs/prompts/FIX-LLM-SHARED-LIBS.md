# Claude Code Prompt: Fix llama-server shared library missing in Docker image

## Problem

The LLM container crashes at startup:
```
llama-server: error while loading shared libraries: libllama.so: cannot open shared object file: No such file or directory
```

The Dockerfile at `docker/llama-server/Dockerfile` copies only the `llama-server` binary from the
build stage but not the shared libraries it links against. Recent llama.cpp versions (b4677+) build
`llama-server` dynamically linked against `libllama.so` and `libggml*.so`.

## Fix

In `docker/llama-server/Dockerfile`, add the shared library copies after the binary copy and set
`LD_LIBRARY_PATH` in the runtime stage.

Replace this line:
```dockerfile
COPY --from=builder /build/llama.cpp/build/bin/llama-server /usr/local/bin/
```

With:
```dockerfile
COPY --from=builder /build/llama.cpp/build/bin/llama-server /usr/local/bin/
COPY --from=builder /build/llama.cpp/build/src/libllama.so /usr/local/lib/
COPY --from=builder /build/llama.cpp/build/ggml/src/libggml*.so /usr/local/lib/
RUN ldconfig
```

The `ldconfig` call updates the shared library cache so the linker can find them at `/usr/local/lib/`
without needing `LD_LIBRARY_PATH`.

**Important:** The exact paths for the .so files may vary by llama.cpp version. If the paths above
don't exist in the build stage, find them with:
```dockerfile
RUN find /build/llama.cpp/build -name "*.so" -type f
```
Add that as a temporary debug line in the builder stage, build, check the output, then update the
COPY paths accordingly and remove the debug line.

## Testing

```bash
cd /home/alton/Documents/lucid-it-agent
docker compose build llm  # or: scripts/build-containers.sh --skip-ollama
docker compose up -d llm
docker compose logs --tail 20 llm
# Should see: "Starting llama-server..." followed by llama.cpp startup output
# Should NOT see: "cannot open shared object file"
```

## Commit

```
fix(llm): copy shared libraries into runtime Docker image

llama-server links dynamically against libllama.so and libggml*.so.
The Dockerfile only copied the binary, causing "cannot open shared
object file" at runtime. Copy the shared libraries and run ldconfig.
```

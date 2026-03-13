# Build stage
FROM nvidia/cuda:12.4.0-devel-ubuntu22.04 AS builder

ARG LLAMA_CPP_VERSION=b4677

RUN apt-get update && apt-get install -y \
    git cmake build-essential cuda-compat-12-4 libssl-dev \
    && rm -rf /var/lib/apt/lists/*

# cuda-compat provides libcuda.so.1 stub needed at link time.
# The real driver is mounted by nvidia-container-toolkit at runtime.
ENV LD_LIBRARY_PATH="/usr/local/cuda/compat:${LD_LIBRARY_PATH}"

RUN git clone --branch ${LLAMA_CPP_VERSION} --depth 1 \
    https://github.com/ggml-org/llama.cpp.git /build/llama.cpp

WORKDIR /build/llama.cpp
RUN cmake -B build \
    -DGGML_CUDA=ON \
    -DLLAMA_CURL=OFF \
    -DLLAMA_SERVER_SSL=ON \
    -DGGML_NATIVE=OFF \
    -DCMAKE_BUILD_TYPE=Release \
    && cmake --build build --target llama-server -j$(nproc)

# Runtime stage
FROM nvidia/cuda:12.4.0-runtime-ubuntu22.04

RUN apt-get update && apt-get install -y \
    libgomp1 ca-certificates curl libssl3 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=builder /build/llama.cpp/build/bin/llama-server /usr/local/bin/

COPY --from=builder /build/llama.cpp/build/bin/lib*.so /usr/local/lib/

RUN ldconfig

COPY docker-entrypoint.sh /usr/local/bin/docker-entrypoint.sh
RUN chmod +x /usr/local/bin/docker-entrypoint.sh

VOLUME /models
VOLUME /certs

EXPOSE 8443

ENTRYPOINT ["/usr/local/bin/docker-entrypoint.sh"]

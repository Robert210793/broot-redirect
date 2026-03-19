/** @type {import('http-proxy-middleware').Options} */
const proxyConfig = {
  "/api": {
    target: "https://localhost:7233",
    secure: false,
    changeOrigin: true,
    onProxyRes: function (proxyRes, req, res) {
      // Disable buffering for SSE streams so chunks arrive in real-time
      const contentType = proxyRes.headers["content-type"] || "";
      if (contentType.includes("text/event-stream")) {
        // Remove transfer-encoding chunked header that can cause buffering
        // and ensure the proxy flushes immediately
        proxyRes.headers["x-accel-buffering"] = "no";
      }
    }
  }
};

module.exports = proxyConfig;
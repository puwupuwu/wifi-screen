import http from "node:http";

// AirPlay's status bit 7 means PasswordRequired; bit 3 means PINRequired.
// A password-protected receiver need not display a new code on the TV.
export function airPlayPasswordRequired(properties = {}) {
  if (properties.passwordRequired === true || String(properties.pw).toLowerCase() === "true") return true;
  const raw = properties.sf || properties.flags || "0x0";
  try {
    const flags = typeof raw === "number" || typeof raw === "bigint"
      ? BigInt(raw)
      : BigInt(/^0x/i.test(String(raw)) ? String(raw) : `0x${raw}`);
    return (flags & 0x80n) !== 0n;
  } catch {
    return false;
  }
}

export function airPlayPairingInput(device, input) {
  if (airPlayPasswordRequired(device)) {
    const value = String(input ?? "");
    if (!value.length || value.length > 1024) throw new Error("Enter the AirPlay password configured on the TV.");
    return value;
  }
  const value = String(input ?? "").replace(/\D/g, "");
  if (value.length < 4 || value.length > 8) throw new Error("Enter the code shown on the AirPlay device.");
  return value;
}

export function createAirPlayPairingSrp(SrpClient, params, salt, device, input, privateKey) {
  return new SrpClient(params, salt, Buffer.from("Pair-Setup"),
    Buffer.from(airPlayPairingInput(device, input), "utf8"), privateKey);
}

// Keep the PIN request on the pairing agent's socket whenever the receiver
// frames its response. Destroying even a valid empty response discards the
// receiver's connection-local pairing state before /pair-setup can use it.
export function requestAirPlayPinStart({
  address,
  port = 7000,
  agent,
  timeoutMs = 5000,
  onStage = () => {},
}) {
  return new Promise((resolve, reject) => {
    let settled = false;
    let response;
    const finish = (error, result) => {
      if (settled) return;
      settled = true;
      clearTimeout(deadline);
      if (error) {
        response?.destroy();
        request.destroy();
        reject(error);
      } else {
        resolve(result);
      }
    };
    const request = http.request({
      hostname: address,
      port,
      path: "/pair-pin-start",
      method: "POST",
      agent,
      headers: {
        "User-Agent": "AirPlay/381.13",
        "X-Apple-HKP": "3",
        "Content-Type": "application/octet-stream",
        "Content-Length": "0",
        Connection: "keep-alive",
      },
    }, (incoming) => {
      response = incoming;
      const statusCode = response.statusCode || 0;
      const transferEncoding = String(response.headers["transfer-encoding"] || "");
      const framed = response.headers["content-length"] !== undefined ||
        /(?:^|,)\s*chunked\s*$/i.test(transferEncoding) ||
        statusCode === 204 || statusCode === 304;
      const result = {
        ok: statusCode >= 200 && statusCode < 300,
        statusCode,
        responseFraming: framed ? "framed" : "unframed",
      };
      onStage({ stage: "pin-start-response", ...result, reusedSocket: request.reusedSocket });

      response.on("error", (error) => finish(error));
      response.on("aborted", () => finish(new Error("AirPlay PIN response ended before it was complete.")));
      if (!framed) {
        // Some receivers acknowledge the request with headers alone, omitting
        // Content-Length and never ending the body. Node cannot reuse this
        // close-delimited socket safely. Only this malformed case needs the
        // old socket-release workaround; do not wait forever for its `end`.
        finish(null, result);
        response.destroy();
        return;
      }

      let bytes = 0;
      response.on("data", (chunk) => {
        bytes += chunk.length;
        if (bytes > 65536) finish(new Error("AirPlay PIN response was unexpectedly large."));
      });
      response.on("end", () => finish(null, result));
      response.on("close", () => {
        if (!response.complete) finish(new Error("AirPlay PIN response was incomplete."));
      });
      response.resume();
    });
    const deadline = setTimeout(() => {
      finish(new Error("AirPlay PIN request timed out before a complete response."));
    }, timeoutMs);
    request.on("error", (error) => finish(error));
    onStage({ stage: "pin-start-request" });
    request.end();
  });
}

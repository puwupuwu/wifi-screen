import assert from "node:assert/strict";
import http from "node:http";
import net from "node:net";
import test from "node:test";
import { airPlayPasswordRequired, airPlayPairingInput, createAirPlayPairingSrp, requestAirPlayPinStart } from "./airplay-pairing-http.mjs";

test("Samsung status 0x84 advertises a configured password even without pw TXT", () => {
  assert.equal(airPlayPasswordRequired({ flags: "0x84" }), true);
  assert.equal(airPlayPasswordRequired({ sf: "0x84" }), true);
  assert.equal(airPlayPasswordRequired({ flags: 0x84 }), true);
  assert.equal(airPlayPasswordRequired({ pw: "true" }), true);
});

test("on-screen PIN flags and malformed metadata do not falsely claim password mode", () => {
  for (const flags of ["0x4", "0xc", "0x204", "invalid", ""]) {
    assert.equal(airPlayPasswordRequired({ flags }), false);
  }
});

test("configured password reaches SRP as exact UTF-8 including whitespace and punctuation", () => {
  const input = "  Test_Auth-\u00e9\u65e5!  ";
  let received;
  class MockSrpClient {
    constructor(params, salt, username, password, privateKey) {
      received = { params, salt, username, password, privateKey };
    }
  }
  const params = {}, salt = Buffer.alloc(16), privateKey = Buffer.alloc(32);
  createAirPlayPairingSrp(MockSrpClient, params, salt, { flags: "0x84" }, input, privateKey);
  assert.deepEqual(received.password, Buffer.from(input, "utf8"));
  assert.equal(received.username.toString(), "Pair-Setup");
  assert.equal(received.params, params);
  assert.equal(received.salt, salt);
  assert.equal(received.privateKey, privateKey);
});

test("numeric PIN normalization remains compatible while password accepts short text", () => {
  assert.equal(airPlayPairingInput({ flags: "0xc" }, " 12-34 "), "1234");
  assert.equal(airPlayPairingInput({ flags: "0x84" }, " a "), " a ");
  assert.throws(() => airPlayPairingInput({ flags: "0xc" }, "abc"), /code shown/);
  assert.throws(() => airPlayPairingInput({ flags: "0x84" }, ""), /password configured/);
});

async function receiver(t, handler, raw = false) {
  const sockets = new Set();
  const server = raw ? net.createServer(handler) : http.createServer(handler);
  server.on("connection", (socket) => {
    sockets.add(socket);
    socket.on("close", () => sockets.delete(socket));
  });
  await new Promise((resolve) => server.listen(0, "127.0.0.1", resolve));
  const agent = new http.Agent({ keepAlive: true, maxSockets: 1 });
  t.after(async () => {
    agent.destroy();
    for (const socket of sockets) socket.destroy();
    await new Promise((resolve) => server.close(resolve));
  });
  return { address: "127.0.0.1", port: server.address().port, agent };
}

function setupRequest(options) {
  return new Promise((resolve, reject) => {
    const request = http.request({
      hostname: options.address,
      port: options.port,
      agent: options.agent,
      path: "/pair-setup",
      method: "POST",
      headers: { "Content-Length": "0" },
    }, (response) => {
      response.resume();
      response.on("end", resolve);
      response.on("error", reject);
    });
    request.on("error", reject);
    request.end();
  });
}

for (const framing of ["empty", "chunked"]) {
  test(`PIN start preserves ${framing} keep-alive response socket for pair-setup`, async (t) => {
    const requests = [];
    const options = await receiver(t, (request, response) => {
      requests.push({ path: request.url, socket: request.socket });
      if (request.url === "/pair-pin-start") {
        assert.equal(request.headers["x-apple-hkp"], "3");
        assert.equal(request.headers["content-type"], "application/octet-stream");
      }
      response.writeHead(200, framing === "empty"
        ? { "Content-Length": "0", Connection: "keep-alive" }
        : { "Transfer-Encoding": "chunked", Connection: "keep-alive" });
      response.end(framing === "chunked" ? "acknowledged" : undefined);
    });
    const stages = [];
    const result = await requestAirPlayPinStart({ ...options, onStage: (stage) => stages.push(stage) });
    assert.equal(result.ok, true);
    assert.equal(result.responseFraming, "framed");
    await setupRequest(options);
    assert.deepEqual(requests.map((request) => request.path), ["/pair-pin-start", "/pair-setup"]);
    assert.equal(requests[0].socket, requests[1].socket);
    assert.deepEqual(stages.map((stage) => stage.stage), ["pin-start-request", "pin-start-response"]);
  });
}

test("PIN start reports receiver refusal instead of success", async (t) => {
  const options = await receiver(t, (_request, response) => {
    response.writeHead(403, { "Content-Length": "0" });
    response.end();
  });
  const result = await requestAirPlayPinStart(options);
  assert.equal(result.ok, false);
  assert.equal(result.statusCode, 403);
});

test("header-only unframed acknowledgement does not wait for an endless body", async (t) => {
  const options = await receiver(t, (socket) => {
    socket.once("data", () => socket.write("HTTP/1.1 200 OK\r\nConnection: keep-alive\r\n\r\n"));
  }, true);
  const result = await requestAirPlayPinStart({ ...options, timeoutMs: 200 });
  assert.equal(result.ok, true);
  assert.equal(result.responseFraming, "unframed");
});

test("incomplete framed response fails promptly rather than leaving pairing pending", async (t) => {
  const options = await receiver(t, (socket) => {
    socket.once("data", () => socket.end("HTTP/1.1 200 OK\r\nContent-Length: 4\r\n\r\nx"));
  }, true);
  await assert.rejects(requestAirPlayPinStart(options), /complete|aborted/i);
});

test("stalled receiver has a bounded PIN request deadline", async (t) => {
  const options = await receiver(t, () => {}, true);
  await assert.rejects(requestAirPlayPinStart({ ...options, timeoutMs: 50 }), /timed out/i);
});

test("oversized response is rejected without recording its contents", async (t) => {
  const options = await receiver(t, (_request, response) => {
    response.writeHead(200, { "Content-Length": "65537" });
    response.end(Buffer.alloc(65537, 65));
  });
  const stages = [];
  await assert.rejects(requestAirPlayPinStart({ ...options, onStage: (stage) => stages.push(stage) }), /unexpectedly large/i);
  assert.equal(JSON.stringify(stages).includes("AAAA"), false);
});

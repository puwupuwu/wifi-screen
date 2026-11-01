import assert from "node:assert/strict";
import test from "node:test";
import { chacha20poly1305 } from "@noble/ciphers/chacha.js";
import { takeAirPlayAudioNonce } from "./airplay-audio-nonce.mjs";

test("audio nonce remains unique beyond RTP sequence wrap", () => {
  const audio = { nonceCounter: 0n, sequence: 65530 };
  const seen = new Set();
  for (let index = 0; index < 70000; index++) {
    const nonce = takeAirPlayAudioNonce(audio);
    const trailer = nonce.subarray(4);
    assert.equal(trailer.readBigUInt64LE(), BigInt(index));
    assert.equal(seen.has(trailer.toString("hex")), false);
    seen.add(trailer.toString("hex"));
    audio.sequence = (audio.sequence + 1) & 0xffff;
  }
  assert.equal(seen.size, 70000);
  assert.equal(audio.nonceCounter, 70000n);
});

test("nonce is consumed before encryption or a send failure", () => {
  const audio = { nonceCounter: 10n };
  const discarded = takeAirPlayAudioNonce(audio);
  // A caller may fail before sending this reserved nonce. It is not reused.
  const next = takeAirPlayAudioNonce(audio);
  assert.equal(discarded.readBigUInt64LE(4), 10n);
  assert.equal(next.readBigUInt64LE(4), 11n);
});

test("the exact eight-byte trailer reconstructs the nonce used by AEAD", () => {
  const audio = { nonceCounter: 0x123456789abcdef0n };
  const key = new Uint8Array(32), header = Buffer.alloc(12), plaintext = Buffer.from("audio test frame");
  const nonce = takeAirPlayAudioNonce(audio);
  const ciphertext = chacha20poly1305(key, nonce, header.subarray(4)).encrypt(plaintext);
  const packet = Buffer.concat([header, Buffer.from(ciphertext), nonce.subarray(4)]);
  const receiverNonce = Buffer.concat([Buffer.alloc(4), packet.subarray(-8)]);
  const decrypted = chacha20poly1305(key, receiverNonce, packet.subarray(4, 12))
    .decrypt(packet.subarray(12, -8));
  assert.deepEqual(Buffer.from(decrypted), plaintext);
});

test("nonce exhaustion requires a new session and never wraps", () => {
  const audio = { nonceCounter: 0xffffffffffffffffn };
  assert.equal(takeAirPlayAudioNonce(audio).readBigUInt64LE(4), 0xffffffffffffffffn);
  assert.throws(() => takeAirPlayAudioNonce(audio), /exhausted/);
  assert.throws(() => takeAirPlayAudioNonce({}), /invalid/);
});

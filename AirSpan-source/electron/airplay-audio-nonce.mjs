// The audio trailer carries an independent 64-bit little-endian AEAD nonce.
// RTP's 16-bit sequence wraps every few minutes and must never be used as the
// encryption counter. Reserve before encryption/send so failed sends cannot
// reuse a nonce for another plaintext. Retransmits reuse the stored packet.
export function takeAirPlayAudioNonce(audio) {
  const counter = audio.nonceCounter;
  if (typeof counter !== "bigint" || counter < 0n || counter > 0xffffffffffffffffn) {
    throw new Error("AirPlay audio nonce counter is invalid or exhausted. Reconnect the receiver.");
  }
  audio.nonceCounter = counter + 1n;
  const nonce = Buffer.alloc(12);
  nonce.writeBigUInt64LE(counter, 4);
  return nonce;
}

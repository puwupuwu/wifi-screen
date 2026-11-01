// Keep the receiver's audio clock alive while muted, without sending captured sound.
export function audioPayload(data, enabled) {
  return enabled ? data : Buffer.alloc(data.byteLength);
}

export function effectiveAudioEnabled(enabled, muteState) {
  if (!enabled) return false;
  if (!muteState?.muteSyncRequired) return true;
  return muteState.muteSyncAvailable === true && muteState.pcMuted === false;
}

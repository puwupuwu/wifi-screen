const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const test = require('node:test');

// Exercise the JavaScript that ScreenServer.Viewer actually emits.
const source = fs.readFileSync(require('node:path').join(__dirname, 'Streaming.cs'), 'utf8');
const script = source.match(/<script>([\s\S]*?)<\/script>/)[1]
  .replace("'\"+root+@\"'", "'/test-session/'");
assert(!script.includes('+root+@'), 'C# root interpolation must be resolved');

function viewer(options = {}) {
  let now = 100000, nextTimer = 1;
  const timers = new Map(), sockets = [], images = [], revoked = [], draws = [];
  const elements = {
    screen: { width: 300, height: 150, style: {}, getContext: () => ({
      clearRect() {},
      drawImage(img) { if (options.drawError) throw Error('decode/draw unsupported'); draws.push(img); },
    }) },
    state: { textContent: '' }, full: {}, compat: {},
  };
  function schedule(fn, delay, interval = false) {
    const id = nextTimer++;
    timers.set(id, { fn, due: now + (delay || 0), interval: interval && delay });
    return id;
  }
  class FakeDate extends Date { static now() { return now; } }
  class Image {
    constructor() { this.width = 1280; this.height = 720; images.push(this); }
    set src(value) { this.source = value; }
    get src() { return this.source; }
  }
  class WebSocket {
    constructor(url) { this.url = url; this.readyState = 1; this.sent = []; sockets.push(this); }
    close() { this.readyState = 3; if (this.onclose) this.onclose(); }
    send(value) { if (options.sendError) throw Error('ACK failed'); this.sent.push(value); }
  }
  const objectURL = {
    createObjectURL() { if (options.blobError) throw Error('Blob URL unsupported'); return 'blob:' + images.length; },
    revokeObjectURL(value) { revoked.push(value); },
  };
  const window = { WebSocket, URL: options.noURL ? undefined : objectURL, innerWidth: 1920, innerHeight: 1080 };
  const context = vm.createContext({
    Date: FakeDate, Image, WebSocket, window,
    document: { getElementById: id => elements[id], documentElement: {} },
    location: { host: '192.0.2.1:8765' },
    setTimeout: (fn, delay) => schedule(fn, delay),
    clearTimeout: id => timers.delete(id),
    setInterval: (fn, delay) => schedule(fn, delay, true),
  });
  vm.runInContext(script, context);
  function advance(ms) {
    const end = now + ms;
    let iterations = 0;
    while (true) {
      const next = [...timers.entries()].filter(([, t]) => t.due <= end)
        .sort((a, b) => a[1].due - b[1].due || a[0] - b[0])[0];
      if (!next) break;
      assert(++iterations < 1000, 'timer loop should remain bounded');
      const [id, timer] = next;
      now = timer.due;
      if (timer.interval) timer.due += timer.interval; else timers.delete(id);
      timer.fn();
    }
    now = end;
  }
  function frame(socket = sockets.at(-1)) {
    socket.onmessage({ data: {} });
    return images.at(-1);
  }
  return { context, sockets, images, revoked, draws, elements, frame, advance,
    polls: () => images.filter(i => i.src?.includes('frame.jpg')) };
}

test('asynchronous WebSocket failures switch to JPEG compatibility polling', () => {
  const v = viewer();
  v.sockets[0].close();
  v.advance(1000);
  assert.equal(v.sockets.length, 2);
  v.sockets[1].onerror();
  assert.equal(v.context.mode, 'compatibility');
  assert.equal(v.polls().length, 1);
  v.advance(1000);
  assert.equal(v.sockets.length, 2, 'fallback must stop stream retries');
});

test('a socket that never delivers a first frame falls back within six seconds', () => {
  const v = viewer();
  v.advance(6000);
  assert.equal(v.context.mode, 'compatibility');
  assert.equal(v.polls().length, 1);
  assert.equal(v.sockets[0].readyState, 3);
});

test('unsupported Blob handling and image decode failures fall back safely', () => {
  const unsupported = viewer({ blobError: true });
  unsupported.frame();
  assert.equal(unsupported.context.mode, 'compatibility');
  assert.equal(unsupported.polls().length, 1);
  const decode = viewer();
  const image = decode.frame();
  image.onerror();
  assert.equal(decode.context.mode, 'compatibility');
  assert.deepEqual(decode.revoked, [image.src]);
  assert.equal(decode.polls().length, 1);
});

test('successful stream acknowledges only after drawing and maintains aspect ratio', () => {
  const v = viewer();
  const image = v.frame();
  assert.equal(v.sockets[0].sent.length, 0);
  image.onload();
  assert.equal(v.draws.length, 1);
  assert.deepEqual(v.sockets[0].sent, ['ok']);
  assert.deepEqual(v.revoked, [image.src]);
  assert.equal(v.elements.screen.style.width, '1920px');
  assert.equal(v.elements.screen.style.height, '1080px');
  v.context.window.innerWidth = 1000;
  v.context.window.innerHeight = 1000;
  v.context.window.onresize();
  assert.equal(v.elements.screen.style.width, '1000px');
  assert.equal(v.elements.screen.style.height, '563px');
  v.advance(4000);
  assert.equal(v.context.mode, 'stream');
  assert.equal(v.polls().length, 0);
});

test('draw and ACK exceptions fall back instead of stranding the stream', () => {
  for (const options of [{ drawError: true }, { sendError: true }]) {
    const v = viewer(options);
    const image = v.frame();
    image.onload();
    assert.equal(v.context.mode, 'compatibility');
    assert.equal(v.polls().length, 1);
    assert.deepEqual(v.revoked, [image.src]);
  }
});

test('stale socket and image callbacks cannot interfere with the active transport', () => {
  const v = viewer();
  const oldSocket = v.sockets[0], oldImage = v.frame();
  oldSocket.close();
  v.advance(1000);
  const current = v.sockets[1];
  oldSocket.onerror();
  oldImage.onload();
  assert.equal(v.context.ws, current);
  assert.equal(v.context.mode, 'stream');
  assert.equal(v.draws.length, 0);
  assert.deepEqual(oldSocket.sent, []);
  const pending = v.frame(current);
  v.elements.compat.onclick();
  pending.onload();
  current.onclose();
  assert.equal(v.context.mode, 'compatibility');
  assert.equal(v.draws.length, 0);
  assert.equal(v.polls().length, 1);
  assert.deepEqual(current.sent, []);
});

test('compatibility polling recovers from a hung request and ignores late completion', () => {
  const v = viewer({ noURL: true });
  const first = v.polls()[0];
  v.advance(6000);
  assert.equal(v.polls().length, 2);
  first.onload();
  first.onerror();
  assert.equal(v.draws.length, 0);
  const second = v.polls()[1];
  second.onload();
  assert.equal(v.draws.length, 1);
  v.advance(33);
  assert.equal(v.polls().length, 3);
});

test('a stream that stops decoding frames switches to compatibility mode', () => {
  const v = viewer();
  v.frame().onload();
  v.advance(6000);
  assert.equal(v.context.mode, 'compatibility');
  assert.equal(v.polls().length, 1);
});

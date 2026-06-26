const net = require("net");

const listenHost = process.env.RTSP_PROXY_LISTEN_HOST || "127.0.0.1";
const listenPort = Number(process.env.RTSP_PROXY_LISTEN_PORT || "8555");
const targetHost = process.env.RTSP_PROXY_TARGET_HOST || "192.168.1.37";
const targetPort = Number(process.env.RTSP_PROXY_TARGET_PORT || "554");

function rewriteRtspMessage(message) {
  const splitAt = message.indexOf("\r\n\r\n");
  if (splitAt < 0) return message;

  const head = message.slice(0, splitAt);
  const body = message.slice(splitAt);
  const fixed = head.replace(
    /^Transport:\s*RTP\/AVP(;[^\r\n]*interleaved=[^\r\n]*)$/gim,
    "Transport: RTP/AVP/TCP$1",
  );

  return fixed + body;
}

function flushCameraBuffer(state, client) {
  while (state.buffer.length > 0) {
    if (state.buffer[0] === 0x24) {
      if (state.buffer.length < 4) return;
      const frameLength = state.buffer.readUInt16BE(2);
      const totalLength = 4 + frameLength;
      if (state.buffer.length < totalLength) return;

      client.write(state.buffer.subarray(0, totalLength));
      state.buffer = state.buffer.subarray(totalLength);
      continue;
    }

    const text = state.buffer.toString("latin1");
    const headerEnd = text.indexOf("\r\n\r\n");
    if (headerEnd < 0) return;

    const header = text.slice(0, headerEnd + 4);
    const contentLength = /Content-Length:\s*(\d+)/i.exec(header);
    const messageLength = headerEnd + 4 + (contentLength ? Number(contentLength[1]) : 0);
    if (state.buffer.length < messageLength) return;

    const message = state.buffer.subarray(0, messageLength).toString("latin1");
    client.write(Buffer.from(rewriteRtspMessage(message), "latin1"));
    state.buffer = state.buffer.subarray(messageLength);
  }
}

const server = net.createServer((client) => {
  const camera = net.createConnection({ host: targetHost, port: targetPort });
  const state = { buffer: Buffer.alloc(0) };

  client.on("data", (chunk) => camera.write(chunk));
  camera.on("data", (chunk) => {
    state.buffer = Buffer.concat([state.buffer, chunk]);
    flushCameraBuffer(state, client);
  });

  const closeBoth = () => {
    client.destroy();
    camera.destroy();
  };

  client.on("error", closeBoth);
  camera.on("error", closeBoth);
  client.on("close", closeBoth);
  camera.on("close", closeBoth);
});

server.listen(listenPort, listenHost, () => {
  console.log(`RTSP transport fix proxy listening on ${listenHost}:${listenPort}, target ${targetHost}:${targetPort}`);
});

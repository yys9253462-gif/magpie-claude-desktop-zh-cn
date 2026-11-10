// bridge 首次请求偶发 404 时，独立报到让 Magpie 不把页面误判为无响应。
(() => {
  let next = 0;
  let answered = false;
  const pending = new Set();
  addEventListener("message", (event) => {
    if (event.source !== parent) return;
    const message = event.data;
    if (!message || message.magpie !== 1) return;
    if (message.event) { answered = true; return; }
    if (pending.delete(message.id)) answered = true;
  });
  const hello = () => {
    if (answered) return;
    const id = `boot-${++next}`;
    pending.add(id);
    parent.postMessage({ magpie: 1, id, method: "hello", params: {} }, "*");
  };
  hello();
  const timer = setInterval(hello, 400);
  setTimeout(() => clearInterval(timer), 9000);
  addEventListener("pagehide", () => clearInterval(timer));
})();

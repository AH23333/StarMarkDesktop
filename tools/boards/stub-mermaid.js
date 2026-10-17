/* 决策 4（§九〔裁决 2026-10-17·代裁〕）：mermaid 那颗插件摘除——
   引擎对它的引用被 alias 到这颗 stub；宿主自排 MainMenu 时⛔ 排它的入口。
   这颗报错串「StarMarkBoards.mermaidStub」同时是闸门测试的摘除标记（bundle 里必须能搜到它）。 */
export const mermaidToExcalidraw = async () => {
  throw new Error("StarMarkBoards.mermaidStub: mermaid 已随决策4摘除（§九 裁决块）");
};
export default mermaidToExcalidraw;

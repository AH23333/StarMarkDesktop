import * as React from "react";
import { createRoot } from "react-dom/client";
import {
  Excalidraw, MainMenu, CaptureUpdateAction,
  exportToBlob, exportToCanvas, exportToSvg, exportToClipboard,
  serializeAsJSON, loadFromBlob, bumpVersion
} from "@excalidraw/excalidraw";
/* 随包 bundle 的唯一出口（§21.2 那颗「用 9 颗旋钮」之外的页面侧形状）：
   名单外的引擎面一律 ⛔ 交——加出口前先过 §21.2 那张表。 */
window.StarMarkBoards = {
  React, createRoot, Excalidraw, MainMenu, CaptureUpdateAction,
  exportToBlob, exportToCanvas, exportToSvg, exportToClipboard,
  serializeAsJSON, loadFromBlob, bumpVersion
};

// StarMark 应用图标生成器（批次 VC-2 立骨，片263 重绘为「星群」）。
//
// 为什么要有这颗脚本，而不是让谁手工画一张：这颗标记的**几何只有这里一份出处**。
// 仓里原本有两处各自的"星"——托盘在 TrayHost.cs 里逐像素画一颗 #FCB630 的五角星，
// 浏览器扩展那边是 gen-icons.mjs 画的蓝紫渐变＋黄菱形。两处各画各的，迟早分岔（#123 那一族）。
// 运行时不再自己画像素：TrayHost 从这颗 .ico 里按系统小图标尺寸取一档。
//
// 片263 重绘（用户裁决「还是一颗五角星太俗套，配不上功能这么丰富的项目」）：
// 三方向打样（A 星标书签 / B 星群 / C 流星）用户选定 **B 星群**。语义：
// 你收进 StarMark 的每条东西都是一颗星，这个应用把它们**连成星群**——
// 一颗孤星压不住一个功能套件，一群星可以。品牌语言不动：
// 金色 STAR_RGB 沿用、主星仍是五角尖朝上、蓝紫三档对角渐变底。
// 画面构成（比例坐标，全在本文件一处）：
//   主星      —— 五角尖朝上，光学居中略下沉（重心偏下），SDF 圆角星尖；
//   SAT1/SAT2 —— 右上／左下两颗四芒星芒（一大一小，呼应"群"）；
//   连线      —— 主星心→两颗星芒心的细线，七成不透明度，若隐若现；
//   星点      —— 左上／右下两粒小圆点，把"天空"铺开；
//   小档简化  —— ≤24px 只留主星＋SAT1（连线星点在这个尺寸只是噪点），
//                主星放大收胖（画得出＝认得出，批次 WT 那一招沿用）。
// 渲染：逐样本 SDF＋smoothstep 抗锯齿；星后柔光把底往淡紫提亮一圈；
// 托盘那份任何档都不带底（16px 上带底只会糊成一团），纯金剪影住在 alpha 里。
//
// 用法（开发期一次性，构建不依赖它，也不需要装任何东西——只用 node 自带的 zlib）：
//   node scripts/make-app-icon.mjs
import { deflateSync } from 'node:zlib';
import { writeFileSync, mkdirSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const OUT_DIR = join(dirname(fileURLToPath(import.meta.url)), '..', 'assets', 'app');
const PREVIEW_DIR = join(OUT_DIR, 'preview');

// ===== 品牌读数（金星沿用批次 VC-2；底色三档对角渐变，全档 B>R——IsStar 分型靠它）=====
const STAR_RGB = [0xfc, 0xb6, 0x30];            // 托盘那颗的金色（TrayHost 里曾是同一组字节，片263 起唯一出处在这里）
const GRADIENT_STOPS = [                        // 对角渐变三档：t=0（左上）→0.5→1（右下）
  [0x20, 0x1a, 0x52],                           // 深靛
  [0x3e, 0x28, 0x9e],                           // 亮紫蓝
  [0x6e, 0x3f, 0xe0],                           // 亮紫罗兰
];
const GLOW_RGB = [0xb9, 0xa8, 0xff];            // 柔光往淡紫提亮（B=255>R=185，压不过金也压不过底的分型）
const CORNER_RATIO = 0.22;       // 圆角方块半径 / 画边长（Windows 磁贴那一档观感）
const SS = 8;                    // 每格线性采样次数（8×8＝64 点）
const SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256];

// ===== 星群布局（片263 用户三选一裁决 B；比例坐标，闸门从这几行读，不抄第二份）=====
const SMALL = 24;                // ≤SMALL 走小档简化：只留主星＋SAT1
const MAIN = { cx: 0.50, cy: 0.535, outer: 0.30, inner: 0.46, rounding: 0.024 };   // 主星：五角・尖朝上・光学居中略下沉
const SAT1 = { cx: 0.76, cy: 0.27, outer: 0.105, inner: 0.28 };  // 右上四芒星芒（小档也保留——星群语义的最小可读集）
const SAT2 = { cx: 0.26, cy: 0.74, outer: 0.068, inner: 0.28 };  // 左下四芒星芒（只在大档出现）
const DOT1 = { cx: 0.17, cy: 0.24, r: 0.016 };                   // 左上星点（只在大档出现）
const DOT2 = { cx: 0.84, cy: 0.66, r: 0.012 };                   // 右下星点（只在大档出现）
const LINE_W = 0.012;            // 连线半宽（主星心→星芒心；边缘 smoothstep 收软）
const LINE_ALPHA = 0.72;         // 连线不透明度（若隐若现，压住"连线太硬"）
const GLOW_K = 0.14;             // 柔光峰值提亮比例
const GLOW_SIGMA = 0.42;         // 柔光径向衰减 σ（/size）

// 小档粗细补偿：16px 上标准比例的星群会糊成一坨，主星放大收胖、SAT1 放大、圆角收小。
// 与"序号圆点半径给下限"（批次 WT）同一招：**画得出＝认得出**，几何仍在这里一处算。
const MAIN_SMALL = { cy: 0.52, outer: 0.42, inner: 0.52, rounding: 0.03 };
const SAT1_SMALL = { outer: 0.14 };

// 通用多边形 SDF：到各边最近距离，符号由奇偶环绕（射线法）定。
// 星、四芒星芒都是多边形，一个距离场管所有形状：减 rounding＝圆角、smoothstep＝软边。
function polySDF(px, py, verts) {
  let d = Infinity;
  let inside = false;
  for (let i = 0, j = verts.length - 1; i < verts.length; j = i++) {
    const [xi, yi] = verts[i], [xj, yj] = verts[j];
    const dx = xj - xi, dy = yj - yi;
    const len2 = dx * dx + dy * dy;
    let t = len2 > 0 ? ((px - xi) * dx + (py - yi) * dy) / len2 : 0;
    t = t < 0 ? 0 : t > 1 ? 1 : t;
    const ex = xi + t * dx - px, ey = yi + t * dy - py;
    const e = Math.sqrt(ex * ex + ey * ey);
    if (e < d) d = e;
    if ((yi > py) !== (yj > py) && px < ((xj - xi) * (py - yi)) / (yj - yi) + xi) inside = !inside;
  }
  return inside ? -d : d;
}

// 五角星十个顶点。k 偶＝尖、k 奇＝谷；第一个尖朝上（+π/2 是尖朝下，+π/5 会画出十芒——都红过）。
function starVertices(cx, cy, outer, inner) {
  const v = [];
  for (let k = 0; k < 10; k++) {
    const angle = -Math.PI / 2 + (k * Math.PI) / 5;
    const r = k % 2 === 0 ? outer : inner;
    v.push([cx + r * Math.cos(angle), cy + r * Math.sin(angle)]);
  }
  return v;
}

// 四芒星芒八个顶点（k 偶＝尖、k 奇＝谷）：比五角星轻，做"群"里的配角而不是抢主星。
function sparkleVertices(cx, cy, outer, inner) {
  const v = [];
  for (let k = 0; k < 8; k++) {
    const angle = -Math.PI / 2 + (k * Math.PI) / 4;
    const r = k % 2 === 0 ? outer : inner;
    v.push([cx + r * Math.cos(angle), cy + r * Math.sin(angle)]);
  }
  return v;
}

// 点到线段距离（连线的距离场）。
function segDist(px, py, ax, ay, bx, by) {
  const dx = bx - ax, dy = by - ay;
  const len2 = dx * dx + dy * dy;
  let t = len2 > 0 ? ((px - ax) * dx + (py - ay) * dy) / len2 : 0;
  t = t < 0 ? 0 : t > 1 ? 1 : t;
  return Math.sqrt((ax + t * dx - px) ** 2 + (ay + t * dy - py) ** 2);
}

function inRoundedRect(x, y, size, radius) {
  const lo = radius, hi = size - radius;
  if (x >= lo && x <= hi) return true;
  if (y >= lo && y <= hi) return true;
  const cx = x < lo ? lo : hi;
  const cy = y < lo ? lo : hi;
  return Math.hypot(x - cx, y - cy) <= radius;
}

// 逐样本覆盖度（0..1）：d<0 全在，d>1 出去，半像素软边。
// 与「超采样二值命中」的差别在边缘：64 个二值样本只有 65 级台阶，smoothstep 是连续的，
// 大档圆角与斜边不再有灰阶断层。
const coverOf = d => (d <= -0.5 ? 1 : d >= 0.5 ? 0 : 0.5 - d);
const clamp01 = x => (x < 0 ? 0 : x > 1 ? 1 : x);
const smoothstep = (e0, e1, x) => { const t = clamp01((x - e0) / (e1 - e0)); return t * t * (3 - 2 * t); };

function baseGradient(x, y, size) {
  const t = Math.min(1, Math.max(0, (x + y) / (2 * Math.max(size - 1, 1))));
  const a = t < 0.5 ? GRADIENT_STOPS[0] : GRADIENT_STOPS[1];
  const b = t < 0.5 ? GRADIENT_STOPS[1] : GRADIENT_STOPS[2];
  const k = t < 0.5 ? t * 2 : (t - 0.5) * 2;
  return [0, 1, 2].map(c => a[c] + (b[c] - a[c]) * k);
}

// 星群在某一档的覆盖场：给定像素，返回 0..1 的"金色覆盖度"。
// 连线压在星体下面（星体覆盖大处自然盖过线），星点星芒一律与主星同一组金。
function makeCoverField(size) {
  const u = v => v * size;
  const small = size <= SMALL;
  const main = small
    ? { cx: MAIN.cx, cy: MAIN_SMALL.cy, outer: MAIN_SMALL.outer, inner: MAIN_SMALL.inner, rounding: MAIN_SMALL.rounding }
    : MAIN;
  const mainVerts = starVertices(u(main.cx), u(main.cy), u(main.outer), u(main.outer * main.inner));
  const sat1Outer = u(small ? SAT1_SMALL.outer : SAT1.outer);
  const sat1Verts = sparkleVertices(u(SAT1.cx), u(SAT1.cy), sat1Outer, sat1Outer * SAT1.inner);
  const sat2Outer = u(SAT2.outer);
  const sat2Verts = sparkleVertices(u(SAT2.cx), u(SAT2.cy), sat2Outer, sat2Outer * SAT2.inner);
  const lines = small ? [] : [
    [MAIN.cx, MAIN.cy, SAT1.cx, SAT1.cy],
    [MAIN.cx, MAIN.cy, SAT2.cx, SAT2.cy],
  ];
  const lw = u(LINE_W);
  return (px, py) => {
    let g = coverOf(polySDF(px, py, mainVerts) - u(main.rounding));
    g = Math.max(g, coverOf(polySDF(px, py, sat1Verts)));
    if (!small) {
      g = Math.max(g, coverOf(polySDF(px, py, sat2Verts)));
      g = Math.max(g, coverOf(Math.hypot(px - u(DOT1.cx), py - u(DOT1.cy)) - u(DOT1.r)));
      g = Math.max(g, coverOf(Math.hypot(px - u(DOT2.cx), py - u(DOT2.cy)) - u(DOT2.r)));
      for (const [ax, ay, bx, by] of lines) {
        const d = segDist(px, py, u(ax), u(ay), u(bx), u(by));
        g = Math.max(g, (1 - smoothstep(lw * 0.7, lw * 1.6, d)) * LINE_ALPHA);
      }
    }
    return g;
  };
}

function makePng(size, withTile) {
  const corner = size * CORNER_RATIO;
  const coverField = makeCoverField(size);
  const cx = size / 2;
  const glowSigma = size * GLOW_SIGMA;
  const ss = size >= 48 ? 4 : SS;   // 大档不需要那么密的采样（小档才是抗锯齿吃紧的地方）
  const rows = [];
  for (let y = 0; y < size; y++) {
    const row = [0];
    for (let x = 0; x < size; x++) {
      let tileHits = 0, goldSum = 0;
      for (let j = 0; j < ss; j++) {
        for (let i = 0; i < ss; i++) {
          const px = x + (i + 0.5) / ss;
          const py = y + (j + 0.5) / ss;
          if (withTile && !inRoundedRect(px, py, size, corner)) continue;
          tileHits++;
          goldSum += coverField(px, py);
        }
      }
      const samples = ss * ss;
      const tileFrac = tileHits / samples;
      const goldFrac = goldSum / samples;
      if (tileFrac === 0) { row.push(0, 0, 0, 0); continue; }
      const cover = withTile ? tileFrac : goldFrac;
      const rgb = [0, 1, 2].map(c => {
        if (!withTile) return STAR_RGB[c];                       // 托盘：纯金剪影，形状全住在 alpha 里
        let bg = baseGradient(x + 0.5, y + 0.5, size)[c];
        const dx = x + 0.5 - cx, dy = y + 0.5 - cx;              // 柔光以画面中心为心（星群的"天穹"感）
        const glow = Math.exp(-(dx * dx + dy * dy) / (2 * glowSigma * glowSigma)) * GLOW_K;
        bg = bg + (GLOW_RGB[c] - bg) * glow;
        return Math.round((bg * (tileFrac - goldFrac) + STAR_RGB[c] * goldFrac) / tileFrac);
      });
      row.push(rgb[0], rgb[1], rgb[2], Math.round(255 * cover));
    }
    rows.push(Buffer.from(row));
  }
  const crcTable = [];
  for (let n = 0; n < 256; n++) {
    let c2 = n;
    for (let k = 0; k < 8; k++) c2 = c2 & 1 ? 0xedb88320 ^ (c2 >>> 1) : c2 >>> 1;
    crcTable[n] = c2 >>> 0;
  }
  const crc32 = buf => {
    let v = 0xffffffff;
    for (const b of buf) v = crcTable[(v ^ b) & 0xff] ^ (v >>> 8);
    return (v ^ 0xffffffff) >>> 0;
  };
  const chunk = (type, data) => {
    const len = Buffer.alloc(4); len.writeUInt32BE(data.length);
    const t = Buffer.from(type, 'ascii');
    const crc = Buffer.alloc(4); crc.writeUInt32BE(crc32(Buffer.concat([t, data])));
    return Buffer.concat([len, t, data, crc]);
  };
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(size, 0); ihdr.writeUInt32BE(size, 4);
  ihdr[8] = 8; ihdr[9] = 6;
  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk('IHDR', ihdr),
    chunk('IDAT', deflateSync(Buffer.concat(rows), { level: 9 })),
    chunk('IEND', Buffer.alloc(0)),
  ]);
}

// PNG-in-ICO：Vista 起的 shell 认这个形状；一份 .ico 装各档 PNG，比逐档 BMP 小得多也清楚得多。
function makeIco(pngs) {
  const header = Buffer.alloc(6);
  header.writeUInt16LE(0, 0); header.writeUInt16LE(1, 2); header.writeUInt16LE(pngs.length, 4);
  let offset = header.length + 16 * pngs.length;
  const entries = [], blobs = [];
  for (const { size, data } of pngs) {
    const e = Buffer.alloc(16);
    e.writeUInt8(size === 256 ? 0 : size, 0);
    e.writeUInt8(size === 256 ? 0 : size, 1);
    e.writeUInt16LE(1, 4); e.writeUInt16LE(32, 6);
    e.writeUInt32LE(data.length, 8); e.writeUInt32LE(offset, 12);
    entries.push(e); blobs.push(data); offset += data.length;
  }
  return Buffer.concat([header, ...entries, ...blobs]);
}

const build = withTile => makeIco(SIZES.map(size => ({ size, data: makePng(size, withTile) })));

mkdirSync(OUT_DIR, { recursive: true });
mkdirSync(PREVIEW_DIR, { recursive: true });
for (const [name, withTile] of [['StarMark.ico', true], ['StarMarkTray.ico', false]]) {
  const ico = build(withTile);
  writeFileSync(join(OUT_DIR, name), ico);
  console.log(`${name}  ${(ico.length / 1024).toFixed(1)} KB  档位 ${SIZES.join('/')}`);
}
for (const [tag, withTile] of [['tile', true], ['tray', false]]) {
  for (const size of [16, 32, 256]) {
    writeFileSync(join(PREVIEW_DIR, `${tag}-${size}.png`), makePng(size, withTile));
  }
}
console.log('预览写到 assets/app/preview/');

// StarMark 应用图标生成器（批次 VC-2）。
//
// 为什么要有这颗脚本，而不是让谁手工画一张：这颗标记的**几何只有这里一份出处**。
// 仓里原本有两处各自的"星"——托盘在 TrayHost.cs 里逐像素画一颗 #FCB630 的五角星，
// 浏览器扩展那边是 gen-icons.mjs 画的蓝紫渐变＋黄菱形。两处各画各的，迟早分岔（#123 那一族）。
// 这一批把它们合成一颗：**蓝紫渐变圆角方块 ＋ 金色五角星**，色值与星形比例都从现有两处取，
// 生成两份资产：
//   StarMark.ico      —— 带底的完整标记（窗口/任务栏/开始菜单/安装包/卸载面板/exe）
//   StarMarkTray.ico  —— 透明底只留星（托盘：16px 下带底会糊成一团）
// 运行时不再自己画像素：TrayHost 改成从这颗 .ico 里按系统小图标尺寸取一档。
//
// 用法（开发期一次性，构建不依赖它，也不需要装任何东西——只用 node 自带的 zlib）：
//   node scripts/make-app-icon.mjs
import { deflateSync } from 'node:zlib';
import { writeFileSync, mkdirSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const OUT_DIR = join(dirname(fileURLToPath(import.meta.url)), '..', 'assets', 'app');
const PREVIEW_DIR = join(OUT_DIR, 'preview');

// ===== 品牌读数（两处现存的合成，不是新发明）=====
const TILE_TOP_LEFT = [0x18, 0x18, 0x78];       // 扩展那颗的渐变起点
const TILE_BOTTOM_RIGHT = [0x36, 0x7c, 0xdc];   // 扩展那颗的渐变终点
const STAR_RGB = [0xfc, 0xb6, 0x30];            // 托盘那颗的金色（TrayHost.CreateStarIcon 里同一组字节）
const CORNER_RATIO = 0.22;       // 圆角方块半径 / 画边长（Windows 磁贴那一档观感）
const SS = 8;                    // 每格线性采样次数（8×8＝64 点）
const SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256];

// 小档的粗细补偿：16px 上"标准五角星"的角会糊成一坨（第一版实测就是 4×4 一个点），
// 所以小尺寸把外接半径放大一点、内径收得胖一点。与"序号圆点半径给下限"（批次 WT）同一招：
// **画得出＝认得出**，几何仍在这里一处算，不是两套形状。
const SMALL = 24;
const outerRatioOf = size => (size <= SMALL ? 0.42 : 0.38);
const innerRatioOf = size => (size <= SMALL ? 0.50 : 0.382);   // 0.382＝正五角星那一档

// 五角星用**十个顶点的多边形**判内外，不用极坐标线性插值。
// 后者会把每条边画成向外鼓的弧（第一版就是这样，看着像六瓣花而不是星）——直边才是星。
function starVertices(cx, cy, outer, inner) {
  const v = [];
  for (let k = 0; k < 10; k++) {
    const angle = -Math.PI / 2 + (k * Math.PI) / 5;   // k 偶＝尖、k 奇＝谷；第一个尖朝上
    const r = k % 2 === 0 ? outer : inner;
    v.push([cx + r * Math.cos(angle), cy + r * Math.sin(angle)]);
  }
  return v;
}

function pointInPolygon(x, y, verts) {
  let inside = false;
  for (let i = 0, j = verts.length - 1; i < verts.length; j = i++) {
    const [xi, yi] = verts[i], [xj, yj] = verts[j];
    if ((yi > y) !== (yj > y) && x < ((xj - xi) * (y - yi)) / (yj - yi) + xi) inside = !inside;
  }
  return inside;
}

function inRoundedRect(x, y, size, radius) {
  const lo = radius, hi = size - radius;
  if (x >= lo && x <= hi) return true;
  if (y >= lo && y <= hi) return true;
  const cx = x < lo ? lo : hi;
  const cy = y < lo ? lo : hi;
  return Math.hypot(x - cx, y - cy) <= radius;
}

function makePng(size, withTile) {
  const outer = size * outerRatioOf(size);
  const inner = outer * innerRatioOf(size);
  const corner = size * CORNER_RATIO;
  const cx = size / 2;
  // 光学居中：尖朝上的五角星重心偏下（下面两条腿），所以整颗往下挪一点点，
  // 否则在磁贴里看着"吊在上面"。0.06 是肉眼调出来的，不是算出来的。
  const cy = size / 2 + outer * 0.06;
  const star = starVertices(cx, cy, outer, inner);
  const ss = size >= 48 ? 4 : SS;   // 大档不需要那么密的采样（小档才是抗锯齿吃紧的地方）
  const rows = [];
  for (let y = 0; y < size; y++) {
    const row = [0];
    for (let x = 0; x < size; x++) {
      let tileHits = 0, starHits = 0;
      for (let j = 0; j < ss; j++) {
        for (let i = 0; i < ss; i++) {
          const px = x + (i + 0.5) / ss;
          const py = y + (j + 0.5) / ss;
          const inTile = !withTile || inRoundedRect(px, py, size, corner);
          if (!inTile) continue;
          tileHits++;
          if (pointInPolygon(px, py, star)) starHits++;
        }
      }
      const samples = ss * ss;
      const tileFrac = tileHits / samples;
      const starFrac = starHits / samples;
      if (tileFrac === 0) { row.push(0, 0, 0, 0); continue; }
      const t = (x + y) / (2 * Math.max(size - 1, 1));
      const bg = [0, 1, 2].map(k => TILE_TOP_LEFT[k] + (TILE_BOTTOM_RIGHT[k] - TILE_TOP_LEFT[k]) * t);
      // 预乘平均：星覆盖的那部分用金色，其余用底；alpha 由覆盖率决定（带底＝底覆盖率，托盘＝星覆盖率）
      const cover = withTile ? tileFrac : starFrac;
      const rgb = [0, 1, 2].map(k => {
        if (!withTile) return STAR_RGB[k];
        const onlyBg = bg[k] * (tileFrac - starFrac) + STAR_RGB[k] * starFrac;
        return Math.round(onlyBg / tileFrac);
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

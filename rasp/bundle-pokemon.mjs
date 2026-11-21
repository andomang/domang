// ============================================================
// bundle-pokemon.mjs - 배포용 게임 스크립트 묶기 (deploy.bat이 game 폴더를 복사한 직후 실행)
//   1) wwwroot/pokemon/index.html 에 적힌 순서대로 game/*.js 를 하나로 합침 → game/astera.bundle.js
//   2) brotli(최고 압축)·gzip 압축본을 함께 만듦 (.br / .gz) → 서버가 바로 보냄
//   3) 배포용 index.html 의 스크립트 태그 34개를 묶음 파일 하나로 바꿈
//   * 원본(C:\2026\meta\pokemon)은 건드리지 않음. 개별 파일도 그대로 둠(/pokeadmin 이 일부를 따로 씀)
//   * 묶음은 'use strict' 하나로 시작 (원래 30개 파일이 strict, 나머지 4개는 데이터만 있는 파일이라 안전)
// 사용: node bundle-pokemon.mjs <wwwroot/pokemon 폴더>
// ============================================================
import fs from 'fs';
import path from 'path';
import zlib from 'zlib';

const root = process.argv[2];
if (!root || !fs.existsSync(path.join(root, 'index.html'))) {
  console.error('사용법: node bundle-pokemon.mjs <wwwroot/pokemon 폴더>');
  process.exit(1);
}
const indexPath = path.join(root, 'index.html');
let html = fs.readFileSync(indexPath, 'utf8');

// 이미 묶인 index.html 이면(다시 실행) 아무것도 안 함
if (html.includes('game/astera.bundle.js')) { console.log('[묶기] 이미 묶여 있음 - 건너뜀'); process.exit(0); }

const tagRe = /[ \t]*<script src="(game\/[^"]+\.js)"><\/script>\r?\n?/g;
const files = [...html.matchAll(tagRe)].map(m => m[1]);
if (!files.length) { console.error('[묶기] 게임 스크립트 태그를 찾지 못함'); process.exit(1); }

const parts = ["'use strict';"];
let rawBytes = 0;
for (const f of files) {
  const src = fs.readFileSync(path.join(root, f), 'utf8').replace(/^\uFEFF/, '');
  rawBytes += Buffer.byteLength(src);
  // 파일 경계 표시 + 앞 파일이 세미콜론 없이 끝나도 안전하게
  parts.push('\n;/* ===== ' + f + ' ===== */\n' + src);
}
const bundle = parts.join('');
const out = path.join(root, 'game', 'astera.bundle.js');
fs.writeFileSync(out, bundle);
const br = zlib.brotliCompressSync(Buffer.from(bundle), { params: { [zlib.constants.BROTLI_PARAM_QUALITY]: 11, [zlib.constants.BROTLI_PARAM_SIZE_HINT]: bundle.length } });
const gz = zlib.gzipSync(Buffer.from(bundle), { level: 9 });
fs.writeFileSync(out + '.br', br);
fs.writeFileSync(out + '.gz', gz);

// index.html: 첫 스크립트 자리에 묶음 하나, 나머지 태그는 제거
let first = true;
html = html.replace(tagRe, (m) => {
  if (!first) return '';
  first = false;
  const indent = (m.match(/^[ \t]*/) || [''])[0];
  return indent + '<!-- 배포용 묶음: 게임 스크립트 ' + files.length + '개 (원본 순서 그대로) -->\n' + indent + '<script src="game/astera.bundle.js"></script>\n';
});
fs.writeFileSync(indexPath, html);

const kb = n => Math.round(n / 1024) + 'KB';
console.log('[묶기] 스크립트 ' + files.length + '개 → astera.bundle.js ' + kb(bundle.length) + ' (brotli ' + kb(br.length) + ', gzip ' + kb(gz.length) + ')');

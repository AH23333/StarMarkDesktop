// 临时：读踩坑记录尾部编号，跑完即删
import fs from 'node:fs'

const s = fs.readFileSync('docs/踩坑记录.md', 'utf8')
const lines = s.split('\n')
console.log('total lines:', lines.length)
for (let i = lines.length - 70; i < lines.length; i++) {
  const t = lines[i].trim()
  if (/^\d+\.\s/.test(t) || t.startsWith('## ')) console.log(`${i + 1}: ${t.slice(0, 90)}`)
}

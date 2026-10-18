import { cp,mkdir } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
const from=fileURLToPath(new URL('../ui/node_modules/vditor/dist/',import.meta.url));
const to=fileURLToPath(new URL('../ui/dist/vditor/dist/',import.meta.url));
await mkdir(to,{recursive:true});
await cp(from,to,{recursive:true,filter:source=>!source.endsWith('.d.ts')&&!source.endsWith('.map')});
await cp(fileURLToPath(new URL('../ui/node_modules/vditor/LICENSE',import.meta.url)),fileURLToPath(new URL('../ui/dist/vditor/LICENSE',import.meta.url)));
console.log('Copied offline note editor assets and license');

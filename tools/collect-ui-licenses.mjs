import {readFile,readdir,mkdir,writeFile} from 'node:fs/promises';
import {resolve,dirname} from 'node:path';
import {fileURLToPath} from 'node:url';
const ui=resolve(dirname(fileURLToPath(import.meta.url)),'../ui');
const lock=JSON.parse(await readFile(resolve(ui,'package-lock.json'),'utf8'));
const notices=[];
for(const [path,info] of Object.entries(lock.packages)) {
  if(!path.startsWith('node_modules/')||info.dev)continue;
  const files=await readdir(resolve(ui,path)).catch(()=>[]);
  const licenses=files.filter(name=>/^(licen[cs]e|copying|ofl)(\..*)?$/i.test(name));
  if(!files.length)continue; // dépendance facultative pour une autre plateforme
  const pkg=JSON.parse(await readFile(resolve(ui,path,'package.json'),'utf8'));
  // Ces deux paquets du même monorepo omettent la licence de leurs archives npm.
  if(!licenses.length&&['rehype-katex','remark-math'].includes(pkg.name)) {
    notices.push(pkg.name+' '+pkg.version+'\n'+await readFile(resolve(ui,'licenses/remark-math-MIT.txt'),'utf8'));continue;
  }
  if(!licenses.length)throw Error('Missing license text: '+pkg.name);
  for(const name of licenses)notices.push(pkg.name+' '+pkg.version+'\n'+await readFile(resolve(ui,path,name),'utf8'));
}
if(!notices.length)throw Error('No UI license notices collected');
notices.push('beUI source components\n'+await readFile(resolve(ui,'licenses/beui-MIT.txt'),'utf8'));
notices.push('lucide-animated source icons\n'+await readFile(resolve(ui,'licenses/lucide-animated-MIT.txt'),'utf8'));
await mkdir(resolve(ui,'dist/licenses'),{recursive:true});
await writeFile(resolve(ui,'dist/licenses/third-party.txt'),notices.join('\n\n────────────────────────\n\n'));
console.log('Collected '+notices.length+' UI license notices');

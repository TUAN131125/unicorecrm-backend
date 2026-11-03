import { createRequire } from "node:module";
import { readFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),"../..");
const frontend=path.join(root,"frontend/unicorecrm-web");
const require=createRequire(path.join(frontend,"package.json"));
const {createServer}=await import(pathToFileURL(require.resolve("vite")).href);
const snapshots=new Map([
 ["/src/modules/quotes/presentation/components/QuoteWorkflow.tsx","QuoteWorkflow.tsx"],
 ["/src/modules/quotes/presentation/components/QuoteSalesAcceptancePanel.tsx","QuoteSalesAcceptancePanel.tsx"],
 ["/src/modules/quotes/presentation/pages/QuoteDetailPage.tsx","QuoteDetailPage.tsx"],
]);
const server=await createServer({root:frontend,configFile:path.join(frontend,"vite.config.ts"),
 plugins:[{name:"quote-source-before-review",enforce:"pre",async load(id){
  const normalized=id.replaceAll("\\","/");
  for(const [suffix,file] of snapshots) if(normalized.endsWith(suffix))
   return readFile(path.join(root,"task-runtime/shared-smtp-quote/before",file),"utf8");
 }}],server:{host:"127.0.0.1",port:3058,strictPort:true}});
await server.listen();
process.on("SIGTERM",()=>void server.close().then(()=>process.exit(0)));

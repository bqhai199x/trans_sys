import { createInterface } from 'node:readline';
let initialized = false;
for await (const line of createInterface({input:process.stdin})) {
  const request=JSON.parse(line);
  if(request.method==='initialize') process.stdout.write(JSON.stringify({id:request.id,result:{userAgent:'fixture'}})+'\n');
  else if(request.method==='initialized') initialized=true;
  else if(request.method==='model/list') {
    if(!initialized)process.exit(2);
    const second=request.params.cursor==='second';
    process.stdout.write(JSON.stringify({id:request.id,result:{data:[{id:second?'second-model':'first-model',supportedReasoningEfforts:[{reasoningEffort:second?'high':'low'}]}],nextCursor:second?null:'second'}})+'\n');
  }
}

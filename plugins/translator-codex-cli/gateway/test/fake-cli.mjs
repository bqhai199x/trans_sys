import { writeFile } from 'node:fs/promises';
const args = process.argv.slice(2);
let input = '';
for await (const data of process.stdin) input += data;
if (input === 'hang') { setInterval(() => {}, 1000); }
else {
  const value = JSON.stringify({texts:['xe đỏ 😀']});
  const events = [
    {type:'thread.started',thread_id:'session-123'},
    {type:'item.completed',item:{type:'reasoning',text:'not final'}},
    {type:'item.completed',item:{type:'agent_message',text:value}},
    ...(input === 'no-completion' ? [] : [{type:'turn.completed',usage:{input_tokens:10,output_tokens:6}}]),
  ];
  const output = Buffer.from(events.map(e=>JSON.stringify(e)).join('\n')+'\n');
  for (let i=0;i<output.length;i++) process.stdout.write(output.subarray(i,i+1));
  await writeFile(args[args.indexOf('--output-last-message')+1],value);
}

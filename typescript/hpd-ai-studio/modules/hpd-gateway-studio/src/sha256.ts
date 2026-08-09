const constants = new Uint32Array([0x428a2f98,0x71374491,0xb5c0fbcf,0xe9b5dba5,0x3956c25b,0x59f111f1,0x923f82a4,0xab1c5ed5,0xd807aa98,0x12835b01,0x243185be,0x550c7dc3,0x72be5d74,0x80deb1fe,0x9bdc06a7,0xc19bf174,0xe49b69c1,0xefbe4786,0x0fc19dc6,0x240ca1cc,0x2de92c6f,0x4a7484aa,0x5cb0a9dc,0x76f988da,0x983e5152,0xa831c66d,0xb00327c8,0xbf597fc7,0xc6e00bf3,0xd5a79147,0x06ca6351,0x14292967,0x27b70a85,0x2e1b2138,0x4d2c6dfc,0x53380d13,0x650a7354,0x766a0abb,0x81c2c92e,0x92722c85,0xa2bfe8a1,0xa81a664b,0xc24b8b70,0xc76c51a3,0xd192e819,0xd6990624,0xf40e3585,0x106aa070,0x19a4c116,0x1e376c08,0x2748774c,0x34b0bcb5,0x391c0cb3,0x4ed8aa4a,0x5b9cca4f,0x682e6ff3,0x748f82ee,0x78a5636f,0x84c87814,0x8cc70208,0x90befffa,0xa4506ceb,0xbef9a3f7,0xc67178f2]);
const encoder = new TextEncoder();
export function framedSha256(frame: string, ...values: readonly Uint8Array[]): string {
  const parts: Uint8Array<ArrayBufferLike>[] = [encoder.encode(frame)];
  for (const value of values) { const length = new Uint8Array(8); new DataView(length.buffer).setBigUint64(0, BigInt(value.byteLength)); parts.push(length, value); }
  const size = parts.reduce((sum, value) => sum + value.byteLength, 0); const input = new Uint8Array(size); let offset = 0;
  for (const part of parts) { input.set(part, offset); offset += part.byteLength; }
  return sha256(input);
}
export function sha256Segments(...parts: readonly Uint8Array[]): string { const size=parts.reduce((sum,value)=>sum+value.byteLength,0);const input=new Uint8Array(size);let offset=0;for(const part of parts){input.set(part,offset);offset+=part.byteLength;}return sha256(input); }
export function uint64(value:bigint):Uint8Array{const bytes=new Uint8Array(8);new DataView(bytes.buffer).setBigUint64(0,value);return bytes;}
export function lengthFrame(value:Uint8Array):Uint8Array{const output=new Uint8Array(8+value.byteLength);output.set(uint64(BigInt(value.byteLength)));output.set(value,8);return output;}
function sha256(input: Uint8Array): string {
  const length = input.byteLength; const padded = new Uint8Array(((length + 9 + 63) >> 6) << 6); padded.set(input); padded[length] = 0x80;
  new DataView(padded.buffer).setBigUint64(padded.byteLength - 8, BigInt(length) * 8n);
  const state = new Uint32Array([0x6a09e667,0xbb67ae85,0x3c6ef372,0xa54ff53a,0x510e527f,0x9b05688c,0x1f83d9ab,0x5be0cd19]); const words = new Uint32Array(64); const view = new DataView(padded.buffer);
  for (let block = 0; block < padded.byteLength; block += 64) {
    for (let i = 0; i < 16; i++) words[i] = view.getUint32(block + i * 4);
    for (let i = 16; i < 64; i++) { const x=words[i-15]!,y=words[i-2]!; const s0=rotr(x,7)^rotr(x,18)^(x>>>3),s1=rotr(y,17)^rotr(y,19)^(y>>>10); words[i]=(words[i-16]!+s0+words[i-7]!+s1)>>>0; }
    let [a,b,c,d,e,f,g,h]=state;
    for(let i=0;i<64;i++){const s1=rotr(e!,6)^rotr(e!,11)^rotr(e!,25),ch=(e!&f!)^(~e!&g!),t1=(h!+s1+ch+constants[i]!+words[i]!)>>>0,s0=rotr(a!,2)^rotr(a!,13)^rotr(a!,22),maj=(a!&b!)^(a!&c!)^(b!&c!),t2=(s0+maj)>>>0;h=g;g=f;f=e;e=(d!+t1)>>>0;d=c;c=b;b=a;a=(t1+t2)>>>0;}
    const values=[a,b,c,d,e,f,g,h]; for(let i=0;i<8;i++) state[i]=(state[i]!+values[i]!)>>>0;
  }
  return [...state].map(value=>value.toString(16).padStart(8,'0')).join('');
}
function rotr(value:number,count:number):number{return(value>>>count)|(value<<(32-count));}

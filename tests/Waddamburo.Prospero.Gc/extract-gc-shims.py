from pathlib import Path
import struct
w=Path(__file__).resolve().parent
b=(w/'gc-arena-compat.o').read_bytes()
off=struct.unpack_from('<Q',b,40)[0]; size,n=struct.unpack_from('<HH',b,58)
secs=[struct.unpack_from('<IIQQQQIIQQ',b,off+i*size) for i in range(n)]
st=next(s for s in secs if s[1]==2); namesec=secs[st[6]]; names=b[namesec[4]:namesec[4]+namesec[5]]; syms=[]
for at in range(st[4],st[4]+st[5],st[9]):
 nm,info,other,idx,val,length=struct.unpack_from('<IBBHQQ',b,at)
 syms.append((names[nm:].split(b'\0')[0].decode(),idx,val,length))
out=['.text']
for name in ('waddamburo_gc_mmap_unlocked','waddamburo_gc_mprotect_unlocked','waddamburo_gc_madvise_unlocked','sysconf'):
 fn=next(s for s in syms if s[0]==name); sec=secs[fn[1]]; code=b[sec[4]+fn[2]:sec[4]+fn[2]+fn[3]]; rel={}
 for s in secs:
  if s[1]==4 and s[7]==fn[1]:
   for at in range(s[4],s[4]+s[5],s[9]):
    loc,info,add=struct.unpack_from('<QQq',b,at)
    if fn[2]<=loc<fn[2]+fn[3]:
     assert (info&0xffffffff) in (2,4)
     rel[loc-fn[2]]=(syms[info>>32][0],add)
 exported='emitted_sysconf' if name=='sysconf' else name
 out += ['.p2align 4',f'.globl {exported}',f'.type {exported},@function',exported+':']
 at=0
 while at<len(code):
  if at in rel:
   target,add=rel[at];out.append(f'.long {target} - . + ({add})');at+=4
  else:
   end=min([v for v in rel if v>at]+[len(code),at+16])
   out.append('.byte '+','.join(hex(v) for v in code[at:end]));at=end
 out.append(f'.size {exported},.-{exported}')
out+=['.section .rodata','.globl __sp_error_numbers','.hidden __sp_error_numbers','__sp_error_numbers:','.zero 256','.globl __sp_memory_advice','.hidden __sp_memory_advice','__sp_memory_advice:','.byte 0,1,2,3,4,255,255,255,5','.section .note.GNU-stack,"",@progbits']
(w/'gc-arena-shims.S').write_text('\n'.join(out)+'\n')
print('Extracted actual emitted mapping/protection/advice machine code and relocations')

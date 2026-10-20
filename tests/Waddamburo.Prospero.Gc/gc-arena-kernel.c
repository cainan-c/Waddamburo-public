#define _GNU_SOURCE
#include <stdint.h>
#include <stddef.h>
#include <sys/syscall.h>
#include <unistd.h>
#include <string.h>
#include <errno.h>
#include <stdatomic.h>
#include <sys/mman.h>
#include <dlfcn.h>
#include <pthread.h>
#include <assert.h>
/* Native thread reporting forwards to the host's pthread implementation. */
int __sp_device_pthread_create(void *thread,const void *attr,void *(*start)(void *),void *arg) {
 int (*real)(void *,const void *,void *(*)(void *),void *)=dlsym(RTLD_NEXT,"pthread_create");
 return real(thread,attr,start,arg);
}
int __sp_device_pthread_attr_getstacksize(const void *attr,size_t *size) {return pthread_attr_getstacksize(attr,size);}
int sceKernelConfiguredFlexibleMemorySize(size_t *size) {*size=0x1c000000;return 0;}
uintptr_t scePthreadSelf(void) {return (uintptr_t)pthread_self();}
int scePthreadGetaffinity(uintptr_t thread,void *mask) {(void)thread;*(uint64_t*)mask=255;return 0;}
/* Real inaccessible host pages, not just a byte-array simulation. */
#define PAGE 0x4000UL
#define MAX_MAPS 8192
typedef struct {uintptr_t lo,hi;unsigned char *state;} region;
static region maps[MAX_MAPS];
static unsigned char states[262144]; /* 4 GiB of physical-page state */
static size_t state_used;
static unsigned maps_created,remaps,discarded,unmapped;
static uintptr_t fail_query,committed_query;
static size_t flexible_available=0x17b00000;
static int fail_large_backing;
static int fail_direct_allocation;
static uint64_t flexible_requested,direct_live,next_physical;
typedef struct {int64_t physical;size_t size;} direct_block;
static direct_block direct_blocks[64];
size_t sceKernelGetDirectMemorySize(void) {return 0x200000000UL;}
int sceKernelAvailableDirectMemorySize(int64_t lo,int64_t hi,size_t alignment,int64_t *physical,size_t *size) {
 assert(lo==0&&hi==0x200000000UL&&alignment==PAGE);*physical=next_physical;*size=0x200000000UL-direct_live;return 0;
}
unsigned long gc_test_flexible_requested(void) {return flexible_requested;}
unsigned long gc_test_direct_live(void) {return direct_live;}
void gc_test_fail_direct_allocation(void) {fail_direct_allocation=1;}
int sceKernelAllocateDirectMemory(int64_t lo,int64_t hi,size_t size,size_t alignment,int type,int64_t *physical) {
 assert(lo==0&&hi==0x200000000UL&&alignment==PAGE&&type==11);
 if(fail_direct_allocation){fail_direct_allocation=0;return (int)0x8002000c;}
 for(int i=0;i<64;i++)if(!direct_blocks[i].size){
  *physical=next_physical;next_physical+=size;direct_blocks[i]=(direct_block){*physical,size};direct_live+=size;return 0;
 }
 return (int)0x8002000c;
}
int sceKernelReleaseDirectMemory(int64_t physical,size_t size) {
 for(int i=0;i<64;i++)if(direct_blocks[i].size==size&&direct_blocks[i].physical==physical) {
  direct_blocks[i].size=0;direct_live-=size;return 0;
 }
 return (int)0x80020016;
}
void gc_test_set_available(size_t size) {flexible_available=size;}
void gc_test_fail_large_map(void) {fail_large_backing=1;}
int sceKernelAvailableFlexibleMemorySize(size_t *size) {*size=flexible_available;return 0;}
void gc_test_fail_query_once(void *p) {fail_query=(uintptr_t)p;}
void gc_test_committed_metadata_once(void *p) {committed_query=(uintptr_t)p;}
void *mmap64(void *p,size_t n,int prot,int flags,int fd,off64_t offset) {return mmap(p,n,prot,flags,fd,offset);}
static region *find(uintptr_t p) {for(int i=MAX_MAPS-1;i>=0;i--)if(p>=maps[i].lo&&p<maps[i].hi&&maps[i].state[(p-maps[i].lo)/PAGE]!=255)return &maps[i];return 0;}
void __sp_set_errno(int e) {errno=e;}
int sceKernelDebugOutText(int channel,const char *s) {(void)channel;return syscall(SYS_write,2,s,strlen(s))<0?-1:0;}
int sceKernelVirtualQuery(const void *address,int flags,void *info,size_t size) {
 (void)flags; uintptr_t p=(uintptr_t)address;region *r=find(p);
 if(fail_query==p){fail_query=0;return (int)0x80020016;}
 if(committed_query==p){committed_query=0;memset(info,0,size);*((char*)info+32)=0x10;return 0;}
 if(!r){
  unsigned char resident;
  if(syscall(SYS_mincore,p&~0xfffUL,0x1000,&resident))return (int)0x80020016;
  memset(info,0,size);*(uint32_t*)((char*)info+24)=3;*((char*)info+32)=0x10;return 0;
 }
 unsigned char state=r->state[(p-r->lo)/PAGE];
 if(state==255)return (int)0x80020016;
 memset(info,0,size);*(uintptr_t*)info=p&~(PAGE-1);*((uintptr_t*)info+1)=(p&~(PAGE-1))+PAGE;
 if(state) {unsigned char prot=state&0x7f;*(uint32_t*)((char*)info+24)=prot==1?0:(prot==2?1:3);*((char*)info+32)=state&0x80?0x12:0x11;}
 return 0;
}
static int create(void **address,size_t length,int backed,int prot,int flags) {
 if(!length || length%PAGE)return (int)0x80020016;
 uintptr_t p=(uintptr_t)*address;
 if(flags&16) {
  region *r=find(p);if(!r||p+length>r->hi)return (int)0x80020016;
  size_t at=(p-r->lo)/PAGE;
  for(size_t i=0;i<length/PAGE;i++)if(r->state[at+i])remaps++;
  void *v=(void*)syscall(SYS_mmap,p,length,backed?prot:0,MAP_PRIVATE|MAP_ANONYMOUS|MAP_FIXED,-1,0);
  if(v==MAP_FAILED)return (int)0x8002000c;
  memset(r->state+at,backed?(prot==0?1:prot==1?2:3):0,length/PAGE);
  return 0;
 }
 if(state_used+length/PAGE>sizeof(states))return (int)0x8002000c;
 void *raw=(void*)syscall(SYS_mmap,0,length+PAGE,backed?prot:0,MAP_PRIVATE|MAP_ANONYMOUS,-1,0);
 if(raw==MAP_FAILED)return (int)0x8002000c;
 p=((uintptr_t)raw+PAGE-1)&~(PAGE-1);
 if(p>(uintptr_t)raw)syscall(SYS_munmap,raw,p-(uintptr_t)raw);
 uintptr_t tail=(uintptr_t)raw+length+PAGE;
 if(tail>p+length)syscall(SYS_munmap,p+length,tail-p-length);
 for(int i=0;i<MAX_MAPS;i++)if(!maps[i].hi) {
  maps[i]=(region){p,p+length,states+state_used};
  memset(states+state_used,backed?(prot==0?1:prot==1?2:3):0,length/PAGE);
  state_used+=length/PAGE;*address=(void*)p;maps_created++;return 0;
 }
 return (int)0x8002000c;
}
int sceKernelMapFlexibleMemory(void **p,size_t n,int prot,int flags,int a,int b) {
 flexible_requested+=n;
 if(a||(!(flags&16)&&b))return (int)0x80020016;
 return create(p,n,1,prot==2?3:prot,flags);
}
int sceKernelMapDirectMemory(void **p,size_t n,int prot,int flags,int64_t physical,size_t alignment) {
 assert(prot==3&&flags==16&&alignment==PAGE);
 int allocated=0;for(int i=0;i<64;i++)if(direct_blocks[i].size==n&&direct_blocks[i].physical==physical)allocated=1;
 assert(allocated);
 if(fail_large_backing){fail_large_backing=0;return (int)0x8002000c;}
 int rc=create(p,n,1,3,flags);if(rc)return rc;
 region *r=find((uintptr_t)*p);
 for(size_t i=0;i<n/PAGE;i++)r->state[((uintptr_t)*p-r->lo)/PAGE+i]|=0x80;
 /* Direct physical memory is not guaranteed clean, unlike MAP_ANON. */
 memset(*p,0xa5,PAGE);memset((char*)*p+(n/2&~(PAGE-1)),0xa5,PAGE);memset((char*)*p+n-PAGE,0xa5,PAGE);
 return 0;
}
int sceKernelReserveVirtualRange(void **p,size_t n,int flags,int alignment) {(void)alignment;return create(p,n,0,0,flags);}
int sceKernelMprotect(void *address,size_t length,int prot) {
 uintptr_t p=(uintptr_t)address;region *r=find(p);
 if(!r)return syscall(SYS_mprotect,address,length,prot==2?3:prot)?(int)0x80020016:0;
 if(!length||p%PAGE||length%PAGE||p+length>r->hi)return (int)0x80020016;
 for(size_t at=(p-r->lo)/PAGE;at<(p-r->lo+length)/PAGE;at++) {
  if(r->state[at]==255)return (int)0x80020016;
  /* Match PS5: protecting a reservation succeeds but does not commit it. */
  if(r->state[at]) {
   if(syscall(SYS_mprotect,r->lo+at*PAGE,PAGE,prot==2?3:prot))return (int)0x8002000d;
   r->state[at]=(r->state[at]&0x80)|(prot==0?1:prot==1?2:3);
  }
 }
 return 0;
}
int __sp_device_madvise(void *p,size_t n,int advice) {
 if(advice==4 || advice==5) {discarded++;return syscall(SYS_madvise,p,n,MADV_DONTNEED);}
 return 0;
}
int __sp_device_munmap(void *address,size_t length) {
 uintptr_t p=(uintptr_t)address;region *r=find(p);
 if(!r || p%PAGE || length%PAGE || p+length>r->hi) {errno=22;return -1;}
 if(syscall(SYS_munmap,p,length))return -1;
 memset(r->state+(p-r->lo)/PAGE,255,length/PAGE);unmapped++;
 return 0;
}
int sceKernelMunmap(void *address,size_t length) {return __sp_device_munmap(address,length) ? (int)0x80020016 : 0;}
long sysconf(int name) {if(name==30)return PAGE;if(name==83||name==84)return 8;if(name==85)return sceKernelGetDirectMemorySize()/PAGE;if(name==86)return (sceKernelGetDirectMemorySize()-direct_live)/PAGE;long (*real)(int)=dlsym(RTLD_NEXT,"sysconf");return real(name);}
unsigned gc_test_remaps(void) {return remaps;}
unsigned gc_test_discards(void) {return discarded;}
unsigned gc_test_unmaps(void) {return unmapped;}
unsigned gc_test_maps(void) {return maps_created;}

#define _GNU_SOURCE
#include <assert.h>
#include <stdio.h>
#include <sys/mman.h>
#include <stdint.h>
#include <errno.h>
#include <pthread.h>
#include <string.h>
extern unsigned gc_test_remaps(void),gc_test_discards(void),gc_test_unmaps(void);
extern int waddamburo_gc_memory_self_test(void);
extern int sceKernelVirtualQuery(const void *,int,void *,size_t);
extern void gc_test_fail_query_once(void *),gc_test_committed_metadata_once(void *);
extern void gc_test_set_available(size_t),gc_test_fail_large_map(void),gc_test_fail_direct_allocation(void);
extern unsigned long gc_test_flexible_requested(void),gc_test_direct_live(void);
extern long emitted_sysconf(int);
static unsigned char *race_page;
static pthread_barrier_t barrier;
static void *worker(void *arg) {
 uintptr_t offset=(uintptr_t)arg;
 pthread_barrier_wait(&barrier);
 for(int i=0;i<1000;i++) {
  assert(mprotect(race_page+offset,0x1000,3)==0);
  memset(race_page+offset,offset?0x91:0x53,0x1000);
 }
 return 0;
}
int main(void) {
 assert(emitted_sysconf(85)==0x200000000UL/0x4000);
 assert(emitted_sysconf(86)==0x200000000UL/0x4000);
 puts("PASS emitted CLR physical-memory queries use the direct pool instead of the 448 MiB flexible pool");
 for(int i=0;i<1000;i++)assert(waddamburo_gc_memory_self_test()==0);
 puts("PASS 1000 full decommit/recommit cycles with real inaccessible reserved pages");
 unsigned char *p=mmap(0,0x10000,3,0x22,-1,0);assert(p!=MAP_FAILED);memset(p,0xa5,0x10000);
 assert(mmap(p+0x3000,0x6000,0,0x32,-1,0)==p+0x3000);
 for(int i=0;i<0x10000;i++)assert(p[i]==(i>=0x3000&&i<0x9000?0:0xa5));
 assert(madvise(p+0x9000,0x1000,MADV_DONTNEED)==0);
 for(int i=0;i<0x10000;i++)assert(p[i]==(i>=0x3000&&i<0xa000?0:0xa5));
 memset(p,0xa5,0x10000);assert(madvise(p,0x10000,MADV_FREE)==0);
 for(int i=0;i<0x10000;i++)assert(p[i]==0xa5);
 assert(gc_test_discards()==0);assert(munmap(p,0x10000)==0);
 puts("PASS cross-page exact zeroing, both live neighbors, DONTNEED and non-destructive FREE");
 p=mmap(0,0x10000,0,0x22,-1,0);assert(p!=MAP_FAILED);race_page=p;
 pthread_t a,b;pthread_barrier_init(&barrier,0,3);
 pthread_create(&a,0,worker,0);pthread_create(&b,0,worker,(void*)0x1000);
 pthread_barrier_wait(&barrier);pthread_join(a,0);pthread_join(b,0);pthread_barrier_destroy(&barrier);
 for(int i=0;i<0x2000;i++)assert(p[i]==(i<0x1000?0x53:0x91));
 assert(mprotect(p,0x10000,3)==0);
 for(int i=0x4000;i<0x10000;i++)assert(p[i]==0);
 assert(gc_test_remaps()==0);assert(munmap(p,0x10000)==0);
 puts("PASS simultaneous commits to two 4 KiB subpages preserve live bytes; mixed-range commit");
 p=mmap(0,0x10000,0,0x22,-1,0);assert(p!=MAP_FAILED);
 gc_test_fail_query_once(p);
 assert(mprotect(p,0x4000,3)==-1); /* Kernel protection succeeds but backing is still absent. */
 assert(mprotect(p,0x4000,3)==0);memset(p,0x71,0x4000);
 gc_test_committed_metadata_once(p);assert(mprotect(p,0x4000,3)==0);
 for(int i=0;i<0x4000;i++)assert(p[i]==0x71);
 assert(gc_test_remaps()==0);assert(munmap(p,0x10000)==0);
 puts("PASS failed query cannot report a false successful commit; committed-only flags preserve live data");
 size_t size=0x40010000;
 gc_test_set_available(0x17b00000); /* Actual console: 448 MiB configured, 379 MiB free. */
 unsigned long flexible_before=gc_test_flexible_requested();
 p=mmap(0,size,0,0x22,-1,0);assert(p!=MAP_FAILED);
 assert(gc_test_flexible_requested()==flexible_before&&gc_test_direct_live()==size);
 assert(emitted_sysconf(86)==(0x200000000UL-size)/0x4000);
 for(size_t i=0;i<size;i+=0x4000) {
  _Alignas(8) unsigned char query[72];assert(!sceKernelVirtualQuery(p+i,0,query,sizeof(query)));
  assert(query[32]&2);assert(*(int*)(query+24)&2);
 }
 for(size_t i=0;i<0x4000;i++)assert(!p[i]&&!p[(size/2&~0x3fffUL)+i]&&!p[size-0x4000+i]);
 p[0]=51;p[size-1]=93;
 assert(munmap(p+size-0x4000,0x4000)==0);assert(gc_test_direct_live()==size);
 assert(mprotect(p,size-0x4000,3)==0);assert(p[0]==51);
 /* A middle unmap splits VA ownership but must not release other live pieces. */
 assert(munmap(p+0x4000,0x4000)==0);assert(gc_test_direct_live()==size);
 assert(munmap(p,0x4000)==0);assert(gc_test_direct_live()==size);
 assert(munmap(p+0x8000,size-0xc000)==0);assert(gc_test_direct_live()==0);
 puts("PASS >1 GiB managed arena uses direct memory with a 448 MiB flexible pool; dirty physical pages zeroed");
 puts("PASS prefix/suffix trims and middle split retain physical ownership until the final mapping is released");
 gc_test_fail_direct_allocation();assert(mmap(0,size,0,0x22,-1,0)==MAP_FAILED);assert(gc_test_direct_live()==0);
 gc_test_fail_large_map();assert(mmap(0,size,0,0x22,-1,0)==MAP_FAILED);assert(gc_test_direct_live()==0);
 assert(gc_test_flexible_requested()==flexible_before);
 puts("PASS direct allocation/map failures roll back reservations and physical memory without flexible fallback");
 unsigned before=gc_test_unmaps();p=mmap(0,0x1000,3,0x22,-1,0);assert(p!=MAP_FAILED);
 assert(munmap(p,0x1000)==0);assert(gc_test_unmaps()==before+1);
 assert(mmap(0,0,3,0x22,-1,0)==MAP_FAILED);assert(mprotect(0,0,3)==-1);
 assert(mmap(0,4096,3,2,7,0)==MAP_FAILED);
 assert(gc_test_remaps()==0&&gc_test_discards()==0);
 puts("PASS short full-allocation release, invalid ranges, unsupported file mapping rejection");
}

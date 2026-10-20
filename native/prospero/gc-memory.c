/* NativeAOT VM compatibility for the PS5 physical 16 KiB page size.
 * The emitted shim uses the edge helper; the native wrappers below own the
 * complete commit/decommit/discard/release policy for application modules. */
#include <stddef.h>
#include <stdint.h>
#include <stdatomic.h>
extern int sceKernelVirtualQuery(const void *, int, void *, size_t);
static int zero_edge(uintptr_t begin, uintptr_t end) {
    if (begin >= end) return 0;
    _Alignas(8) unsigned char info[0x48] = {0};
    int result = sceKernelVirtualQuery((const void *)begin, 0, info, sizeof(info));
    if (result) return result;
    uint32_t protection = *(uint32_t *)(info + 0x18);
    /* Reserved pages have no readable contents and will be zero on first map. */
    if (!protection && !(info[0x20] & 0x1f)) return 0;
    if (!(protection & 2)) return (int)0x8002000d; /* EACCES */
    for (volatile unsigned char *p = (volatile unsigned char *)begin;
         (uintptr_t)p < end; ++p) *p = 0;
    return 0;
}
int waddamburo_gc_zero_release_edges(void *address, size_t length) {
    uintptr_t begin = (uintptr_t)address, end = begin + length;
    if (end < begin) return (int)0x80020016; /* EINVAL */
    const uintptr_t mask = 0x3fff;
    if (begin & mask) {
        uintptr_t edge = (begin & ~mask) + 0x4000;
        int result = zero_edge(begin, end < edge ? end : edge);
        if (result || end <= edge) return result;
    }
    if (end & mask) {
        uintptr_t edge = end & ~mask;
        return zero_edge(edge < begin ? begin : edge, end);
    }
    return 0;
}

/* Run through the linked application shims, not through native SDK wrappers. */
extern void *mmap(void *, size_t, int, int, int, long);
extern int mprotect(void *, size_t, int);
extern int munmap(void *, size_t);
int waddamburo_gc_memory_self_test(void) {
    const size_t size = 0x10000;
    unsigned char *p = mmap(0, size, 3, 0x22, -1, 0);
    if (p == (void *)-1) return 1;
    int result = 0;
    for (size_t i = 0; i < size; ++i) p[i] = 0xa5;
    if (mmap(p + 0x1000, 0x1000, 0, 0x32, -1, 0) == (void *)-1) { result = 2; goto done; }
    if (mprotect(p + 0x1000, 0x1000, 3)) { result = 3; goto done; }
    for (size_t i = 0; i < size; ++i) {
        unsigned char expected = i >= 0x1000 && i < 0x2000 ? 0 : 0xa5;
        if (p[i] != expected) { result = 4; goto done; }
    }
    if (mmap(p + 0x4000, 0x4000, 0, 0x32, -1, 0) == (void *)-1) { result = 5; goto done; }
    /* Recommit must preserve data outside both released ranges. */
    if (mprotect(p, size, 3)) { result = 6; goto done; }
    for (size_t i = 0; i < size; ++i) {
        unsigned char expected = (i >= 0x1000 && i < 0x2000) || (i >= 0x4000 && i < 0x8000) ? 0 : 0xa5;
        if (p[i] != expected) { result = 7; goto done; }
    }
done:
    munmap(p, size);
    if (result) return result;
    /* A separate reservation really does mix backed and reserved pages. */
    p = mmap(0, size, 0, 0x22, -1, 0);
    if (p == (void *)-1) return 8;
    if (mprotect(p, 0x1000, 3)) { result = 9; goto mixed_done; }
    for (size_t i = 0; i < 0x4000; ++i) p[i] = 0x5a;
    if (mprotect(p, size, 3)) { result = 10; goto mixed_done; }
    for (size_t i = 0; i < size; ++i)
        if (p[i] != (i < 0x4000 ? 0x5a : 0)) { result = 11; break; }
mixed_done:
    munmap(p, size);
    return result;
}

/* The console's flexible pool is only 448 MiB. The 1.5 GiB managed
 * reservation therefore uses CPU-cached direct memory, keeping the configured
 * 1 GiB managed heap independent of flexible memory used by threads/runtime.
 * Logical decommit still clears exactly its bytes and retains backing. An
 * allocation owner retains physical memory across VA alignment trims/splits;
 * it releases the whole physical allocation once its last mapping is unmapped.
 * Native graphics/media allocations keep their separate PS5 ABI and budgets. */
#define PS5_PAGE ((uintptr_t)0x4000)
#define PS5_MASK (PS5_PAGE-1)
#define ARENA_MIN ((size_t)0x40000000)
#define VM_SLOTS 4096
typedef struct { uintptr_t lo, hi, logical_end; int arena; unsigned direct_id; } vm_range;
#define DIRECT_OWNERS 64
typedef struct { int64_t physical; size_t size, live; } direct_owner;
static direct_owner direct_owners[DIRECT_OWNERS];
static vm_range ranges[VM_SLOTS];
static atomic_uint mapping_gate;
static void mapping_enter(void) {
    for (;;) {
        if (!atomic_exchange_explicit(&mapping_gate,1,memory_order_acquire)) return;
        while (atomic_load_explicit(&mapping_gate,memory_order_relaxed))
            __asm__ volatile("pause");
    }
}
static void mapping_leave(void) { atomic_store_explicit(&mapping_gate,0,memory_order_release); }
extern void *waddamburo_gc_mmap_unlocked(void *,size_t,int,int,int,long);
extern int waddamburo_gc_mprotect_unlocked(void *,size_t,int);
extern int waddamburo_gc_madvise_unlocked(void *,size_t,int);
extern int sceKernelMunmap(void *,size_t);
extern void __sp_set_errno(int);
extern int sceKernelMprotect(void *,size_t,int);
extern int sceKernelAvailableFlexibleMemorySize(size_t *);
extern size_t sceKernelGetDirectMemorySize(void);
extern int sceKernelAllocateDirectMemory(int64_t,int64_t,size_t,size_t,int,int64_t *);
extern int sceKernelMapDirectMemory(void **,size_t,int,int,int64_t,size_t);
extern int sceKernelReleaseDirectMemory(int64_t,size_t);
extern int sceKernelAvailableDirectMemorySize(int64_t,int64_t,size_t,int64_t *,size_t *);
static void report_direct_failure(const char *,int,size_t);
extern int sceKernelDebugOutText(int,const char *);
static int fail(int e) { __sp_set_errno(e); return -1; }
static int valid_range(uintptr_t begin,size_t size) {
    return size && size <= UINTPTR_MAX-begin && begin+size <= UINTPTR_MAX-PS5_MASK;
}
static int free_slot(void) {
    for (int i=0;i<VM_SLOTS;++i) if (!ranges[i].hi) return i;
    return -1;
}
static int contains(uintptr_t begin,uintptr_t end) {
    for (int i=0;i<VM_SLOTS;++i)
        if (ranges[i].hi && begin>=ranges[i].lo && end<=ranges[i].hi) return i;
    return -1;
}
/* The lock covers query, zeroing, protection changes, commit and final release.
 * A native-only critical section: no allocation or managed callbacks. */
static int clear_released(uintptr_t begin,uintptr_t end) {
    while (begin<end) {
        uintptr_t page=begin&~PS5_MASK;
        uintptr_t next=page+PS5_PAGE;
        if (next>end) next=end;
        _Alignas(8) unsigned char info[0x48]={0};
        int rc=sceKernelVirtualQuery((void *)page,0,info,sizeof(info));
        if (rc) return fail(22);
        uint32_t prot=*(uint32_t *)(info+0x18);
        if (prot || (info[0x20]&0x1f)) {
            if (!(prot&2)) return fail(13);
            for (volatile unsigned char *p=(volatile unsigned char *)begin;
                 (uintptr_t)p<next;++p) *p=0;
        }
        begin=next;
    }
    return 0;
}
void *mmap(void *address,size_t length,int protection,int flags,int fd,long offset) {
    uintptr_t begin=(uintptr_t)address;
    if (!valid_range(begin,length)) { fail(22); return (void *)-1; }
    /* The existing shim only supports anonymous memory. Reject file mappings
       rather than silently returning empty memory instead of file contents. */
    if (!(flags&0x20) || fd!=-1 || offset) { fail(95); return (void *)-1; }
    mapping_enter();
    if ((flags&0x10) && protection==0) {
        int rc=clear_released(begin,begin+length);
        mapping_leave();
        return rc ? (void *)-1 : address;
    }
    int slot=(flags&0x10) ? -1 : free_slot();
    if (!(flags&0x10) && slot<0) { mapping_leave(); fail(12); return (void *)-1; }
    int large_arena=!(flags&0x10) && protection==0 && length>=ARENA_MIN;
    int owner=-1;
    size_t rounded=(length+PS5_MASK)&~PS5_MASK;
    if (large_arena) {
        for (int i=0;i<DIRECT_OWNERS;++i) if (!direct_owners[i].size) { owner=i; break; }
        if (owner<0) { mapping_leave(); fail(12); return (void *)-1; }
    }
    void *result=waddamburo_gc_mmap_unlocked(address,length,protection,flags,fd,offset);
    if (large_arena && result!=(void *)-1) {
        int64_t physical=-1;
        int rc=sceKernelAllocateDirectMemory(0,(int64_t)sceKernelGetDirectMemorySize(),rounded,PS5_PAGE,11,&physical);
        const char *stage="allocate";
        if (!rc) {
            void *mapped=result;
            stage="map";
            rc=sceKernelMapDirectMemory(&mapped,rounded,3,0x10,physical,PS5_PAGE);
            if (!rc && mapped!=result) rc=(int)0x80020016;
            if (rc) (void)sceKernelReleaseDirectMemory(physical,rounded);
        }
        if (rc) {
            (void)sceKernelMunmap(result,rounded);
            mapping_leave();
            report_direct_failure(stage,rc,rounded);
            fail(12);
            return (void *)-1;
        }
        /* Direct allocations can contain old data. Anonymous runtime memory
           must start zero-filled, including pages the GC has not touched yet. */
        for (volatile uint64_t *q=result;(uintptr_t)q<(uintptr_t)result+rounded;++q) *q=0;
        direct_owners[owner]=(direct_owner){physical,rounded,rounded};
    }
    if (result!=(void *)-1 && slot>=0) {
        uintptr_t lo=(uintptr_t)result;
        ranges[slot]=(vm_range){lo,lo+rounded,lo+length,large_arena,large_arena ? (unsigned)owner+1 : 0};
    }
    mapping_leave();
    if (large_arena && result!=(void *)-1)
        sceKernelDebugOutText(0,"[Waddamburo GC v033] managed arena backed by CPU-cached direct memory; flexible pool preserved\n");
    return result;
}
int mprotect(void *address,size_t length,int protection) {
    uintptr_t begin=(uintptr_t)address;
    if (!valid_range(begin,length)) return fail(22);
    mapping_enter();
    int i=contains(begin,begin+length), result;
    if (i>=0 && ranges[i].arena && protection==3) {
        /* It is already backed; never remap live heap bytes. */
        uintptr_t lo=begin&~PS5_MASK,hi=(begin+length+PS5_MASK)&~PS5_MASK;
        result=sceKernelMprotect((void *)lo,hi-lo,2);
        if (result) result=fail(13);
    } else {
        result=waddamburo_gc_mprotect_unlocked(address,length,protection);
        /* Protecting a PS5 reservation can report success without backing it.
           Never let that become a successful managed commit, even if the first
           query in the emitted shim failed or returned ambiguous metadata. */
        if (!result && protection) {
            uintptr_t lo=begin&~PS5_MASK,hi=(begin+length+PS5_MASK)&~PS5_MASK;
            for (uintptr_t page=lo;page<hi;page+=PS5_PAGE) {
                _Alignas(8) unsigned char info[0x48]={0};
                if (sceKernelVirtualQuery((void *)page,0,info,sizeof(info)) ||
                    !(info[0x20]&0x1f) || !*(uint32_t *)(info+0x18)) {
                    result=fail(12);
                    break;
                }
            }
        }
    }
    mapping_leave();
    return result;
}
int madvise(void *address,size_t length,int advice) {
    uintptr_t begin=(uintptr_t)address;
    if (!valid_range(begin,length)) return fail(22);
    mapping_enter();
    int result;
    if (advice==4) result=clear_released(begin,begin+length); /* Linux DONTNEED: zero on reuse */
    else if (advice==8 || advice==16 || advice==17) result=0; /* FREE/DONTDUMP/DODUMP: optional */
    else result=waddamburo_gc_madvise_unlocked(address,length,advice);
    mapping_leave();
    return result;
}
int munmap(void *address,size_t length) {
    uintptr_t begin=(uintptr_t)address;
    if (!valid_range(begin,length)) return fail(22);
    uintptr_t end=begin+length;
    mapping_enter();
    uintptr_t lo=(begin+PS5_MASK)&~PS5_MASK,hi=end&~PS5_MASK;
    /* Free the padding of a complete short allocation too. It belongs only to
       this allocation; outward rounding is unsafe for a subrange/trim. */
    int whole=contains(begin,end);
    if (whole>=0 && begin==ranges[whole].lo && end>=ranges[whole].logical_end)
        hi=ranges[whole].hi;
    if (lo>=hi) { mapping_leave(); return 0; }
    int split=-1;
    for (int i=0;i<VM_SLOTS;++i)
        if (ranges[i].hi && lo>ranges[i].lo && hi<ranges[i].hi) {
            split=free_slot();
            if (split<0) { mapping_leave(); return fail(12); }
            break;
        }
    int native_result=sceKernelMunmap((void *)lo,hi-lo);
    int result=native_result ? fail(22) : 0;
    if (!result) for (int i=0;i<VM_SLOTS;++i) {
        vm_range old=ranges[i];
        if (!old.hi || hi<=old.lo || lo>=old.hi) continue;
        if (old.direct_id) {
            uintptr_t overlap_lo=lo>old.lo ? lo : old.lo,overlap_hi=hi<old.hi ? hi : old.hi;
            direct_owners[old.direct_id-1].live-=overlap_hi-overlap_lo;
        }
        if (lo<=old.lo && hi>=old.hi) ranges[i]=(vm_range){0};
        else if (lo<=old.lo) ranges[i].lo=hi;
        else if (hi>=old.hi) {
            ranges[i].hi=lo;
            if (ranges[i].logical_end>lo) ranges[i].logical_end=lo;
        } else {
            ranges[split]=old;
            ranges[split].lo=hi;
            ranges[i].hi=lo;
            ranges[i].logical_end=lo;
        }
    }
    /* Ordinary munmap removes the VA, not its direct physical reservation.
       Alignment trims keep the owner alive; release only after the final piece. */
    for (int i=0;i<DIRECT_OWNERS;++i) if (direct_owners[i].size && !direct_owners[i].live) {
        int rc=sceKernelReleaseDirectMemory(direct_owners[i].physical,direct_owners[i].size);
        if (!rc) direct_owners[i]=(direct_owner){0};
        else { report_direct_failure("release",rc,direct_owners[i].size); result=fail(12); }
    }
    mapping_leave();
    return result;
}

/* Preserve the native pthread ABI and error result. No allocation or stdio on
 * the failure path: even a refused stack allocation can still be reported. */
extern int __sp_device_pthread_create(void *,const void *,void *(*)(void *),void *);
extern int __sp_device_pthread_attr_getstacksize(const void *,size_t *);
extern int sceKernelConfiguredFlexibleMemorySize(size_t *);
static atomic_uint thread_create_attempts;
static char *diagnostic_text(char *p,const char *s) { while (*s) *p++=*s++; return p; }
static char *diagnostic_hex(char *p,uintptr_t n) {
    static const char digits[]="0123456789abcdef";
    *p++='0';*p++='x';
    for (int shift=60;shift>=0;shift-=4) *p++=digits[(n>>shift)&15];
    return p;
}
/* The CLR heap now uses direct memory. Report the matching physical pool in
   sysconf rather than sizing it against the small flexible-memory pool. */
int waddamburo_gc_total_memory(size_t *size) {
    *size=sceKernelGetDirectMemorySize();
    if ((intptr_t)*size>0) return 0;
    return sceKernelConfiguredFlexibleMemorySize(size);
}
int waddamburo_gc_available_memory(size_t *size) {
    size_t total=sceKernelGetDirectMemorySize();
    int64_t offset=0;
    if ((intptr_t)total>0) {
        int rc=sceKernelAvailableDirectMemorySize(0,(int64_t)total,PS5_PAGE,&offset,size);
        if (!rc) return 0;
    }
    return sceKernelAvailableFlexibleMemorySize(size);
}
static void report_direct_failure(const char *stage,int result,size_t size) {
    char buffer[192],*p=buffer;
    p=diagnostic_text(p,"[Waddamburo GC v033] direct-memory ");p=diagnostic_text(p,stage);
    p=diagnostic_text(p," failed result=");p=diagnostic_hex(p,(uint32_t)result);
    p=diagnostic_text(p," bytes=");p=diagnostic_hex(p,size);
    *p++='\n';*p=0;sceKernelDebugOutText(0,buffer);
}
void waddamburo_runtime_memory_report(void) {
    size_t configured=0,available=0;
    (void)sceKernelConfiguredFlexibleMemorySize(&configured);
    (void)sceKernelAvailableFlexibleMemorySize(&available);
    char buffer[256],*p=buffer;
    p=diagnostic_text(p,"[Waddamburo runtime v033] flexible configured=");p=diagnostic_hex(p,configured);
    p=diagnostic_text(p," available=");p=diagnostic_hex(p,available);
    p=diagnostic_text(p," direct pool=");p=diagnostic_hex(p,sceKernelGetDirectMemorySize());
    p=diagnostic_text(p," thread-create calls=");p=diagnostic_hex(p,atomic_load(&thread_create_attempts));
    *p++='\n';*p=0;sceKernelDebugOutText(0,buffer);
}
int pthread_create(void *thread,const void *attributes,void *(*start)(void *),void *argument) {
    unsigned attempt=atomic_fetch_add(&thread_create_attempts,1)+1;
    int result=__sp_device_pthread_create(thread,attributes,start,argument);
    if (result) {
        size_t stack=0,available=0;
        if (attributes) (void)__sp_device_pthread_attr_getstacksize(attributes,&stack);
        (void)sceKernelAvailableFlexibleMemorySize(&available);
        char buffer[256],*p=buffer;
        p=diagnostic_text(p,"[Waddamburo runtime v033] pthread_create FAILED result=");p=diagnostic_hex(p,(uint32_t)result);
        p=diagnostic_text(p," stack=");p=diagnostic_hex(p,stack);
        p=diagnostic_text(p," flexible available=");p=diagnostic_hex(p,available);
        p=diagnostic_text(p," attempt=");p=diagnostic_hex(p,attempt);
        *p++='\n';*p=0;sceKernelDebugOutText(0,buffer);
    }
    return result;
}

# PS5 NativeAOT memory regression checks

Run `run.sh` on Linux/WSL with .NET 10 NativeAOT, Clang/LLVM 19 and Python 3,
after building the SharpProspero binding tool. The script extracts the actual
emitted mmap/mprotect/madvise/sysconf code and links the actual gc-memory.c
against a simulated 16 KiB PS5 kernel with a separate direct physical pool.

Checks cover exact zeroing, neighbors, mixed/concurrent commits, query failures,
committed metadata, direct allocation/map failure rollback, initially dirty
physical pages, independent flexible/direct pool accounting, memory queries,
and physical owner lifetime across alignment trims and split VA mappings.
The real NativeAOT stress executable checks 200 compactions, four workers,
160000 rooted nodes, pooled/large arrays and 23200 checked finalizers.

The production GC arena (1.5 GiB, 1 GiB heap limit) now uses CPU-cached direct
memory. The console log reports a 448 MiB flexible pool, which cannot back the
configured heap. Small runtime mappings remain flexible. Logical decommit
retains cleared backing; the direct owner releases its physical allocation
when its final virtual mapping is unmapped. All GC/finalizers remain enabled.

The sibling Waddamburo.Prospero.HeapChecks project tests 768 MiB of managed
pixel buffers surviving compaction without drawing that memory from flexible
memory. Publish it with NativeAOT and run with the library produced by run.sh
in LD_LIBRARY_PATH and LD_PRELOAD (same setup as the GC stress executable).

These are host tests; the console kernel mapping/cache behavior, resource
budgets and graphics/media interaction still require PS5 verification.

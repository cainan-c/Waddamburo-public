# PS5 worker budget checks

On Linux/WSL, publish this project with .NET 10 NativeAOT, then run the published
WorkerChecks executable. It compiles the actual ProsperoThreadPool initialization
and the console runtime settings: eight workers, retained after warmup.

Checks verify eight pre-created workers, 5120 asynchronous work items with two
blocked audio-style producers, bounded decode/upload waits, and worker retention.
This tests queue progress and limits; PS5 kernel stack/thread resource limits
still require on-console verification. Native thread failure diagnostics are
tested separately in the validation archive using ASan/UBSan fault injection.

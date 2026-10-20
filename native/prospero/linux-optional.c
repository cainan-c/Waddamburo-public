/* SPDX-License-Identifier: MIT
 * Linux ABI helpers for optional .NET networking. The offline game does not use
 * these facilities; return explicit unsupported errors instead of passing Linux
 * epoll/addrinfo structures into firmware APIs with different layouts.
 */
extern int *__errno_location(void);
static int unsupported(void) { *__errno_location() = 38; return -1; }
int epoll_create1(int flags) { (void)flags; return unsupported(); }
int epoll_ctl(int fd, int op, int socket, void *event) {
    (void)fd; (void)op; (void)socket; (void)event; return unsupported();
}
int epoll_wait(int fd, void *events, int count, int timeout) {
    (void)fd; (void)events; (void)count; (void)timeout; return unsupported();
}
int accept4(int fd, void *address, void *length, int flags) {
    (void)fd; (void)address; (void)length; (void)flags; return unsupported();
}
int getaddrinfo(const char *node, const char *service, const void *hints, void **result) {
    (void)node; (void)service; (void)hints; if (result) *result = 0;
    return -2; /* Linux EAI_NONAME */
}
void freeaddrinfo(void *result) { (void)result; }
int getnameinfo(const void *address, unsigned length, char *host, unsigned host_size,
    char *service, unsigned service_size, int flags) {
    (void)address; (void)length; (void)host; (void)host_size;
    (void)service; (void)service_size; (void)flags; return -6; /* EAI_FAMILY */
}
int getdomainname(char *name, unsigned long length) {
    if (length) name[0] = 0; return unsupported();
}
unsigned if_nametoindex(const char *name) { (void)name; unsupported(); return 0; }
char *if_indextoname(unsigned index, char *name) { (void)index; (void)name; unsupported(); return 0; }
/* Linux and the SDK both use the x86_64 BSD/POSIX tm layout here. */
extern long waddamburo_native_timegm(void *tm);
long timegm(void *tm) { return waddamburo_native_timegm(tm); }

/* Firmware exports FILE* variables. The compatibility globals must contain
 * their values, rather than addresses of the exported pointer variables.
 * In particular NativeAOT's emergency logger passes stderr to fwrite. */
extern void *stdin, *stdout, *stderr;
extern void *__stdinp, *__stdoutp, *__stderrp;
__attribute__((constructor))
static void waddamburo_initialize_standard_streams(void) {
    stdin = __stdinp;
    stdout = __stdoutp;
    stderr = __stderrp;
}
/* SQLite's Unix VFS includes the failing syscall, pathname and errno in its
 * log callback. Send it directly to the kernel, without standard FILE streams. */
extern int sqlite3_config(int option, ...);
extern int sceKernelDebugOutText(int channel, const char *text);
static void waddamburo_sqlite_log(void *context, int code, const char *message) {
    (void)context; (void)code;
    sceKernelDebugOutText(0, "[Waddamburo SQLite] ");
    sceKernelDebugOutText(0, message);
    sceKernelDebugOutText(0, "\n");
}
__attribute__((constructor))
static void waddamburo_sqlite_diagnostics(void) {
    sqlite3_config(16 /* SQLITE_CONFIG_LOG */, waddamburo_sqlite_log, (void*)0);
}
/* Mesa's Unix disk cache probes unsupported identity APIs on PS5.
 * Shaders continue compiling in memory; persistence is optional. */
int disk_cache_enabled(void) { return 0; }

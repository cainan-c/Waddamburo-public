void *stdin, *stdout, *stderr;
void *__stdinp=(void*)0x1010, *__stdoutp=(void*)0x2020, *__stderrp=(void*)0x3030;
#include "../../native/prospero/linux-optional.c"
int sqlite3_config(int option, ...) { (void)option; return 0; }
int sceKernelDebugOutText(int channel, const char *text) { (void)channel; (void)text; return 0; }
void _start(void) {
    waddamburo_initialize_standard_streams();
    long failed = stdin != __stdinp || stdout != __stdoutp || stderr != __stderrp;
    __asm__ volatile("syscall" : : "a"(60L), "D"(failed) : "rcx", "r11", "memory");
    __builtin_unreachable();
}

/* Native POSIX descriptor duplication through an exported kernel API.
 * SDK dup fallback emits a raw syscall, forbidden for application modules. */
extern int __sp_device_fcntl(int fd, int command, ...);
int waddamburo_native_dup(int fd) {
    return __sp_device_fcntl(fd, 0 /* F_DUPFD */, 0);
}

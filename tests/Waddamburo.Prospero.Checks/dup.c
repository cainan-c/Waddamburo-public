#define _XOPEN_SOURCE 700
#include <fcntl.h>
#include <stdarg.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>
#include <errno.h>
#include <stdio.h>
extern int waddamburo_native_dup(int);
int __sp_device_fcntl(int fd, int command, ...) {
    va_list a; va_start(a,command);int minimum=va_arg(a,int);va_end(a);
    if(command!=0) return -1;
    return fcntl(fd,F_DUPFD,minimum);
}
int main(void) {
    char path[]="/tmp/waddamburo-dup-XXXXXX", b[4];int fd=mkstemp(path);if(fd<0)return 1;unlink(path);
    if(write(fd,"abcd",4)!=4 || lseek(fd,0,SEEK_SET)!=0)return 2;
    int other=waddamburo_native_dup(fd);if(other<0 || other==fd)return 3;
    if(read(other,b,2)!=2 || memcmp(b,"ab",2))return 4;
    if(read(fd,b,2)!=2 || memcmp(b,"cd",2))return 5;
    close(fd);if(lseek(other,0,SEEK_SET)!=0 || read(other,b,4)!=4 || memcmp(b,"abcd",4))return 6;close(other);
    if(waddamburo_native_dup(-1)!=-1 || errno!=EBADF)return 7;
    puts("PASS: duplicated descriptors share offsets, close independently, and preserve invalid-descriptor errors.");return 0;
}

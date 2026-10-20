#include "sqlite3.h"
#include <sys/stat.h>
#include <fcntl.h>
#include <stdarg.h>
#include <stdint.h>
extern int sceKernelDebugOutText(int,const char*);
extern int *__error(void);
static void text(const char *s) { sceKernelDebugOutText(0,s?s:"<null>"); }
static void number(int64_t n) { char b[32]; unsigned i=sizeof(b)-1; b[i]=0; uint64_t v=n<0?-(uint64_t)n:(uint64_t)n; do { b[--i]='0'+v%10; v/=10; }while(v); if(n<0)b[--i]='-'; text(b+i); }
static void start(const char *name,const char *path) { text("[SQLite IO] ");text(name);text(" ");if(path)text(path);text("\n"); }
static void result(int rc) { int error=*__error();text("[SQLite IO] result=");number(rc);text(" errno=");number(error);text("\n"); *__error()=error; }
static int (*realOpen)(const char*,int,...);
static int (*realStat)(const char*,struct stat*);
static int (*realFstat)(int,struct stat*);
static int (*realFcntl)(int,int,...);
static int traceOpen(const char *path,int flags,...) { int mode=0; if(flags&O_CREAT){va_list a;va_start(a,flags);mode=va_arg(a,int);va_end(a);}start("open",path);int rc=realOpen(path,flags,mode);result(rc);return rc; }
static int traceStat(const char *path,struct stat *s) { start("stat",path);int rc=realStat(path,s);result(rc);if(!rc){text("[SQLite IO] stat nlink=");number(s->st_nlink);text(" size=");number(s->st_size);text("\n");}return rc; }
static int traceFstat(int fd,struct stat *s) { start("fstat",0);text("[SQLite IO] fd=");number(fd);text("\n");int rc=realFstat(fd,s);result(rc);if(!rc){text("[SQLite IO] fstat nlink=");number(s->st_nlink);text(" size=");number(s->st_size);text("\n");}return rc; }
static int traceFcntl(int fd,int cmd,...) { start("fcntl",0);text("[SQLite IO] fd=");number(fd);text(" cmd=");number(cmd);text("\n");int rc;if(cmd==F_GETFD || cmd==F_GETFL || cmd==F_GETOWN)rc=realFcntl(fd,cmd);else{va_list a;va_start(a,cmd);uintptr_t arg=va_arg(a,uintptr_t);va_end(a);rc=realFcntl(fd,cmd,arg);}result(rc);return rc; }
static int (*realVfsOpen)(sqlite3_vfs*,const char*,sqlite3_file*,int,int*);
static int traceVfsOpen(sqlite3_vfs *v,const char *path,sqlite3_file *f,int flags,int *out) { start("VFS open BEGIN",path);int rc=realVfsOpen(v,path,f,flags,out);start("VFS open END",path);result(rc);return rc; }
void waddamburo_sqlite_trace_start(void) {
    sqlite3_vfs *v=sqlite3_vfs_find(0);if(!v || v->iVersion<3 || !v->xGetSystemCall || !v->xSetSystemCall)return;
    if(realVfsOpen)return;
    realVfsOpen=v->xOpen;v->xOpen=traceVfsOpen;
#define HOOK(name,fn,slot) slot=(void*)v->xGetSystemCall(v,name);if(slot)v->xSetSystemCall(v,name,(sqlite3_syscall_ptr)fn)
    HOOK("open",traceOpen,realOpen);HOOK("stat",traceStat,realStat);HOOK("fstat",traceFstat,realFstat);HOOK("fcntl",traceFcntl,realFcntl);
    start("native VFS tracing enabled",v->zName);
}

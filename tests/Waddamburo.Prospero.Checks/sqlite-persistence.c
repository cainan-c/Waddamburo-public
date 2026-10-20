#include <errno.h>
#include <stdio.h>
#include <sys/stat.h>
#include "sqlite3.h"
static int lstat_calls, ownership_calls;
static unsigned traceRootUid(void) { return 0; }
static int blockedOwnership(int fd, unsigned uid, unsigned gid) { (void)fd; (void)uid; (void)gid; ++ownership_calls; errno=EPERM; return -1; }
int waddamburo_blocked_lstat(const char *path, struct stat *value) {
    (void)path; (void)value; ++lstat_calls; errno=EPERM; return -1;
}
int main(int argc, char **argv) {
    sqlite3 *db=0; sqlite3_stmt *statement=0;
    if(argc!=2) return 1;
    sqlite3_vfs *vfs=sqlite3_vfs_find(0);
    int hook=vfs->xSetSystemCall(vfs,"lstat",(sqlite3_syscall_ptr)waddamburo_blocked_lstat);
    if(hook!=SQLITE_OK && hook!=SQLITE_NOTFOUND) return 8;
    vfs->xSetSystemCall(vfs,"fchown",(sqlite3_syscall_ptr)blockedOwnership);
    vfs->xSetSystemCall(vfs,"geteuid",(sqlite3_syscall_ptr)traceRootUid);
    if(sqlite3_open(argv[1],&db)!=SQLITE_OK) return 2;
    if(sqlite3_exec(db,"CREATE TABLE saved(value INTEGER); BEGIN; INSERT INTO saved VALUES(73); COMMIT;",0,0,0)!=SQLITE_OK) return 3;
    sqlite3_close(db);
    if(sqlite3_open(argv[1],&db)!=SQLITE_OK) return 4;
    if(sqlite3_prepare_v2(db,"SELECT value FROM saved",-1,&statement,0)!=SQLITE_OK) return 5;
    if(sqlite3_step(statement)!=SQLITE_ROW || sqlite3_column_int(statement,0)!=73) return 6;
    sqlite3_finalize(statement); sqlite3_close(db);
    if(lstat_calls || ownership_calls) return 7;
    puts("PASS: create, commit, close, reopen and read database while lstat is denied; no lstat or ownership calls.");
    return 0;
}

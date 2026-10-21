/* SPDX-License-Identifier: MIT -- exercise the production refresh controller. */
#include <assert.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>
#include <ps5platform/videoout.h>
int waddamburo_videoout_open(int32_t,int32_t,int32_t,const void*);
int waddamburo_ps5_set_refresh_rate(int);
static int high_error, restore_error, flip_error;
static unsigned high_calls,restore_calls,flip_calls,opens,support_calls,wait_calls;
static char last_log[256];
int sceVideoOutOpen(int32_t a,int32_t b,int32_t c,const void*d) { opens++;return 42; }
int sceVideoOutIsOutputSupported(int32_t h,uint32_t m,const void*a,const void*b,const void*c) {
    support_calls++;return 0; /* A startup probe reporting no high mode cannot veto selection. */
}
int sceVideoOutConfigureOutput(int32_t h,uint32_t m,const void*a,const void*b,const void*c) {
    assert(h==42);if(m==15){high_calls++;return high_error;}
    assert(m==1);restore_calls++;return restore_error;
}
int sceVideoOutWaitVblank(int32_t h) { wait_calls++;return -1; }
int wsi_videoout_set_flip_rate(int rate){assert(rate==0);flip_calls++;return flip_error;}
void waddamburo_ps5_log(const char *s){snprintf(last_log,sizeof(last_log),"%s",s);}
int main(void) {
    assert(waddamburo_ps5_set_refresh_rate(120)==-1);
    assert(waddamburo_videoout_open(255,0,0,0)==42&&opens==1);
    assert(waddamburo_ps5_set_refresh_rate(60)==60&&restore_calls==1&&flip_calls==1);
    assert(waddamburo_ps5_set_refresh_rate(60)==60&&restore_calls==1);
    assert(waddamburo_ps5_set_refresh_rate(120)==120&&high_calls==1);
    assert(strstr(last_log,"120 Hz mode accepted"));
    assert(waddamburo_ps5_set_refresh_rate(120)==120&&high_calls==1);
    assert(waddamburo_ps5_set_refresh_rate(60)==60&&restore_calls==2);
    assert(waddamburo_ps5_set_refresh_rate(120)==120&&high_calls==2);
    assert(support_calls==0&&wait_calls==0);
    puts("PASS explicit 120 Hz request survives unsupported startup probe/failed idle vblank; 60/120 transitions and caching");
    assert(waddamburo_ps5_set_refresh_rate(60)==60);
    high_error=-9;
    assert(waddamburo_ps5_set_refresh_rate(120)==60);
    assert(strstr(last_log,"VideoOut refused 120 Hz"));
    high_error=0;
    assert(waddamburo_ps5_set_refresh_rate(120)==120);
    puts("PASS actual VideoOut refusal falls back to 60 Hz; retry can enable 120 Hz");
    assert(waddamburo_ps5_set_refresh_rate(60)==60);
    flip_error=-8;
    unsigned previous=restore_calls;
    assert(waddamburo_ps5_set_refresh_rate(120)==-8&&restore_calls==previous+1);
    flip_error=0;
    assert(waddamburo_ps5_set_refresh_rate(120)==120);
    restore_error=-7;
    assert(waddamburo_ps5_set_refresh_rate(60)==-7);
    restore_error=0;
    assert(waddamburo_ps5_set_refresh_rate(60)==60);
    assert(waddamburo_videoout_open(255,0,0,0)==42&&opens==2);
    previous=restore_calls;
    assert(waddamburo_ps5_set_refresh_rate(60)==60&&restore_calls==previous+1);
    assert(support_calls==0&&wait_calls==0);
    puts("PASS pacing errors roll back, failed transitions retry and reopening invalidates cached mode");
    return 0;
}

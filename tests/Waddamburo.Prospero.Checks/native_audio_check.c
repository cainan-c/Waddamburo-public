/* SPDX-License-Identifier: MIT -- Exercise the native queue and mixer without console services. */
#include <assert.h>
#include <stdint.h>
#include <stdatomic.h>
#include <stdio.h>
#include <time.h>
#include <math.h>
typedef struct Voice Voice;
int waddamburo_audio_open(int);
void waddamburo_audio_pause(int),waddamburo_audio_close(void);
Voice* waddamburo_audio_stream_create(void);
void waddamburo_audio_stream_destroy(Voice*);
int waddamburo_audio_stream_queue(Voice*,const float*,int),waddamburo_audio_stream_queued(Voice*);
void waddamburo_audio_stream_clear(Voice*);
static atomic_int heard;
static atomic_int value;
int sceAudioOutInit(void) { return 0; }
int sceAudioOutOpen(int user,int type,int index,uint32_t grain,uint32_t rate,uint32_t format) {
    assert(user==0xff && type==0 && index==0 && grain==256 && rate==48000 && format==1); return 7;
}
int sceAudioOutClose(int port) { assert(port==7); return 0; }
int sceAudioOutOutput(int port,const void* data) {
    assert(port==7);
    if (data && ((const int16_t*)data)[0]) { atomic_store(&value,((const int16_t*)data)[0]); atomic_store(&heard,1); }
    struct timespec delay={0,1000000}; nanosleep(&delay,0); return 0;
}
void waddamburo_ps5_log(const char* text) { (void)text; }
int main(void) {
    assert(waddamburo_audio_open(255)<0);
    assert(waddamburo_audio_open(256)==0);
    Voice* main=waddamburo_audio_stream_create(); Voice* hit=waddamburo_audio_stream_create(); assert(main && hit);
    float music[512],drum[512]; for(int i=0;i<512;i++) { music[i]=0.25f;drum[i]=0.5f; }
    assert(waddamburo_audio_stream_queue(main,music,2048)==0);
    assert(waddamburo_audio_stream_queue(hit,drum,2048)==0);
    assert(waddamburo_audio_stream_queued(main)==2048);
    assert(waddamburo_audio_stream_queue(main,music,6)<0);
    waddamburo_audio_pause(0);
    for(int i=0;i<2000 && !atomic_load(&heard);i++) { struct timespec d={0,1000000};nanosleep(&d,0); }
    assert(atomic_load(&heard) && atomic_load(&value)==24575);
    assert(waddamburo_audio_stream_queued(main)==0 && waddamburo_audio_stream_queued(hit)==0);
    waddamburo_audio_pause(1);
    assert(waddamburo_audio_stream_queue(main,music,2048)==0);
    waddamburo_audio_stream_clear(main); assert(waddamburo_audio_stream_queued(main)==0);
    waddamburo_audio_stream_destroy(hit); waddamburo_audio_stream_destroy(main); waddamburo_audio_close();
    puts("PASS native stereo mixing, voice consumption, pause, queue validation and clear");
    return 0;
}

/* SPDX-License-Identifier: MIT -- Native AudioOut mixer, independent of managed GC. */
#include <pthread.h>
#include <stdatomic.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>
extern int sceAudioOutInit(void);
extern int sceAudioOutOpen(int,int,int,uint32_t,uint32_t,uint32_t);
extern int sceAudioOutOutput(int,const void*);
extern int sceAudioOutClose(int);
extern void waddamburo_ps5_log(const char*);
typedef struct Voice { pthread_mutex_t gate; float* samples; size_t position,count,capacity; struct Voice* next; } Voice;
static pthread_mutex_t voices_gate=PTHREAD_MUTEX_INITIALIZER;
static Voice* voices;
static pthread_t worker;
static atomic_int running,paused,failure;
static int port=-1,grain;
static void* mix_thread(void* unused) {
    (void)unused; float mixed[4096]; int16_t output[4096];
    while (atomic_load(&running)) {
        size_t count=(size_t)grain*2; memset(mixed,0,count*sizeof(float));
        if (!atomic_load(&paused)) {
            pthread_mutex_lock(&voices_gate);
            for (Voice* voice=voices;voice;voice=voice->next) {
                pthread_mutex_lock(&voice->gate);
                size_t available=voice->count-voice->position; if (available>count) available=count;
                for (size_t i=0;i<available;i++) mixed[i]+=voice->samples[voice->position+i];
                voice->position+=available; if (voice->position==voice->count) voice->position=voice->count=0;
                pthread_mutex_unlock(&voice->gate);
            }
            pthread_mutex_unlock(&voices_gate);
        }
        for (size_t i=0;i<count;i++) {
            float value=mixed[i]; if (value!=value) value=0.0f;
            if (value<-1.0f) value=-1.0f; if (value>1.0f) value=1.0f;
            output[i]=(int16_t)(value*32767.0f);
        }
        int result=sceAudioOutOutput(port,output);
        if (result<0) { atomic_store(&failure,result); atomic_store(&running,0); waddamburo_ps5_log("AudioOut failed; mixer stopped"); }
    }
    return NULL;
}
int waddamburo_audio_open(int frames) {
    if (port>=0 || frames<256 || frames>2048 || frames%256) return -1;
    (void)sceAudioOutInit(); port=sceAudioOutOpen(0xff,0,0,(uint32_t)frames,48000,1);
    if (port<0) return port;
    grain=frames; atomic_store(&failure,0); atomic_store(&paused,1); atomic_store(&running,1);
    int result=pthread_create(&worker,NULL,mix_thread,NULL);
    if (result) { atomic_store(&running,0); sceAudioOutClose(port); port=-1; return -result; }
    waddamburo_ps5_log("native AudioOut mixer started"); return 0;
}
void waddamburo_audio_pause(int pause) { atomic_store(&paused,pause!=0); }
void waddamburo_audio_close(void) {
    if (port<0) return;
    atomic_store(&running,0); pthread_join(worker,NULL); sceAudioOutOutput(port,NULL); sceAudioOutClose(port); port=-1;
}
Voice* waddamburo_audio_stream_create(void) {
    Voice* voice=calloc(1,sizeof(*voice)); if (!voice) return NULL;
    pthread_mutexattr_t attr; pthread_mutexattr_init(&attr); pthread_mutexattr_settype(&attr,PTHREAD_MUTEX_RECURSIVE);
    int result=pthread_mutex_init(&voice->gate,&attr); pthread_mutexattr_destroy(&attr);
    if (result) { free(voice); return NULL; }
    pthread_mutex_lock(&voices_gate); voice->next=voices; voices=voice; pthread_mutex_unlock(&voices_gate); return voice;
}
void waddamburo_audio_stream_destroy(Voice* voice) {
    if (!voice) return;
    pthread_mutex_lock(&voices_gate); Voice** item=&voices;
    while (*item && *item!=voice) item=&(*item)->next; if (*item) *item=voice->next;
    pthread_mutex_unlock(&voices_gate); pthread_mutex_destroy(&voice->gate); free(voice->samples); free(voice);
}
int waddamburo_audio_stream_queue(Voice* voice,const float* data,int bytes) {
    if (!voice || bytes<0 || bytes%8 || (!data && bytes)) return -1;
    if (atomic_load(&failure)) return atomic_load(&failure);
    pthread_mutex_lock(&voice->gate);
    size_t remaining=voice->count-voice->position,extra=(size_t)bytes/sizeof(float),total=remaining+extra;
    if (total>32u*1024u*1024u) { pthread_mutex_unlock(&voice->gate); return -1; }
    if (total>voice->capacity) {
        float* grown=realloc(voice->samples,total*sizeof(float));
        if (!grown) { pthread_mutex_unlock(&voice->gate); return -1; }
        voice->samples=grown; voice->capacity=total;
    }
    if (remaining) memmove(voice->samples,voice->samples+voice->position,remaining*sizeof(float));
    if (extra) memcpy(voice->samples+remaining,data,extra*sizeof(float));
    voice->position=0; voice->count=total; pthread_mutex_unlock(&voice->gate); return 0;
}
int waddamburo_audio_stream_queued(Voice* voice) {
    if (!voice) return 0; pthread_mutex_lock(&voice->gate);
    int bytes=(int)((voice->count-voice->position)*sizeof(float)); pthread_mutex_unlock(&voice->gate); return bytes;
}
void waddamburo_audio_stream_clear(Voice* voice) {
    if (!voice) return; pthread_mutex_lock(&voice->gate); voice->position=voice->count=0; pthread_mutex_unlock(&voice->gate);
}
void waddamburo_audio_stream_lock(Voice* voice) { if (voice) pthread_mutex_lock(&voice->gate); }
void waddamburo_audio_stream_unlock(Voice* voice) { if (voice) pthread_mutex_unlock(&voice->gate); }

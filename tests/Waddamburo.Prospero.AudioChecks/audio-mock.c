#include "waddamburo/media.h"
#include <stdlib.h>
#include <string.h>
static int files, callbacks, destroyed, index_value, failed;
void mock_reset(int value){files=callbacks=destroyed=index_value=0;failed=value;}
int mock_files(void){return files;} int mock_callbacks(void){return callbacks;}
int mock_destroyed(void){return destroyed;} int mock_index(void){return index_value;}
waddamburo_media_result waddamburo_media_negotiate_abi(uint32_t requested,uint32_t *version){*version=requested;return 0;}
static int open_mock(const waddamburo_media_decoder_options *options,waddamburo_media_decoder **decoder,waddamburo_media_error *error){
 index_value=(int)options->source_stream_index;
 if(failed){const char *text="Synthetic native bank rejection";error->code=5;error->message_length=strlen(text);memcpy(error->message,text,error->message_length);*decoder=NULL;return 5;}
 *decoder=(void*)malloc(1);return 0;
}
waddamburo_media_result waddamburo_media_decoder_create_file(const waddamburo_media_decoder_options *o,const char *path,waddamburo_media_decoder **d,waddamburo_media_error *e){(void)path;++files;return open_mock(o,d,e);}
waddamburo_media_result waddamburo_media_decoder_create_callbacks(const waddamburo_media_decoder_options *o,const waddamburo_media_io_callbacks *cb,waddamburo_media_decoder **d,waddamburo_media_error *e){(void)cb;++callbacks;return open_mock(o,d,e);}
waddamburo_media_result waddamburo_media_decoder_get_stream_info(waddamburo_media_decoder *d,waddamburo_media_stream_info *s){(void)d;s->sample_rate=48000;s->channels=2;s->total_frames=960;s->loop_start_frame=s->loop_end_frame=UINT64_MAX;return 0;}
waddamburo_media_result waddamburo_media_decoder_read_frames(waddamburo_media_decoder *d,float *out,uint64_t cap,uint64_t *read){(void)d;*read=cap<8?cap:8;memset(out,0,*read*2*sizeof(float));return 0;}
waddamburo_media_result waddamburo_media_decoder_seek(waddamburo_media_decoder *d,uint64_t frame){(void)d;(void)frame;return 0;}
waddamburo_media_result waddamburo_media_decoder_cancel(waddamburo_media_decoder *d){(void)d;return 0;}
waddamburo_media_result waddamburo_media_decoder_get_error(const waddamburo_media_decoder *d,waddamburo_media_error *e){(void)d;e->message_length=0;return 0;}
void waddamburo_media_decoder_destroy(waddamburo_media_decoder *d){++destroyed;free(d);}

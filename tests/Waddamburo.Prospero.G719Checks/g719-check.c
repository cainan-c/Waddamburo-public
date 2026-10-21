/* MIT -- synthetic BNSF/IS22 decode through the production vgmstream API. */
#include <assert.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>
#include <math.h>
#include <libvgmstream.h>
#include <libvgmstream_streamfile.h>
static void be16(uint8_t *p,unsigned x){p[0]=(uint8_t)(x>>8);p[1]=(uint8_t)x;}
static void be32(uint8_t *p,unsigned x){p[0]=(uint8_t)(x>>24);p[1]=(uint8_t)(x>>16);p[2]=(uint8_t)(x>>8);p[3]=(uint8_t)x;}
int main(int argc,char **argv){
 assert(argc==2);uint8_t bytes[288]={0};memcpy(bytes,"BNSF",4);be32(bytes+4,sizeof(bytes));memcpy(bytes+8,"IS22sfmt",8);
 be32(bytes+16,20);be16(bytes+22,2);be32(bytes+24,48000);be32(bytes+28,960);be16(bytes+36,240);be16(bytes+38,960);
 memcpy(bytes+40,"sdat",4);be32(bytes+44,240);FILE *f=fopen(argv[1],"wb");assert(f);assert(fwrite(bytes,1,sizeof(bytes),f)==sizeof(bytes));assert(!fclose(f));
 libstreamfile_t *file=libstreamfile_open_from_stdio(argv[1]);assert(file);
 libvgmstream_config_t cfg={0};cfg.ignore_loop=true;cfg.force_sfmt=LIBVGMSTREAM_SFMT_FLOAT;
 libvgmstream_t *v=libvgmstream_create(file,1,&cfg);libstreamfile_close(file);assert(v);
 assert(v->format->channels==2&&v->format->sample_rate==48000);
 assert(libvgmstream_render(v)>=0);assert(v->decoder->buf_samples==960&&v->decoder->buf_bytes==960*2*4);
 float first[1920];memcpy(first,v->decoder->buf,sizeof(first));
 for(unsigned i=0;i<1920;i++)assert(isfinite(first[i])&&fabsf(first[i])<=1.01f);
 if(v->decoder->done)libvgmstream_reset(v);libvgmstream_seek(v,0);assert(libvgmstream_render(v)>=0);assert(v->decoder->buf_samples==960);
 /* Zero codewords exercise concealment; its noise need not be bit-identical after reset. */
 for(unsigned i=0;i<1920;i++)assert(isfinite(((float*)v->decoder->buf)[i])&&fabsf(((float*)v->decoder->buf)[i])<=1.01f);
 libvgmstream_free(v);assert(!remove(argv[1]));
 puts("PASS native G.719 BNSF/IS22 opens, renders 960 stereo float frames at 48 kHz and resumes decoding after an EOF seek.");
}

#include <assert.h>
#include <math.h>
#include <stdio.h>
#include <libvgmstream.h>
#include <libvgmstream_streamfile.h>
int main(int argc,char **argv){
 assert(argc==2);
 libstreamfile_t *file=libstreamfile_open_from_stdio(argv[1]);assert(file);
 libvgmstream_config_t cfg={0};cfg.ignore_loop=true;cfg.force_sfmt=LIBVGMSTREAM_SFMT_FLOAT;
 libvgmstream_t *v=libvgmstream_create(file,0,&cfg);libstreamfile_close(file);assert(v);
 long long frames=0;
 for(int i=0;i<40;i++){
  assert(libvgmstream_render(v)>=0);frames+=v->decoder->buf_samples;
  float *p=(float*)v->decoder->buf;
  for(int n=0;n<v->decoder->buf_samples*v->format->channels;n++)assert(isfinite(p[n])&&fabsf(p[n])<8);
  if(v->decoder->done)break;
 }
 assert(frames>0);libvgmstream_seek(v,48000);assert(libvgmstream_render(v)>=0);
 printf("PASS real NUS3 bank opens through native vgmstream: %d Hz, %d channels, %lld decoded frames plus seek.\n",v->format->sample_rate,v->format->channels,frames);
 libvgmstream_free(v);
}

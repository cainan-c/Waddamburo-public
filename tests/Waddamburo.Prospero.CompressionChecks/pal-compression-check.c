#include <stdio.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>
typedef struct { unsigned char *in, *out; char *message; void *state; uint32_t avail_in, avail_out; } pal_stream;
extern int CompressionNative_InflateInit2_(pal_stream *, int);
extern int CompressionNative_Inflate(pal_stream *, int);
extern int CompressionNative_InflateEnd(pal_stream *);
static int wrapped_calls;
int __wrap_inflateInit2_(void *s, int bits, const char *version, int size) {
    (void)s; (void)bits; (void)version; (void)size; ++wrapped_calls; return -2;
}
int main(int argc, char **argv) {
    int conflict = argc == 5 && !strcmp(argv[4], "--expect-conflict");
    const int bits[] = {31, 15, -15};
    if (argc < 4) return 2;
    for (int k = 0; k < 3; ++k) {
        FILE *f = fopen(argv[k+1], "rb");
        if (!f) return 3;
        unsigned char input[131072], chunk[257];
        size_t length = fread(input, 1, sizeof input, f); fclose(f);
        pal_stream s = { .in=input, .avail_in=(uint32_t)length };
        int rc = CompressionNative_InflateInit2_(&s, bits[k]);
        if (conflict) {
            if (rc != -2) { fprintf(stderr,"Expected incompatible init rejection; got %d\n", rc); return 4; }
            CompressionNative_InflateEnd(&s); continue;
        }
        if (rc) { fprintf(stderr,"Init mode %d failed: %d\n", bits[k], rc); return 5; }
        size_t total = 0;
        for (int loops=0; loops<1024; ++loops) {
            s.out=chunk; s.avail_out=sizeof chunk;
            rc=CompressionNative_Inflate(&s, 0);
            size_t got=sizeof chunk-s.avail_out;
            for (size_t i=0; i<got; ++i) if (chunk[i] != (unsigned char)(total+i)) return 6;
            total+=got;
            if (rc==1) break;
            if (rc || (!got && !s.avail_in)) { fprintf(stderr,"Inflate mode %d failed: %d\n",bits[k],rc); return 7; }
        }
        int ended=CompressionNative_InflateEnd(&s);
        if (rc!=1 || total!=65792 || ended) return 8;
    }
    if (conflict && wrapped_calls!=3) return 9;
    if (!conflict && wrapped_calls) return 10;
    puts(conflict ? "PASS unisolated bridge reproduces Z_STREAM_ERROR from competing initializer in all three modes."
                  : "PASS isolated CLR bridge decodes gzip/zlib/raw fixtures in 257-byte chunks despite competing initializer.");
    return 0;
}

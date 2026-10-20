#include "waddamburo/text.h"
#include <ft2build.h>
#include FT_FREETYPE_H
#include <stdint.h>
#include <stdio.h>
#include <string.h>
static int forbidden_file_faces;
FT_Error __wrap_FT_New_Face(FT_Library lib,const char *path,FT_Long index,FT_Face *face) {
    (void)lib;(void)path;(void)index;(void)face;++forbidden_file_faces;return FT_Err_Unimplemented_Feature;
}
int main(int argc,char **argv) {
    if(argc!=2)return 1;
    waddamburo_text_error error={sizeof(error),0,0,0,{0}};
    uint8_t pixels[56*400*4];
    waddamburo_text_context *context=waddamburo_text_context_create(argv[1],&error);
    if(!context){fprintf(stderr,"context: %s (%d)\n",error.message,error.native_code);return 2;}
    for(int i=0;i<3;i++) {
        memset(pixels,0,sizeof(pixels));
        if(waddamburo_text_context_render_song_title_rgba8(context,"Font test",NULL,WADDAMBURO_TEXT_PROFILE_SONG_COMPACT,0,1,56,400,pixels,sizeof(pixels),&error)!=0)return 3;
        unsigned ink=0;for(unsigned p=3;p<sizeof(pixels);p+=4)ink+=pixels[p];if(!ink)return 4;
    }
    waddamburo_text_context_destroy(context);
    if(waddamburo_text_render_vertical_rgba8(argv[1],"TEST",56,400,pixels,sizeof(pixels),&error)!=0)return 5;
    if(waddamburo_text_context_create("/nonexistent/font.ttf",&error)!=NULL)return 6;
    if(forbidden_file_faces)return 7;
    puts("PASS: memory-backed fonts render reused contexts and vertical titles; missing files fail cleanly; FT_New_Face is never called.");return 0;
}

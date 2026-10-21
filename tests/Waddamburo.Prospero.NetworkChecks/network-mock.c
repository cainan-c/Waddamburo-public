/* MIT -- host-only model of the PS5 HTTP/TLS calls made by the production handler. */
#include <stdint.h>
#include <stddef.h>
#include <stdio.h>
#include <string.h>
#include <stdatomic.h>
#include <unistd.h>
static int mode, resources, offset, roots, tls_flags, tls_version;
static atomic_int aborted;
static int root_calls, retained_count, version_calls;
struct cert_data;
static struct cert_data **retained_roots;
static char method[32], auth[128], body[1024];
void mock_reset(int value) { mode=value; version_calls=0; root_calls=0; retained_count=0; retained_roots=NULL; offset=0; roots=0; tls_flags=0; tls_version=0; aborted=0; method[0]=auth[0]=body[0]=0; }
int mock_resources(void) { return resources; }
int mock_roots(void) { return roots; }
int mock_flags(void) { return tls_flags; }
int mock_version(void) { return tls_version; }
int mock_version_calls(void) { return version_calls; }
const char *mock_method(void) { return method; }
const char *mock_auth(void) { return auth; }
const char *mock_body(void) { return body; }
int sceSysmoduleLoadModuleInternal(unsigned id) { return 0; }
int sceNetInit(void) { return 0; }
int sceNetPoolCreate(const char*n,int bytes,int flags) { if(mode==4)return -8;resources++;return 10; }
int sceNetPoolDestroy(int id) { resources--;return 0; }
int sceSslInit(size_t bytes) { resources++;return 20; }
int sceSslTerm(int id) { resources--;return 0; }
int sceHttpInit(int net,int ssl,size_t bytes) { resources++;return 30; }
int sceHttpTerm(int id) { resources--;return 0; }
int sceHttpCreateTemplate(int ctx,const char*ua,int version,int proxy) { resources++;return 40; }
int sceHttpDeleteTemplate(int id) { resources--;return 0; }
struct cert_data { unsigned char *ptr; size_t size; };
static int valid_pem(struct cert_data *cert) {
 const char *begin="-----BEGIN CERTIFICATE-----\r\n",*end="-----END CERTIFICATE-----\r\n";
 if(!cert||!cert->ptr||cert->size<100||cert->size>16384)return 0;
 if(memcmp(cert->ptr,begin,strlen(begin))||memcmp(cert->ptr+cert->size-strlen(end),end,strlen(end)))return 0;
 for(size_t i=0;i<cert->size;i++)if(cert->ptr[i]=='\n'&&(i==0||cert->ptr[i-1]!='\r'))return 0;
 return cert->ptr[cert->size]==0;
}
int mock_root_calls(void) { return root_calls; }
const char *mock_root_at(int i) { return i>=0&&i<retained_count?(const char*)retained_roots[i]->ptr:NULL; }
static int import_pem(int count,struct cert_data **certs) {
 if(mode==5)return -456;
 if(count<1||!certs)return (int)0x8095f005;
 for(int i=0;i<count;i++)if(!valid_pem(certs[i]))return (int)0x8095f005;
 root_calls++;roots=count;retained_count=count;retained_roots=certs;return 0;
}
int sceHttpsLoadCert(int ctx,int count,struct cert_data **certs,void *cert,void *key) { return import_pem(count,certs); }
int sceSslLoadCert(int ctx,int count,struct cert_data **certs,void *cert,void *key) { return import_pem(count,certs); }
int sceHttpsEnableOption(int id,unsigned flags) { tls_flags=flags;return 0; }
int sceHttpsDisableOption(int id,unsigned flags) { tls_flags=0;return 0; }
int sceHttpsSetSslVersion(int id,int version) { version_calls++;tls_version=version;return 0; }
int sceHttpSetAutoRedirect(int id,int enable) { return enable?-1:0; }
int sceHttpSetConnectTimeOut(int id,unsigned value) { return value?0:-1; }
int sceHttpSetResolveTimeOut(int id,unsigned value) { return value?0:-1; }
int sceHttpSetRecvTimeOut(int id,unsigned value) { return value?0:-1; }
int sceHttpSetSendTimeOut(int id,unsigned value) { return value?0:-1; }
int sceHttpCreateConnectionWithURL(int ctx,const char*url,int keepalive) { resources++;return 50; }
int sceHttpDeleteConnection(int id) { resources--;return 0; }
int sceHttpCreateRequestWithURL2(int ctx,const char*m,const char*url,uint64_t length) { snprintf(method,sizeof(method),"%s",m);resources++;return 60; }
int sceHttpDeleteRequest(int id) { resources--;return 0; }
int sceHttpAddRequestHeader(int id,const char *name,const char *value,unsigned mode) {
 if(!strcmp(name,"Authorization"))snprintf(auth,sizeof(auth),"%s",value);return 0;
}
int sceHttpSendRequest(int id,const void*data,size_t size) {
 for(int i=0;i<retained_count;i++)if(!valid_pem(retained_roots[i]))return (int)0x8095f005;
 if(data&&size)snprintf(body,sizeof(body),"%.*s",(int)size,(const char*)data);
 if(mode==6)return (int)0x8095f007;
 if(mode==2)return -123;
 while(mode==1&&!atomic_load(&aborted))usleep(1000);
 return atomic_load(&aborted)?-9:0;
}
int sceHttpAbortRequest(int id) { atomic_store(&aborted,1);return 0; }
int sceHttpGetStatusCode(int id,int*status) { *status=200;return 0; }
int sceHttpReadData(int id,void*data,size_t size) {
 const char *text="{\"ok\":true}";size_t len=strlen(text);
 if(mode==3){memset(data,'a',size);return (int)size;}
 if((size_t)offset==len)return 0;
 size_t count=len-(size_t)offset;if(count>size)count=size;
 memcpy(data,text+offset,count);offset+=(int)count;return (int)count;
}
int sceHttpGetAllResponseHeaders(int id,char**headers,size_t*size) {
 static char value[]="HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nX-Host-Check: yes\r\n\r\n";
 *headers=value;*size=strlen(value);return 0;
}
int sceRandomGetRandomNumber(void *data,size_t size) { for(size_t i=0;i<size;i++)((unsigned char*)data)[i]=(unsigned char)(0xa5+i);return 0; }

int sceHttpGetLastErrno(int id,int *value) { *value=61;return 0; }
int sceHttpsGetSslError(int id,int *error,unsigned *detail) { *error=-123;*detail=0x20;return 0; }

int sceNetResolverCreate(const char *name,int pool,int flags) { resources++;return 80; }
int sceNetResolverStartNtoa(int id,const char *host,uint32_t *address,int timeout,int retry,int flags) { *address=0x0100007f;return 0; }
int sceNetResolverAbort(int id,int flags) { return 0; }
int sceNetResolverDestroy(int id) { resources--;return 0; }
int sceNetSocket(const char *name,int domain,int type,int protocol) { resources++;return 81; }
int sceNetConnect(int id,void *address,unsigned size) { return 0; }
int sceNetSocketAbort(int id,int flags) { return 0; }
int sceNetSocketClose(int id) { resources--;return 0; }
int sceSslCreateConnection(int context,int socket,const char *hostname) { resources++;return 82; }
int sceSslDeleteConnection(int id) { resources--;return 0; }
int sceSslEnableVerifyOption(int id,unsigned flags) { tls_flags=flags;return 0; }
int sceSslDisableVerifyOption(int id,unsigned flags) { tls_flags=0;return 0; }
int sceSslSetMinSslVersion(int id,unsigned short version) { version_calls++;tls_version=version;return 0; }
int sceSslConnect(int id) { return mode==2?-123:0; }

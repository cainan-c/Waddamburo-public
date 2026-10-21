using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using Waddamburo.Platform.Prospero.Native;

int checks=0;
void Check(bool pass,string text) { if(!pass)throw new Exception(text);checks++;Console.WriteLine("PASS "+text); }
// Exercise both real trust import routes with an independent host X.509 parser.
Mock.Reset(0);
using(var trust=new ProsperoTrustRoots())
{
 trust.Load(30,http:true);
 GC.Collect(); GC.WaitForPendingFinalizers();
 Check(Mock.RootCalls==1&&ValidateRoots(),"HTTP registers the complete PEM CA set once and retains parseable records after GC");
 Check(RejectLegacyDer(),"native format model rejects the previous raw DER import with 0x8095f005");
}
Mock.Reset(0);
using(var trust=new ProsperoTrustRoots())
{
 trust.Load(20,http:false);
 GC.Collect(); GC.WaitForPendingFinalizers();
 Check(Mock.RootCalls==1&&ValidateRoots(),"WebSocket TLS registers and retains the same complete parseable PEM trust set");
}
bool ValidateRoots()
{
 if(Mock.Roots<=100)return false;
 for(int i=0;i<Mock.Roots;i++)
 {
  string pem=Marshal.PtrToStringUTF8(Mock.RootAt(i))!;
  if(!pem.EndsWith("-----END CERTIFICATE-----\r\n",StringComparison.Ordinal)||pem.Replace("\r\n","").Contains('\n'))return false;
  using var certificate=System.Security.Cryptography.X509Certificates.X509Certificate2.CreateFromPem(pem);
  if(certificate.RawData.Length<100)return false;
 }
 return true;
}
unsafe bool RejectLegacyDer()
{
 string pem=Marshal.PtrToStringUTF8(Mock.RootAt(0))!;
 using var certificate=System.Security.Cryptography.X509Certificates.X509Certificate2.CreateFromPem(pem);
 byte[] der=certificate.RawData;
 fixed(byte* data=der)
 {
  var cert=new SharpProspero.Interop.Net.SceSslData{Ptr=data,Size=(nuint)der.Length};
  var ptr=&cert;
  return SharpProspero.Interop.Net.Http.sceHttpsLoadCert(30,1,&ptr,null,null)==unchecked((int)0x8095f005);
 }
}

Mock.Reset(0);
using(var http=new HttpClient(new ProsperoHttpMessageHandler()))
{
 http.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer","synthetic-token");
 using var response=await http.PostAsync("https://example.test/api",new StringContent("fixture-body"));
 Check(await response.Content.ReadAsStringAsync()=="{\"ok\":true}"&&response.Headers.GetValues("X-Host-Check").Single()=="yes","native HTTP maps response body and headers");
 Check(Mock.Method=="POST"&&Mock.Auth=="Bearer synthetic-token"&&Mock.Body=="fixture-body","native HTTP sends method, authentication and request body");
 Check(Mock.Flags==0x3d&&Mock.VersionCalls==0&&Mock.Roots>100,"firmware TLS negotiation without SDK ordinal override; CA roots, chain, hostname and validity checks enabled");
}
Check(Mock.Resources==0,"HTTP success releases every native context");
Mock.Reset(2);
using(var http=new HttpClient(new ProsperoHttpMessageHandler()))
{
 bool failed=false;try{await http.GetAsync("https://bad-cert.test/");}catch(HttpRequestException){failed=true;}
 Check(failed&&Mock.Flags==0x3d&&Mock.Resources==0,"TLS rejection fails closed and releases resources");
}
Mock.Reset(5);
using(var http=new HttpClient(new ProsperoHttpMessageHandler()))
{
 bool handled=false;
 try { await http.GetAsync("https://ca-import.test/"); }
 catch(HttpRequestException error) { handled=error.Message.Contains("CA root import")&&error.Message.Contains("fffffe38")&&error.InnerException is IOException; }
 Check(handled&&Mock.Resources==0,"CA import IO failures become handled HTTP failures with context and release resources");
}
Mock.Reset(2);
using(var http=new HttpClient(new ProsperoHttpMessageHandler()))
{
 bool diagnosed=false;
 try { await http.GetAsync("https://tls-diagnostic.test/"); }
 catch(HttpRequestException error) { diagnosed=error.Message.Contains("errno=0x0000003d")&&error.Message.Contains("verify=0x00000020"); }
 Check(diagnosed&&Mock.Resources==0,"TLS failures retain native errno and certificate verification details");
}
Mock.Reset(1);
using(var http=new HttpClient(new ProsperoHttpMessageHandler()))
using(var timeout=new CancellationTokenSource(TimeSpan.FromMilliseconds(200)))
{
 bool cancelled=false;try{await http.GetAsync("https://slow.test/",timeout.Token);}catch(OperationCanceledException){cancelled=true;}
 Check(cancelled&&Mock.Resources==0,"HTTP cancellation aborts the native request and releases resources");
}
Mock.Reset(3);
using(var http=new HttpClient(new ProsperoHttpMessageHandler()))
{
 bool limited=false;try{await http.GetAsync("https://oversize.test/");}catch(HttpRequestException){limited=true;}
 Check(limited&&Mock.Resources==0,"oversized responses are bounded and release native resources");
}
Mock.Reset(4);
using(var http=new HttpClient(new ProsperoHttpMessageHandler()))
{
 bool failed=false;try{await http.GetAsync("https://pool.test/");}catch(HttpRequestException){failed=true;}
 Check(failed&&Mock.Resources==0,"partial initialization failures release only acquired resources");
}
Mock.Reset(0);
using(var stream=await ProsperoNetworkStream.ConnectAsync(new Uri("wss://stream.test/"),false,CancellationToken.None))
 Check(Mock.VersionCalls==0&&Mock.Flags==0x3d&&Mock.Roots>100,"actual WebSocket TCP/TLS transport retains verification without the SDK version override");
Check(Mock.Resources==0,"WebSocket TCP/TLS connection teardown releases DNS, socket, TLS and pool resources");
Mock.Reset(6);
using(var http=new HttpClient(new ProsperoHttpMessageHandler()))
{
 bool handled=false;
 try { await http.GetAsync("https://invalid-value.test/"); }
 catch(HttpRequestException error) { handled=error.Message.Contains("8095f007")&&error.Message.Contains("DNS/TCP/TLS/send"); }
 Check(handled&&Mock.Resources==0,"captured native invalid-value failure is reported with stage and complete cleanup");
}
string handshake="HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: keep-alive, Upgrade\r\nSec-WebSocket-Accept: s3pPLMBiTxaQ9kYGzzhZRbK+xOo=\r\n\r\n";
ProsperoWebSocket.ValidateHandshake(handshake,"dGhlIHNhbXBsZSBub25jZQ==");
bool rejected=false;try{ProsperoWebSocket.ValidateHandshake(handshake.Replace("s3pPL","xxxxx"),"dGhlIHNhbXBsZSBub25jZQ==");}catch(WebSocketException){rejected=true;}
Check(rejected,"WebSocket RFC handshake vector passes and invalid accept values fail");
using(var wire=new FixtureStream([1,2,(byte)'h',(byte)'e',0x89,1,(byte)'?',0x80,3,(byte)'l',(byte)'l',(byte)'o',0x88,2,3,0xe8]))
using(var socket=new ProsperoWebSocket(wire))
{
 byte[] bytes=new byte[10];var first=await socket.ReceiveAsync(bytes.AsMemory());
 var second=await socket.ReceiveAsync(bytes.AsMemory(first.Count));
 Check(!first.EndOfMessage&&second.EndOfMessage&&Encoding.UTF8.GetString(bytes,0,first.Count+second.Count)=="hello","WebSocket continuation frames assemble across an interleaved ping");
 Check(wire.Sent[0]==0x8a&&(wire.Sent[1]&0x80)!=0&&(wire.Sent[6]^wire.Sent[2])=='?',"WebSocket ping produces a masked pong using native entropy");
 var close=await socket.ReceiveAsync(bytes.AsMemory());
 Check(close.MessageType==WebSocketMessageType.Close&&socket.CloseStatus==WebSocketCloseStatus.NormalClosure,"WebSocket close frames are acknowledged and reported");
}
using(var wire=new FixtureStream([]))
using(var socket=new ProsperoWebSocket(wire))
{
 await socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("hello")),WebSocketMessageType.Text,true,default);
 Check(wire.Sent[0]==0x81&&wire.Sent[1]==0x85&&wire.Sent.Skip(6).Select((b,i)=>(byte)(b^wire.Sent[2+(i&3)])).SequenceEqual(Encoding.UTF8.GetBytes("hello")),"outgoing WebSocket frames are masked and preserve their payload");
}
using(var aes=System.Security.Cryptography.Aes.Create())
{
 aes.Key=Convert.FromHexString("603deb1015ca71be2b73aef0857d77811f352c073b6108d72d9810a30914dff4");
 byte[] iv=Convert.FromHexString("000102030405060708090a0b0c0d0e0f");
 byte[] plain=Convert.FromHexString("6bc1bee22e409f96e93d7e117393172aae2d8a571e03ac9c9eb76fac45af8e51");
 byte[] cipher=aes.EncryptCbc(plain,iv);
 Check(Convert.ToHexString(cipher.AsSpan(0,32)).Equals("F58C4C04D6E5F1BA779EABFB5F7BFBD69CFC4E967EDB808D679F777BC6702C7D",StringComparison.OrdinalIgnoreCase)
   && ProsperoAes256.DecryptCbc(aes.Key,iv,cipher).SequenceEqual(plain),"console AES-256-CBC matches the NIST vector with PKCS7 padding");
 foreach(int length in new[]{0,1,15,16,31,257,1024})
 {
  plain=Enumerable.Range(0,length).Select(i=>(byte)(i*17)).ToArray();cipher=aes.EncryptCbc(plain,iv);
  Check(ProsperoAes256.DecryptCbc(aes.Key,iv,cipher).SequenceEqual(plain) && ProsperoAes256.DecryptCbcSoftware(aes.Key,iv,cipher).SequenceEqual(plain),$"hardware/software console AES asset decode handles {length}-byte plaintext");
 }
 cipher[^17]^=0x01;bool invalid=false;
 try{ProsperoAes256.DecryptCbc(aes.Key,iv,cipher);}catch(System.Security.Cryptography.CryptographicException){invalid=true;}
 Check(invalid,"console AES rejects corrupted PKCS7 padding");
}
Console.WriteLine($"{checks} network policy/protocol checks passed (host service model, not PS5 hardware).");

sealed class FixtureStream(byte[] incoming):Stream
{
 private readonly MemoryStream input=new(incoming);
 public List<byte> Sent { get; }=[];
 public override int Read(byte[] b,int o,int c)=>input.Read(b,o,c);
 public override ValueTask<int> ReadAsync(Memory<byte>b,CancellationToken t=default)=>input.ReadAsync(b,t);
 public override void Write(byte[]b,int o,int c)=>Sent.AddRange(b.Skip(o).Take(c));
 public override ValueTask WriteAsync(ReadOnlyMemory<byte>b,CancellationToken t=default){Sent.AddRange(b.ToArray());return ValueTask.CompletedTask;}
 public override bool CanRead=>true;public override bool CanWrite=>true;public override bool CanSeek=>false;
 public override long Length=>throw new NotSupportedException();public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
 public override void Flush(){}public override long Seek(long o,SeekOrigin s)=>throw new NotSupportedException();public override void SetLength(long v)=>throw new NotSupportedException();
}
static class Mock
{
 [DllImport("libSceHttp",EntryPoint="mock_root_at")]public static extern nint RootAt(int index);
 [DllImport("libSceHttp",EntryPoint="mock_root_calls")]private static extern int RootCallsNative();public static int RootCalls=>RootCallsNative();
 [DllImport("libSceHttp",EntryPoint="mock_reset")]public static extern void Reset(int mode);
 [DllImport("libSceHttp",EntryPoint="mock_resources")]private static extern int ResourcesNative();public static int Resources=>ResourcesNative();
 [DllImport("libSceHttp",EntryPoint="mock_roots")]private static extern int RootsNative();public static int Roots=>RootsNative();
 [DllImport("libSceHttp",EntryPoint="mock_flags")]private static extern int FlagsNative();public static int Flags=>FlagsNative();
 [DllImport("libSceHttp",EntryPoint="mock_version_calls")]private static extern int VersionCallsNative();public static int VersionCalls=>VersionCallsNative();
 [DllImport("libSceHttp",EntryPoint="mock_version")]private static extern int VersionNative();public static int Version=>VersionNative();
 [DllImport("libSceHttp",EntryPoint="mock_method")]private static extern nint MethodNative();public static string Method=>Marshal.PtrToStringUTF8(MethodNative())!;
 [DllImport("libSceHttp",EntryPoint="mock_auth")]private static extern nint AuthNative();public static string Auth=>Marshal.PtrToStringUTF8(AuthNative())!;
 [DllImport("libSceHttp",EntryPoint="mock_body")]private static extern nint BodyNative();public static string Body=>Marshal.PtrToStringUTF8(BodyNative())!;
}

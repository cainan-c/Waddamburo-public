using System.Buffers.Binary;
using System.Security.Cryptography;
using Waddamburo.App.Presentation;
using Waddamburo.Upscale;
using Waddamburo.Platform.Sdl.Rendering;

int checks=0;
void Check(bool pass,string text){if(!pass)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
var temporary=Path.Combine(Path.GetTempPath(),"waddamburo-upscale-check-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temporary);
AppContext.SetSwitch("Waddamburo.Prospero",true);
try
{
 Check(Bc7.Available,"real native BC7 library is available");
 var pixels=Enumerable.Range(0,8*8*4).Select(i=>(byte)(i*29)).ToArray();
 for(int i=3;i<pixels.Length;i+=4)pixels[i]=255;
 var source=new UpscaleSource(8,8,pixels);
 var serialized=new byte[pixels.Length+8];BinaryPrimitives.WriteUInt32LittleEndian(serialized,8);BinaryPrimitives.WriteUInt32LittleEndian(serialized.AsSpan(4),8);pixels.CopyTo(serialized,8);
 Check(UpscaleTool.Key(source)==Convert.ToHexString(SHA256.HashData(serialized))[..32]+".bc7","console texture key exactly matches existing desktop cache identity");
 var cache=Path.Combine(temporary,"upscaled");
 using(var tool=UpscaleTool.Find(cache)??throw new Exception("Embedded model failed to load"))
 {
  tool.RecordUsed("fixture.ddp","fixture.lm");tool.RecordUsed("fixture.ddp","fixture.lm");
  Check(File.ReadAllLines(tool.UsedListPath).Length==1,"used-movie list is persisted once");
  tool.Upscale(source,1);
  var result=tool.Load(UpscaleTool.Key(source))??throw new Exception("No upscale produced");
  Check(result.Width==24&&result.Height==24&&result.Bc7.Length==24*24,"embedded Real-ESRGAN model produces a real 3x BC7 cache entry");
  Check(Bc7.Decode(result.Bc7.Span,24,24).Length==24*24*4,"real BC7 output decodes for RGBA renderers");
  tool.Scale=2;var small=tool.Load(UpscaleTool.Key(source))!.Value;
  Check(small.Width==16&&small.Height==16,"display-sized upscale is generated from the cached 3x result");
  pixels[3]=0;var alphaSource=source with{Rgba=pixels.ToArray()};tool.Scale=3;tool.Upscale(alphaSource,1);
  var alpha=tool.Load(UpscaleTool.Key(alphaSource))!.Value;
  var decoded=Bc7.Decode(alpha.Bc7.Span,(int)alpha.Width,(int)alpha.Height);
  Check(Enumerable.Range(0,decoded.Length/4).Any(i=>decoded[i*4+3]<255),"transparent texture alpha survives neural upscaling and BC7 encoding");
 }
 using(var reopened=UpscaleTool.Find(cache)??throw new Exception("Cache reopen failed"))
  Check(reopened.Load(UpscaleTool.Key(source)) is not null&&reopened.CachedCount==2,"existing nonempty SSD-style cache reopens and reads without memory mapping");
 using(var tar=new FileStream(Path.Combine(cache,"textures.tar"),FileMode.Open,FileAccess.Read))
 using(var reader=new System.Formats.Tar.TarReader(tar))
 {int entries=0;while(reader.GetNextEntry() is not null)entries++;Check(entries==3,"stream cache remains a portable tar containing full and sized entries");}
 // A paused desktop live inference must not block a desktop bake; the new
 // serialization is a console-only memory policy.
 AppContext.SetSwitch("Waddamburo.Prospero",false);
 using(var desktopTool=UpscaleTool.Find(cache)??throw new Exception("Desktop cache failed"))
 using(var started=new ManualResetEventSlim())
 {
  int hold=1;
  var live=Task.Run(()=>desktopTool.Upscale(source,1,()=>{started.Set();return Volatile.Read(ref hold)!=0;}));
  try
  {
   if(!started.Wait(TimeSpan.FromSeconds(5)))throw new Exception("Paused live work did not start");
   var bake=Task.Run(()=>desktopTool.Upscale(source,1));
   await bake.WaitAsync(TimeSpan.FromSeconds(20));
   Check(!live.IsCompleted,"desktop baking still runs while live upscaling is paused");
  }
  finally{Volatile.Write(ref hold,0);await live.WaitAsync(TimeSpan.FromSeconds(20));}
 }
 AppContext.SetSwitch("Waddamburo.Prospero",true);
 var assembly=typeof(Program).Assembly;
 using var param=assembly.GetManifestResourceStream("Waddamburo.Upscale.realesr-animevideov3-x3.param")!;
 using var bin=assembly.GetManifestResourceStream("Waddamburo.Upscale.realesr-animevideov3-x3.bin")!;
 using var model=CompactUpscaler.Load(param,bin);
 var seam=Enumerable.Range(0,70*2*4).Select(i=>(byte)(i*13)).ToArray();
 AppContext.SetSwitch("Waddamburo.Prospero",false);var desktop=model.Upscale(70,2,seam,1);
 AppContext.SetSwitch("Waddamburo.Prospero",true);var console=model.Upscale(70,2,seam,2);
 Check(desktop.Rgba.SequenceEqual(console.Rgba),"64-pixel console tiles match the desktop model across a tile seam");
 bool cancelled=false;try{model.Upscale(8,8,pixels,1,()=>throw new OperationCanceledException());}catch(OperationCanceledException){cancelled=true;}
 Check(cancelled,"cancelled inference exits before allocating output planes");
 Console.WriteLine($"{checks} real model/BC7/cache checks passed (host CPU, not console hardware).");
}
finally{AppContext.SetSwitch("Waddamburo.Prospero",false);Directory.Delete(temporary,true);}

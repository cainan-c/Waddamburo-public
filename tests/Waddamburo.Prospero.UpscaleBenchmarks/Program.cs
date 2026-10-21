using System.Diagnostics;
using System.Numerics;
using System.Runtime.Intrinsics.X86;
using System.Security.Cryptography;
using Waddamburo.Upscale;

AppContext.SetSwitch("Waddamburo.Prospero",true);
var assembly=typeof(Program).Assembly;
using var param=assembly.GetManifestResourceStream("Waddamburo.Upscale.realesr-animevideov3-x3.param")!;
using var bin=assembly.GetManifestResourceStream("Waddamburo.Upscale.realesr-animevideov3-x3.bin")!;
using var model=CompactUpscaler.Load(param,bin);
var pixels=Enumerable.Range(0,8*8*4).Select(i=>(byte)(i*29)).ToArray();
for(int i=3;i<pixels.Length;i+=4)pixels[i]=255;
Console.WriteLine($"{args[0]}: NativeAOT, float vector lanes={Vector<float>.Count}, AVX2={Avx2.IsSupported}, FMA={Fma.IsSupported}");
var samples=new List<double>();byte[]? reference=null;
for(int i=0;i<3;i++)
{
 var clock=Stopwatch.StartNew();var result=model.Upscale(8,8,pixels,2);clock.Stop();
 if(reference is not null&&!reference.SequenceEqual(result.Rgba))throw new Exception("Repeated model output changed.");
 reference=result.Rgba;samples.Add(clock.Elapsed.TotalMilliseconds);
 Console.WriteLine($"{args[0]} sample {i+1}: {clock.Elapsed.TotalMilliseconds:F2} ms; {result.Width}x{result.Height}; SHA256={Convert.ToHexString(SHA256.HashData(result.Rgba))}");
}
samples.Sort();Console.WriteLine($"{args[0]} median: {samples[1]:F2} ms");
File.WriteAllBytes(args[1],reference!);

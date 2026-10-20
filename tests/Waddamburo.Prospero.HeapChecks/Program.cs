using System.Runtime.InteropServices;
static class Native {
 [DllImport("gc-arena-test")]internal static extern ulong gc_test_flexible_requested();
 [DllImport("gc-arena-test")]internal static extern ulong gc_test_direct_live();
}
static class Program {
 static void Main() {
  if(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes!=1073741824)throw new Exception("Wrong heap limit");
  ulong before=Native.gc_test_flexible_requested();
  var pixels=new byte[96][]; // 768 MiB: substantially more than the console flexible pool.
  for(int i=0;i<pixels.Length;i++) {
   pixels[i]=new byte[8*1024*1024];pixels[i][0]=(byte)(i+1);pixels[i][^1]=(byte)(255-i);
  }
  GC.Collect(GC.MaxGeneration,GCCollectionMode.Forced,true,true);
  for(int i=0;i<pixels.Length;i++)if(pixels[i][0]!=(byte)(i+1)||pixels[i][^1]!=(byte)(255-i)||pixels[i][4096]!=0)throw new Exception("Compacted pixel data corruption");
  ulong after=Native.gc_test_flexible_requested();
  if(after-before>64*1024*1024||Native.gc_test_direct_live()<1610612736)throw new Exception("Pixel heap consumed flexible memory");
  Console.WriteLine($"PASS 768 MiB of managed pixel buffers survive compaction with 448 MiB configured flexible memory; flexible commit requests during allocation={after-before}, direct backing={Native.gc_test_direct_live()}");
  GC.KeepAlive(pixels);
 }
}

using System.Runtime;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Buffers;
static class Native {
 [DllImport("gc-arena-test")] internal static extern int waddamburo_gc_memory_self_test();
 [DllImport("gc-arena-test")] internal static extern uint gc_test_remaps();
 [DllImport("gc-arena-test")] internal static extern uint gc_test_discards();
 [DllImport("gc-arena-test")] internal static extern uint gc_test_maps();
 [DllImport("gc-arena-test")] internal static extern uint gc_test_unmaps();
}
sealed class Finalized {
 internal static int Count,Corrupt;
 private readonly byte[] data=new byte[4000];
 internal Finalized() {data[0]=91;data[^1]=173;}
 ~Finalized() {if(data[0]!=91 || data[^1]!=173)Interlocked.Increment(ref Corrupt);Interlocked.Increment(ref Count);}
}
sealed class Node {
 internal readonly int Id;internal Node? Next;internal readonly byte[] Data;
 internal Node(int id,Node? next) {Id=id;Next=next;Data=new byte[4093+(id%19000)];Array.Fill(Data,(byte)(id%251+1));}
 internal void Validate() {byte v=(byte)(Id%251+1);foreach(byte b in Data)if(b!=v)throw new Exception("Rooted data corruption");}
}
static class Program {
 static readonly Node?[] Roots=new Node[4];
 [MethodImpl(MethodImplOptions.NoInlining)] static void Churn(int worker,int round) {
  for(int i=0;i<200;i++) {
   Roots[worker]=new Node(round*1000+i,Roots[worker]);
   if(i%7==0)_=new Finalized();
   byte[] rental=ArrayPool<byte>.Shared.Rent(8000+i*17);Array.Fill(rental,(byte)worker);ArrayPool<byte>.Shared.Return(rental);
  }
  Node? n=Roots[worker];for(int i=0;i<150&&n!=null;i++)n=n.Next;if(n!=null)n.Next=null;
  byte[] large=new byte[1000000+round*1024];large[0]=31;large[^1]=47;
  if(large[0]!=31||large[^1]!=47)throw new Exception("Large array corruption");
 }
 static void Main() {
  Console.WriteLine($"NativeAOT, 16 KiB simulated kernel; heap limit {GC.GetGCMemoryInfo().TotalAvailableMemoryBytes}");
  int rc=Native.waddamburo_gc_memory_self_test();if(rc!=0)throw new Exception($"VM self-test stage {rc}");
  for(int round=0;round<200;round++) {
   int r=round;
   Parallel.For(0,4,w=>Churn(w,r));
   GCSettings.LargeObjectHeapCompactionMode=GCLargeObjectHeapCompactionMode.CompactOnce;
   GC.Collect(GC.MaxGeneration,GCCollectionMode.Forced,true,true);
   GC.WaitForPendingFinalizers();
   for(int w=0;w<4;w++)for(Node? n=Roots[w];n!=null;n=n.Next)n.Validate();
   if(Finalized.Corrupt!=0)throw new Exception("Finalizer saw corrupt data");
   if(round%25==0)Console.WriteLine($"round {round}: finalizers={Finalized.Count}, gen2={GC.CollectionCount(2)}");
  }
  Array.Clear(Roots);GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
  if(Finalized.Count!=23200||Finalized.Corrupt!=0)throw new Exception($"Finalizer loss: {Finalized.Count}");
  Console.WriteLine($"PASS 200 compacting rounds, 4 allocation workers, 160000 rooted nodes, 23200 finalizers; remaps={Native.gc_test_remaps()}, kernel discards={Native.gc_test_discards()}, maps={Native.gc_test_maps()}, releases={Native.gc_test_unmaps()}");
  if(Native.gc_test_remaps()!=0||Native.gc_test_discards()!=0)throw new Exception("Physical managed backing replaced/discarded");
 }
}

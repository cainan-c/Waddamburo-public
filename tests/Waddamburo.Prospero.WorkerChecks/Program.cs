using Waddamburo.Platform.Prospero;
ProsperoThreadPool.Initialize();
ThreadPool.GetMinThreads(out int minimum,out _);ThreadPool.GetMaxThreads(out int maximum,out _);
if(minimum!=8||maximum!=8||ThreadPool.ThreadCount!=8)throw new Exception("Worker budget or prewarm failed");
Console.WriteLine("PASS exact eight-worker limit and all eight stacks warmed before asset work");
using var producerReady=new CountdownEvent(2);
using var stop=new ManualResetEventSlim();
Task[] producers=Enumerable.Range(0,2).Select(_=>Task.Run(()=>{producerReady.Signal();stop.Wait();})).ToArray();
if(!producerReady.Wait(TimeSpan.FromSeconds(5)))throw new Exception("Blocked producer setup failed");
var threads=new System.Collections.Concurrent.ConcurrentDictionary<int,byte>();
int completed=0;
try {
 for(int burst=0;burst<40;burst++) {
  var jobs=Enumerable.Range(0,128).Select(index=>Task.Run(async()=>{
   threads.TryAdd(Environment.CurrentManagedThreadId,0);
   await Task.Yield();
   threads.TryAdd(Environment.CurrentManagedThreadId,0);
   byte[] buffer=new byte[16384+index];buffer[0]=(byte)index;buffer[^1]=91;
   if(buffer[^1]!=91||buffer[0]!=(byte)index)throw new Exception("Work item corruption");
   Interlocked.Increment(ref completed);
  })).ToArray();
  if(!Task.WaitAll(jobs,TimeSpan.FromSeconds(5)))throw new Exception("Work queue stalled behind audio producers");
  if(ThreadPool.ThreadCount>8)throw new Exception("Worker growth exceeded cap");
 }
} finally {stop.Set();Task.WaitAll(producers);}
if(completed!=5120||threads.Count>8)throw new Exception("Burst work incomplete or oversubscribed");
Console.WriteLine($"PASS 5120 async work items finish with two blocked producers; workers={ThreadPool.ThreadCount}");
using var gates=new SemaphoreSlim(2,2);
int decoded=0,uploads=0;
Task[] prefetch=Enumerable.Range(0,80).Select(_=>Task.Run(async()=>{
 await gates.WaitAsync();
 try {await Task.Delay(1);Interlocked.Increment(ref decoded);await Task.Yield();Interlocked.Increment(ref uploads);}
 finally {gates.Release();}
})).ToArray();
if(!Task.WaitAll(prefetch,TimeSpan.FromSeconds(5))||decoded!=80||uploads!=80||ThreadPool.ThreadCount>8)throw new Exception("Bounded async prefetch stalled");
Console.WriteLine("PASS bounded upload/decode waits continue without spawning replacement workers");
var workerIds=threads.Keys.ToHashSet();
Thread.Sleep(1500);
if(ThreadPool.ThreadCount!=8)throw new Exception("Warmed workers were retired");
Console.WriteLine("PASS warmed worker retention; runtime configuration keeps stacks resident");

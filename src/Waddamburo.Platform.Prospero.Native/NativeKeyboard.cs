using System.Text;
using SharpProspero.Interop.Keyboard;
using SharpProspero.Interop.Kernel;
using SharpProspero.Platform;

namespace Waddamburo.Platform.Prospero.Native;

internal sealed unsafe class NativeKeyboard : IDisposable
{
    private readonly int _handle;
    private readonly delegate* unmanaged[Cdecl]<int,SceKeyboardData*,int> _read;
    private readonly delegate* unmanaged[Cdecl]<int,int> _close;
    private NativeKeyboard(int handle, nint read, nint close) { _handle=handle; _read=(delegate* unmanaged[Cdecl]<int,SceKeyboardData*,int>)read; _close=(delegate* unmanaged[Cdecl]<int,int>)close; }
    private static nint Resolve(int module, string name)
    {
        byte[] bytes=Encoding.UTF8.GetBytes(name+"\0");void* result=null;
        fixed(byte* p=bytes) return KernelModule.sceKernelDlsym(module,p,&result)>=0 ? (nint)result : 0;
    }
    public static NativeKeyboard? Open()
    {
        foreach(string path in new[]{"/system/common/lib/libSceKeyboard.sprx","/system_ex/common/lib/libSceKeyboard.sprx"})
        {
            byte[] bytes=Encoding.UTF8.GetBytes(path+"\0");int module;
            fixed(byte* p=bytes) module=KernelModule.sceKernelLoadStartModule(p,0,null,0,null,null);
            if(module<0) continue;
            nint init=Resolve(module,"sceKeyboardInit"),open=Resolve(module,"sceKeyboardOpen"),read=Resolve(module,"sceKeyboardReadState"),close=Resolve(module,"sceKeyboardClose");
            if(init==0 || open==0 || read==0 || close==0) continue;
            if(((delegate* unmanaged[Cdecl]<int>)init)()<0) continue;
            int handle=((delegate* unmanaged[Cdecl]<int,int,int,void*,int>)open)(Users.InitialUserId,0,0,null);
            if(handle>=0) return new(handle,read,close);
        }
        NativeHost.Log("USB keyboard service unavailable; controller input remains active");
        return null;
    }
    internal readonly record struct State(KeyModifier Modifiers, ushort[] Keys);
    public State Read()
    {
        SceKeyboardData data=default;
        if(_read(_handle,&data)<0 || !data.Connected || data.Intercepted) return new(0,[]);
        var keys=new List<ushort>();
        for(int i=0;i<Math.Clamp(data.Length,0,16);i++) if(data.KeyCode[i]!=0)keys.Add(data.KeyCode[i]);
        return new(data.ModifierKey,keys.ToArray());
    }
    public void Dispose() => _close(_handle);
}
